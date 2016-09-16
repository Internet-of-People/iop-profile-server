using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HomeNet.Kernel;
using System.Text;
using HomeNetProtocol;
using System.Runtime.InteropServices;
using System.Net.Security;
using System.Security.Authentication;
using Iop.Homenode;
using System.IO;

namespace HomeNet.Network
{
  /// <summary>
  /// Types of server roles that can each be served on different port.
  /// Some of the roles are served unencrypted, others are encrypted.
  /// </summary>
  /// <remarks>If more than 8 different values are needed, consider changing clientLastId initialization in TcpRoleServer constructore.</remarks>
  [Flags]
  public enum ServerRole
  {
    /// <summary>Primary and Unrelated Nodes Interface server role.</summary>
    PrimaryUnrelated = 1,

    /// <summary>Neighbors Interface server role.</summary>
    NodeNeighbor = 2,

    /// <summary>Colleagues Interface server role.</summary>
    NodeColleague = 4,

    /// <summary>Customer Clients Interface server role.</summary>
    ClientCustomer = 64,

    /// <summary>Non Customer Clients Interface server role.</summary>
    ClientNonCustomer = 128
  }


  /// <summary>
  /// Implementation of an asynchronous TCP server with optional TLS encryption
  /// that provides services for one or more server roles.
  /// </summary>
  public class TcpRoleServer
  {
    private PrefixLogger log;

    /// <summary>
    /// Time in seconds for which a remote client is allowed not to send any request to the node in the open connection.
    /// If the node detects an open connection to the client without any request for more than this value,
    /// the node will close the connection.
    /// </summary>
    public const int ClientKeepAliveIntervalSeconds = 60;

    /// <summary>
    /// Time in seconds for which a remote node is allowed not to send any request to the node in the open connection.
    /// If the node detects an open connection to other node without any request for more than this value,
    /// the node will close the connection.
    /// </summary>
    public const int NodeKeepAliveIntervalSeconds = 300;


    /// <summary>
    /// Provides information about which server roles are operating encrypted and which are unencrypted.
    /// </summary>
    public static Dictionary<ServerRole, bool> ServerRoleEncryption = new Dictionary<ServerRole, bool>()
    {
      { ServerRole.PrimaryUnrelated,  false },
      { ServerRole.NodeNeighbor,      false },
      { ServerRole.NodeColleague,     false },
      { ServerRole.ClientCustomer,    true  },
      { ServerRole.ClientNonCustomer, true  },
    };

    /// <summary>
    /// Provides information about which server roles are for nodes and which are for clients.
    /// true in this table means that the connection is either unknown (primary) or for nodes.
    /// </summary>
    public static Dictionary<ServerRole, bool> ServerRoleForNodes = new Dictionary<ServerRole, bool>()
    {
      { ServerRole.PrimaryUnrelated,  true  },
      { ServerRole.NodeNeighbor,      true  },
      { ServerRole.NodeColleague,     true  },
      { ServerRole.ClientCustomer,    false },
      { ServerRole.ClientNonCustomer, false },
    };


    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the TCP server is stopped.</summary>
    private CancellationTokenSource shutdownCancellationTokenSource = new CancellationTokenSource();

    /// <summary>Internal task that completes once shutdownEvent is set.</summary>
    private Task shutdownTask;

    /// <summary>Internal event that is set when the TCP server shutdown is initiated.</summary>
    private ManualResetEvent shutdownEvent = new ManualResetEvent(false);

    /// <summary>true if the TCP server was stopped, false otherwise.</summary>
    private bool isShutdown = false;

    /// <summary>.NET representation of TCP server.</summary>
    public TcpListener Listener;

    /// <summary>true if the server has TLS encryption enabled.</summary>
    public bool UseTls;

    /// <summary>Specification of network interface and port that the TCP server listens on.</summary>
    public IPEndPoint EndPoint;

    /// <summary>One or more roles of the server.</summary>
    public ServerRole Roles;

    /// <summary>true if the TCP server is running, false otherwise.</summary>
    public bool IsRunning = false;


    /// <summary>true if the server is serving only end user device clients and not nodes.</summary>
    public bool IsServingClientsOnly = false;



    /// <summary>Event that is set when acceptThread is not running.</summary>
    private ManualResetEvent acceptThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for the new clients to connect to the TCP server port.</summary>
    private Thread acceptThread;


    /// <summary>Queue of clients, which is produced by acceptThread and consumed by clientQueueHandlerThread.</summary>
    private Queue<TcpClient> clientQueue = new Queue<TcpClient>();
    
    /// <summary>Synchronization object for exclusive access to clientQueue.</summary>
    private object clientQueueLock = new object();

    /// <summary>Event that is set when a new client has been added to clientQueue.</summary>
    private AutoResetEvent clientQueueEvent = new AutoResetEvent(false);


    /// <summary>Event that is set when clientQueueHandlerThread is not running.</summary>
    private ManualResetEvent clientQueueHandlerThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for handling new clients that were accepted by acceptThread.</summary>
    private Thread clientQueueHandlerThread;



    /// <summary>Lock object for synchronized access to clientList and clientLastId.</summary>
    private object clientListLock = new object();

    /// <summary>List of server's clients indexed by client ID.</summary>
    private Dictionary<uint, Client> clientList = new Dictionary<uint, Client>();

    /// <summary>Server assigned client identifier for internal client maintanence purposes.</summary>
    private uint clientLastId = 0;



    /// <summary>Log message prefix to help to distinguish between different instances of this class.</summary>
    private string logPrefix;

    /// <summary>Name of the class logger.</summary>
    private string logName;



    /// <summary>
    /// Creates a new TCP server to listen on specific IP address and port.
    /// </summary>
    /// <param name="Interface">IP address of the interface on which the TCP server should listen. IPAddress.Any is a valid value.</param>
    /// <param name="Port">TCP port on which the TCP server should listen.</param>
    /// <param name="UseTls">Indication of whether to use TLS for this TCP server.</param>
    /// <param name="Roles">One or more roles of this server.</param>
    public TcpRoleServer(IPAddress Interface, int Port, bool UseTls, ServerRole Roles) :
      this(new IPEndPoint(Interface, Port), UseTls, Roles)
    {      
    }

    /// <summary>
    /// Creates a new TCP server to listen on specific IP endpoint.
    /// </summary>
    /// <param name="EndPoint">Specification of the interface and TCP port on which the TCP server should listen. IPAddress.Any is a valid value for the interface.</param>
    /// <param name="UseTls">Indication of whether to use TLS for this TCP server.</param>
    /// <param name="Roles">One or more roles of this server.</param>
    public TcpRoleServer(IPEndPoint EndPoint, bool UseTls, ServerRole Roles)
    {
      this.IsRunning = false;
      this.UseTls = UseTls;
      this.Roles = Roles;
      this.EndPoint = EndPoint;
      this.Listener = new TcpListener(this.EndPoint);
      this.Listener.Server.LingerState = new LingerOption(true, 0);
      this.Listener.Server.NoDelay = true;

      // We want to determine what types of clients do connect to this server.
      // This information is stored in ServerRoleForNodes dictionary.
      // If ServerRoleForNodes[R] is true for a role R, it means that that role is intended for nodes.
      // Thus if this server roles only consist of roles, for which ServerRoleForNodes[x] is false,
      // it means that the server is intended for clients use.
      this.IsServingClientsOnly = true;
      foreach (ServerRole role in Enum.GetValues(typeof(ServerRole)))
      {
        if (this.Roles.HasFlag(role) && ServerRoleForNodes[role])
        {
          this.IsServingClientsOnly = false;
          break;
        }
      }

      // Initialize last client ID to have different values for each role server.
      clientLastId = ((uint)Roles << 24) + 1;


      logPrefix = string.Format("[{0}/tcp{1}] ", this.EndPoint.Port, this.UseTls ? "_tls" : "");
      logName = "HomeNet.Network.RoleServer";
      this.log = new PrefixLogger(logName, logPrefix);

      shutdownTask = WaitHandleExtension.AsTask(shutdownEvent);
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
      log.Info("(Roles:[{0}])", this.Roles);

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
          log.Info("Socket error code {0} occurred while trying to reuse socket: {1}.", se.SocketErrorCode, se.ToString());
        }

        int waitTime = tryCounter * 3;
        log.Info("Will wait {0} seconds and then try again.", waitTime);
        Thread.Sleep(waitTime * 1000);
        tryCounter--;
      }

      if (res)
      {
        clientQueueHandlerThread = new Thread(new ThreadStart(ClientQueueHandlerThread));
        clientQueueHandlerThread.Start();

        acceptThread = new Thread(new ThreadStart(AcceptThread));
        acceptThread.Start();

        IsRunning = true;
      }

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Stops TCP server and frees resources associated with it.
    /// </summary>
    public void Stop()
    {
      log.Info("()");

      isShutdown = true;
      shutdownEvent.Set();
      shutdownTask.Wait();
      shutdownCancellationTokenSource.Cancel();

      try
      {
        Listener.Stop();

        if ((clientQueueHandlerThread != null) && !clientQueueHandlerThreadFinished.WaitOne(10000))
          log.Error("Client queue handler thread did not terminated in 10 seconds.");

        if ((acceptThread != null) && !acceptThreadFinished.WaitOne(10000))
          log.Error("Accept thread did not terminated in 10 seconds.");

        lock (clientQueueLock)
        {
          log.Info("Closing {0} clients from new clients queue.", clientQueue.Count);
          while (clientQueue.Count > 0)
          {
            TcpClient client = clientQueue.Dequeue();
            NetworkStream stream = client.GetStream();
            if (stream != null) stream.Dispose();
            client.Dispose();
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Info("(-)");
    }


    /// <summary>
    /// Thread procedure that is responsible for accepting new clients on the TCP server port.
    /// New clients are put into clientQueue, from which they are consumed by clientQueueHandlerThread.
    /// </summary>
    private void AcceptThread()
    {
      log.Info("()");

      acceptThreadFinished.Reset();

      while (!isShutdown)
      {
        log.Info("Waiting for new client.");
        Task<TcpClient> acceptTask = Listener.AcceptTcpClientAsync();

        Task[] tasks = new Task[] { acceptTask, shutdownTask };
        int index = Task.WaitAny(tasks);
        if (tasks[index] == shutdownTask)
        {
          log.Info("Shutdown detected.");
          break;
        }

        try
        {
          TcpClient client = acceptTask.Result;
          lock (clientQueueLock)
          {
            clientQueue.Enqueue(client);
          }
          log.Info("New client '{0}' accepted.", client.Client.RemoteEndPoint);
          clientQueueEvent.Set();
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      acceptThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Thread procedure that consumes clients from clientQueue. 
    /// When a new client is detected in the queue, it is removed from the queue 
    /// and enters asynchronous read and processing loop.
    /// </summary>
    private void ClientQueueHandlerThread()
    {
      log.Info("()");

      clientQueueHandlerThreadFinished.Reset();

      while (!isShutdown)
      {
        WaitHandle[] handles = new WaitHandle[] { shutdownEvent, clientQueueEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == shutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        log.Debug("New client in the queue detected, queue count is {0}.", clientQueue.Count);
        bool queueEmpty = false;
        while (!queueEmpty && !isShutdown)
        {
          TcpClient tcpClient = null;
          lock (clientQueueLock)
          {
            if (clientQueue.Count > 0)
              tcpClient = clientQueue.Peek();
          }

          if (tcpClient != null)
          {
            int keepAliveInterval = IsServingClientsOnly ? ClientKeepAliveIntervalSeconds : NodeKeepAliveIntervalSeconds;
            Client client = new Client(tcpClient, UseTls, keepAliveInterval);
            ClientHandlerAsync(client);

            lock (clientQueueLock)
            {
              clientQueue.Dequeue();
              queueEmpty = clientQueue.Count == 0;
            }
          }
          else queueEmpty = true;
        }
      }

      clientQueueHandlerThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Asynchronous read and processing function for each client that connects to the TCP server.
    /// </summary>
    /// <param name="Client">Client that is connected to TCP server.</param>
    /// <remarks>The client is being handled in the processing loop until the connection to it 
    /// is terminated by either side. This function implements reading the message from the network stream,
    /// which includes reading the message length prefix followed by the entire message.</remarks>
    private async void ClientHandlerAsync(Client Client)
    {
      this.log.Info("(Client.RemoteEndPoint:{0})", Client.RemoteEndPoint);

      string prefix = string.Format("{0}[{1}] ", this.logPrefix, Client.RemoteEndPoint);
      PrefixLogger log = new PrefixLogger(this.logName, prefix);

      AddClientToClientList(Client);
      log.Debug("Client ID set to 0x{0:X8}.", Client.Id);

      try
      {
        if (UseTls)
        {
          SslStream sslStream = (SslStream)Client.Stream;
          await sslStream.AuthenticateAsServerAsync(Base.Configuration.TcpServerTlsCertificate, false, SslProtocols.Tls12, false);
        }

        byte[] messageHeaderBuffer = new byte[Utils.HeaderSize];
        byte[] messageBuffer = null;
        ClientStatus clientStatus = ClientStatus.ReadingHeader;
        uint messageSize = 0;
        int messageHeaderBytesRead = 0;
        int messageBytesRead = 0;

        bool disconnect = false;
        while (!isShutdown && !disconnect)
        {
          Task<int> readTask = null;
          int remain = 0;

          log.Trace("Client status is '{0}'.", clientStatus);
          switch (clientStatus)
          {
            case ClientStatus.ReadingHeader:
              {
                remain = Utils.HeaderSize - messageHeaderBytesRead;
                readTask = Client.Stream.ReadAsync(messageHeaderBuffer, messageHeaderBytesRead, remain, shutdownCancellationTokenSource.Token);
                break;
              }

            case ClientStatus.ReadingBody:
              {
                remain = (int)messageSize - messageBytesRead;
                readTask = Client.Stream.ReadAsync(messageBuffer, messageBytesRead, remain, shutdownCancellationTokenSource.Token);
                break;
              }

            default:
              log.Error("Invalid client status '{0}'.", clientStatus);
              break;
          }

          if (readTask == null)
            break;

          log.Trace("{0} bytes remains to be read.", remain);

          int readAmount = await readTask;
          if (readAmount == 0)
          {
            log.Info("Connection has been closed.");
            break;
          }

          log.Trace("Read completed: {0} bytes.", readAmount);

          switch (clientStatus)
          {
            case ClientStatus.ReadingHeader:
              {
                messageHeaderBytesRead += readAmount;
                if (readAmount == remain)
                {
                  uint hdr = Utils.GetValueLittleEndian(messageHeaderBuffer);
                  if (hdr + Utils.HeaderSize <= Utils.MaxSize)
                  {
                    messageSize = hdr;
                    clientStatus = ClientStatus.ReadingBody;
                    messageBuffer = new byte[messageSize];
                    log.Trace("Reading of message header completed. Message size is {0} bytes.", messageSize);
                  }
                  else
                  {
                    log.Warn("Client claimed message of size {0} which exceeds the maximum.", hdr + Utils.HeaderSize);
                    disconnect = true;
                  }
                }
                break;
              }

            case ClientStatus.ReadingBody:
              {
                messageBytesRead += readAmount;
                if (readAmount == remain)
                {
                  clientStatus = ClientStatus.ReadingHeader;
                  messageBytesRead = 0;
                  messageHeaderBytesRead = 0;
                  log.Debug("Reading of message size {0} completed.", messageSize);

                  /*await*/ ProcessMessageAsync(messageBuffer, Client);
                }
                break;
              }
          }
        }
      }
      catch (Exception e)
      {
        if ((e is ObjectDisposedException) || (e is IOException)) log.Info("Connection to client has been terminated.");
        else log.Error("Exception occurred: {0}", e.ToString());
      }

      RemoveClientFromClientList(Client);

      Client.Dispose();

      log.Info("(-)");
    }


    /// <summary>
    /// Assigns ID to a client and safely adds the client to the clientList. 
    /// </summary>
    /// <param name="Client">Client to add.</param>
    public void AddClientToClientList(Client Client)
    {
      log.Trace("()");

      Client.Id = clientLastId;
      lock (clientListLock)
      {
        clientList.Add(Client.Id, Client);
        clientLastId++;
      }
      log.Trace("(-)");
    }

    /// <summary>
    /// Safely removes client from clientList.
    /// </summary>
    /// <param name="Client">Client to remove.</param>
    public void RemoveClientFromClientList(Client Client)
    {
      log.Trace("()");

      uint id = 0;
      try
      {
        id = Client.Id;
        lock (clientListLock)
        {
          clientList.Remove(Client.Id);
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while removing client ID {0} from client list: {1}", id, e.ToString());
      }

      log.Trace("(-)");
    }





    /// <summary>
    /// Creates a copy of list of existing clients that are connected to the servers.
    /// </summary>
    /// <returns>List of clients.</returns>
    public List<Client> GetClientListCopy()
    {
      log.Trace("()");
      List<Client> res = new List<Client>();

      lock (clientListLock)
      {
        foreach (Client client in clientList.Values)
          res.Add(client);
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Asynchronous processing of a message received from a client.
    /// </summary>
    /// <param name="Data">Full ProtoBuf message to be processed.</param>
    /// <param name="Client">TCP client who send the message.</param>
    public /*async*/ void ProcessMessageAsync(byte[] Data, Client Client)
    {
      string prefix = string.Format("{0}[{1}] ", this.logPrefix, Client.RemoteEndPoint);
      PrefixLogger log = new PrefixLogger(this.logName, prefix);

      log.Debug("()");
      try
      {
        // Update time until this client's connection is considered inactive.
        Client.NextKeepAliveTime = DateTime.UtcNow.AddSeconds(Client.KeepAliveIntervalSeconds);
        log.Trace("Client ID 0x{0:X8} NextKeepAliveTime updated to {1}.", Client.Id, Client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));

        Message message = Message.Parser.ParseFrom(Data);
        log.Trace("Message type is {0}, message ID is {1}.", message.MessageTypeCase, message.Id);
        switch (message.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              Message responseMessage = new Message();
              responseMessage.Id = message.Id;

              Request request = message.Request;
              log.Trace("Request conversation type is {0}.", request.ConversationTypeCase);

              responseMessage.Response = new Response();
              Response response = responseMessage.Response;
              response.Status = Status.ErrorProtocolViolation;

              switch (request.ConversationTypeCase)
              {
                case Request.ConversationTypeOneofCase.SingleRequest:
                  {
                    response.SingleResponse = new SingleResponse();

                    SingleRequest singleRequest = request.SingleRequest;
                    log.Trace("Single request type is {0}, version is {1}.", singleRequest.RequestTypeCase, Utils.VersionBytesToString(singleRequest.Version.ToArray()));
                    switch (singleRequest.RequestTypeCase)
                    {
                      case SingleRequest.RequestTypeOneofCase.Ping:
                        {
                          PingRequest pingRequest = singleRequest.Ping;

                          response.Status = Status.Ok;
                          response.SingleResponse.Ping = new PingResponse();
                          response.SingleResponse.Ping.Clock = Utils.GetUnixTimestamp();
                          response.SingleResponse.Ping.Payload = pingRequest.Payload;
                          break;
                        }

                      default:
                        log.Error("Unknown request type '{0}'.", singleRequest.RequestTypeCase);
                        break;
                    }

                    break;
                  }

                case Request.ConversationTypeOneofCase.ConversationRequest:
                  {
                    log.Fatal("NOT UNIMPLEMENTED");
                    break;
                  }

                default:
                  log.Error("Unknown conversation type '{0}'.", request.ConversationTypeCase);
                  break;
              }


              // Send response to client.
              try
              {
                byte[] responseBytes = Utils.GetMessageBytes(responseMessage);
                Client.Stream.Write(responseBytes, 0, responseBytes.Length);
              }
              catch (System.IO.IOException)
              {
                log.Info("Connection to client has been terminated, disposing client.");
                Client.Dispose();
              }
              break;
            }

          case Message.MessageTypeOneofCase.Response:
            log.Fatal("NOT UNIMPLEMENTED");
            break;

          default:
            log.Error("Unknown message type '{0}', disposing client.", message.MessageTypeCase);
            
            // Just close the connection.
            Client.Dispose();
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, disposing client: {0}", e.ToString());
        Client.Dispose();
      }

      log.Debug("(-)");
    }
  }
}
