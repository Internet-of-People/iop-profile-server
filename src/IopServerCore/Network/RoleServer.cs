using IopCommon;
using IopServerCore.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IopServerCore.Network
{
  /// <summary>
  /// Implementation of an asynchronous TCP server with optional TLS encryption
  /// that provides services for one or more server roles.
  /// </summary>
  public class TcpRoleServer<TIncomingClient, TMessage>
    where TIncomingClient : IncomingClientBase<TMessage>
  {
    public delegate TIncomingClient ClientFactoryDelegate(TcpRoleServer<TIncomingClient, TMessage> that, TcpClient tcpClient, ulong clientId, string logPrefix);

    /// <summary>Instance logger.</summary>
    private Logger _log;

    /// <summary>Shutdown signaling object.</summary>
    public ComponentShutdown ShutdownSignaling;

    /// <summary>.NET representation of TCP server.</summary>
    public TcpListener Listener;

    /// <summary>true if the server has TLS encryption enabled.</summary>
    public bool UseTls;

    /// <summary>Specification of network interface and port that the TCP server listens on.</summary>
    public IPEndPoint EndPoint;

    /// <summary>One or more roles of the server.</summary>
    public uint Roles;

    /// <summary>true if the TCP server is running, false otherwise.</summary>
    public bool IsRunning = false;


    /// <summary>Internal server ID that is formed of server roles. It is used as the base of message numbering.</summary>
    public uint IdBase;

    /// <summary>Event that is set when acceptThread is not running.</summary>
    private ManualResetEvent _acceptThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for the new clients to connect to the TCP server port.</summary>
    private Thread _acceptThread;


    /// <summary>Queue of clients, which is produced by acceptThread and consumed by clientQueueHandlerThread.</summary>
    private Queue<TcpClient> _clientQueue = new Queue<TcpClient>();
    
    /// <summary>Synchronization object for exclusive access to clientQueue.</summary>
    private object _clientQueueLock = new object();

    /// <summary>Event that is set when a new client has been added to clientQueue.</summary>
    private AutoResetEvent _clientQueueEvent = new AutoResetEvent(false);


    /// <summary>Event that is set when clientQueueHandlerThread is not running.</summary>
    private ManualResetEvent _clientQueueHandlerThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for handling new clients that were accepted by acceptThread.</summary>
    private Thread _clientQueueHandlerThread;

    /// <summary>List of server's network peers and clients.</summary>
    private IncomingClientList<TMessage> _clients;

    /// <summary>Pointer to the Network.Server component.</summary>
    private ServerBase<TIncomingClient, TMessage> _server;

    private ClientFactoryDelegate _clientFactory;


    /// <summary>Number of milliseconds after which the server's client is considered inactive and its connection can be terminated.</summary>
    public int ClientKeepAliveTimeoutMs;


    /// <summary>
    /// Creates a new TCP server to listen on specific IP address and port.
    /// </summary>
    /// <param name="bindTo">IP address of the interface on which the TCP server should listen. IPAddress.Any is a valid value.</param>
    /// <param name="config">Configured parameters for the role server (port, TLS, roles, timeout, etc.)</param>
    public TcpRoleServer(ClientFactoryDelegate clientFactory, IPAddress bindTo, RoleServerConfiguration config)
    {
      this.UseTls = config.Encrypted;
      this.Roles = config.Roles;
      this.EndPoint = new IPEndPoint(bindTo, config.Port);
      this.ClientKeepAliveTimeoutMs = config.ClientKeepAliveTimeoutMs;
      this._clientFactory = clientFactory;

      string logPrefix = string.Format("[{0}/tcp{1}] ", EndPoint.Port, UseTls ? "_tls" : "");
      _log = new Logger("IopServerCore.Network.TcpRoleServer", logPrefix);
      _log.Trace("(EndPoint:'{0}',UseTls:{1},Roles:{2},ClientKeepAliveTimeoutMs:{3})", EndPoint, UseTls, Roles, ClientKeepAliveTimeoutMs);

      ShutdownSignaling = new ComponentShutdown(Base.ComponentManager.GlobalShutdown);

      _server = (ServerBase<TIncomingClient, TMessage>)Base.ComponentDictionary[ServerBase<TIncomingClient, TMessage>.ComponentName];
      _clients = _server.Clients;

      IsRunning = false;
      Listener = new TcpListener(this.EndPoint);
      Listener.Server.LingerState = new LingerOption(true, 0);
      Listener.Server.NoDelay = true;

      IdBase = ((uint)Roles << 24);
      _log.Trace("(-)");
    }

    /// <summary>
    /// <para>Starts the TCP server listener and starts client thread handlers.</para>
    /// <para>If the application is restarted, it may be the case that the TCP port 
    /// is unusable for a short period of time. This method repeatedly tries to reuse that port until it succeeds 
    /// or until 10 unsuccessful attempts are reached.</para>
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise</returns>
    public bool Start()
    {
      _log.Info("(Roles:[{0}])", this.Roles);

      int tryCounter = 0;
      bool res = false;
      while (tryCounter < 10)
      {
        try
        {
          this.Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
          this.Listener.Start();
          res = true;
          break;
        }
        catch (SocketException se)
        {
          _log.Info("Socket error code {0} occurred while trying to reuse socket: {1}.", se.SocketErrorCode, se.ToString());
        }

        int waitTime = tryCounter * 3;
        _log.Info("Will wait {0} seconds and then try again.", waitTime);
        Thread.Sleep(waitTime * 1000);
        tryCounter++;
      }

      if (res)
      {
        _clientQueueHandlerThread = new Thread(new ThreadStart(ClientQueueHandlerThread));
        _clientQueueHandlerThread.Start();

        _acceptThread = new Thread(new ThreadStart(AcceptThread));
        _acceptThread.Start();

        IsRunning = true;
      }

      _log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Stops TCP server and frees resources associated with it.
    /// </summary>
    public void Stop()
    {
      _log.Info("()");

      ShutdownSignaling.SignalShutdown();

      try
      {
        Listener.Stop();

        if ((_clientQueueHandlerThread != null) && !_clientQueueHandlerThreadFinished.WaitOne(10000))
          _log.Error("Client queue handler thread did not terminated in 10 seconds.");

        if ((_acceptThread != null) && !_acceptThreadFinished.WaitOne(10000))
          _log.Error("Accept thread did not terminated in 10 seconds.");

        lock (_clientQueueLock)
        {
          _log.Info("Closing {0} clients from new clients queue.", _clientQueue.Count);
          while (_clientQueue.Count > 0)
          {
            TcpClient client = _clientQueue.Dequeue();
            NetworkStream stream = client.GetStream();
            if (stream != null) stream.Dispose();
            client.Dispose();
          }
        }
      }
      catch (Exception e)
      {
        _log.Error("Exception occurred: {0}", e.ToString());
      }

      _log.Info("(-)");
    }


    /// <summary>
    /// Thread procedure that is responsible for accepting new clients on the TCP server port.
    /// New clients are put into clientQueue, from which they are consumed by clientQueueHandlerThread.
    /// </summary>
    private void AcceptThread()
    {
      LogDiagnosticContext.Start();

      _log.Trace("()");

      _acceptThreadFinished.Reset();

      AutoResetEvent acceptTaskEvent = new AutoResetEvent(false);

      while (!ShutdownSignaling.IsShutdown)
      {
        _log.Debug("Waiting for new client.");
        Task<TcpClient> acceptTask = Listener.AcceptTcpClientAsync();
        acceptTask.ContinueWith(t => acceptTaskEvent.Set());

        WaitHandle[] handles = new WaitHandle[] { acceptTaskEvent, ShutdownSignaling.ShutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == ShutdownSignaling.ShutdownEvent)
        {
          _log.Info("Shutdown detected.");
          break;
        }

        try
        {
          // acceptTask is finished here, asking for Result won't block.
          TcpClient client = acceptTask.Result;
          EndPoint ep = client.Client.RemoteEndPoint;
          lock (_clientQueueLock)
          {
            _clientQueue.Enqueue(client);
          }
          _log.Debug("New client '{0}' accepted.", ep);
          _clientQueueEvent.Set();
        }
        catch (Exception e)
        {
          _log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      _acceptThreadFinished.Set();

      _log.Trace("(-)");

      LogDiagnosticContext.Stop();
    }


    /// <summary>
    /// Thread procedure that consumes clients from clientQueue. 
    /// When a new client is detected in the queue, it is removed from the queue 
    /// and enters asynchronous read and processing loop.
    /// </summary>
    private void ClientQueueHandlerThread()
    {
      _log.Info("()");

      _clientQueueHandlerThreadFinished.Reset();

      while (!ShutdownSignaling.IsShutdown)
      {
        WaitHandle[] handles = new WaitHandle[] { _clientQueueEvent, ShutdownSignaling.ShutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == ShutdownSignaling.ShutdownEvent)
        {
          _log.Info("Shutdown detected.");
          break;
        }

        _log.Debug("New client in the queue detected, queue count is {0}.", _clientQueue.Count);
        bool queueEmpty = false;
        while (!queueEmpty && !ShutdownSignaling.IsShutdown)
        {
          TcpClient tcpClient = null;
          lock (_clientQueueLock)
          {
            if (_clientQueue.Count > 0)
              tcpClient = _clientQueue.Peek();
          }

          if (tcpClient != null)
          {
            ulong clientId = _clients.GetNewClientId();
            string logPrefix = string.Format("[{0}<=>{1}|{2}] ", EndPoint, tcpClient.Client.RemoteEndPoint, clientId.ToHex());

            TIncomingClient client = _clientFactory(this, tcpClient, clientId, logPrefix);
            ClientHandlerAsync(client);

            lock (_clientQueueLock)
            {
              _clientQueue.Dequeue();
              queueEmpty = _clientQueue.Count == 0;
            }
          }
          else queueEmpty = true;
        }
      }

      _clientQueueHandlerThreadFinished.Set();

      _log.Info("(-)");
    }


    /// <summary>
    /// Handler for each client that connects to the TCP server.
    /// </summary>
    /// <param name="client">Client that is connected to TCP server.</param>
    /// <remarks>The client is being handled in the processing loop until the connection to it is terminated by either side.</remarks>
    private async void ClientHandlerAsync(IncomingClientBase<TMessage> client)
    {
      LogDiagnosticContext.Start();

      _log.Info("(Client.RemoteEndPoint:{0})", client.RemoteEndPoint);

      _clients.AddNetworkPeer(client);
      _log.Debug("Client ID set to {0}.", client.Id.ToHex());

      await client.ReceiveMessageLoop();

      // Free resources used by the client.
      _clients.RemoveNetworkPeer(client);
      await client.HandleDisconnect();
      client.Dispose();

      _log.Info("(-)");

      LogDiagnosticContext.Stop();
    }
  }
}
