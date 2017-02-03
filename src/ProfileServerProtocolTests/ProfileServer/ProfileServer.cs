using ProfileServerCrypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Iop.Profileserver;
using ProfileServerProtocol;

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Types of server roles that can each be served on different port.
  /// Some of the roles are served unencrypted, others are encrypted.
  /// </summary>
  [Flags]
  public enum ServerRole
  {
    /// <summary>Primary Interface server role.</summary>
    Primary = 1,

    /// <summary>Neighbors Interface server role.</summary>
    ServerNeighbor = 4,

    /// <summary>Customer Clients Interface server role.</summary>
    ClientCustomer = 16,

    /// <summary>Non Customer Clients Interface server role.</summary>
    ClientNonCustomer = 32,

    /// <summary>Application Service Interface server role.</summary>
    ClientAppService = 128
  }
  
  /// <summary>
  /// Describes incoming message and server role of the listener that received it.
  /// </summary>
  public class IncomingServerMessage
  {
    /// <summary>Role of the listener that received the message.</summary>
    public ServerRole Role;

    /// <summary>Received message itself.</summary>
    public Message IncomingMessage;

    /// <summary>Connected client that sent the message or null if the client has been disconnected.</summary>
    public IncomingClient Client;
  }

  /// <summary>
  /// Simulates some of the profile server functionality that is related to neighborhood network relations.
  /// </summary>
  public class ProfileServer
  {
    private NLog.Logger log;

    /// <summary>Name of this profile server instance.</summary>
    private string name;
    /// <summary>Name of this profile server instance.</summary>
    public string Name { get { return name; } }

    /// <summary>IP address on which this server listens.</summary>
    private IPAddress ipAddress;

    /// <summary>Port of primary interface of this server.</summary>
    private int primaryPort;
    /// <summary>Port of primary interface of this server.</summary>
    public int PrimaryPort { get { return primaryPort; } }

    /// <summary>Port of neighborhood interface of this server.</summary>
    private int serverNeighborPort;
    /// <summary>Port of neighborhood interface of this server.</summary>
    public int ServerNeighborPort { get { return serverNeighborPort; } }

    /// <summary>Port of client non-customer interface of this server.</summary>
    private int clientNonCustomerPort;
    /// <summary>Port of client non-customer interface of this server.</summary>
    public int ClientNonCustomerPort { get { return clientNonCustomerPort; } }

    /// <summary>TCP listener that accepts new clients on the primary interface.</summary>
    private TcpListener primaryListener;

    /// <summary>TCP listener that accepts new clients on the server neighborhood interface.</summary>
    private TcpListener serverNeighborListener;

    /// <summary>TCP listener that accepts new clients on the client non-customer interface.</summary>
    private TcpListener clientNonCustomerListener;

    /// <summary>Event that is set when acceptThread is not running.</summary>
    private ManualResetEvent acceptThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for the new clients to connect to one of the opened TCP server ports.</summary>
    private Thread acceptThread;

    /// <summary>True if the shutdown was initiated, false otherwise.</summary>
    private bool isShutdown = false;
    /// <summary>True if the shutdown was initiated, false otherwise.</summary>
    public bool IsShutdown { get { return isShutdown; } }

    /// <summary>Shutdown event is set once the shutdown was initiated.</summary>
    private ManualResetEvent shutdownEvent = new ManualResetEvent(false);

    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the shutdown is initiated.</summary>
    private CancellationTokenSource shutdownCancellationTokenSource = new CancellationTokenSource();
    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the shutdown is initiated.</summary>
    public CancellationTokenSource ShutdownCancellationTokenSource { get { return shutdownCancellationTokenSource; } }

    /// <summary>Cryptographic keys of this server instance.</summary>
    private KeysEd25519 keys;
    /// <summary>Cryptographic keys of this server instance.</summary>
    public KeysEd25519 Keys { get { return keys; } }

    /// <summary>Server's TLS certificate.</summary>
    private X509Certificate tlsCertificate;
    /// <summary>Server's TLS certificate.</summary>
    public X509Certificate TlsCertificate { get { return tlsCertificate; } }

    /// <summary>Lock object to protect access to messageList.</summary>
    private object messageListLock = new object();

    /// <summary>List of processed incoming messages.</summary>
    private List<IncomingServerMessage> messageList;


    /// <summary>If true, the profile server rejects NeighborhoodSharedProfileUpdateRequest requests.</summary>
    public bool RejectNeighborhoodSharedProfileUpdate = false;

    /// <summary>GPS location of the profile server.</summary>
    private GpsLocation location;

    /// <summary>
    /// Initializes an instance of a profile server simulator.
    /// </summary>
    /// <param name="Name">Name of the instance.</param>
    /// <param name="IpAddress">IP address on which this server will listen.</param>
    /// <param name="BasePort">Base port from which specific interface port numbers are calculated.</param>
    /// <param name="Keys">Cryptographic keys of this server instance, or null if they should be generated.</param>
    /// <param name="Location">Location of the profile server.</param>
    public ProfileServer(string Name, IPAddress IpAddress, int BasePort, KeysEd25519 Keys = null, GpsLocation Location = null)
    {
      log = NLog.LogManager.GetLogger("Test.ProfileServer." + Name);
      log.Trace("(IpAddress:'{0}',BasePort:{1})", IpAddress, BasePort);

      keys = Keys != null ? Keys : Ed25519.GenerateKeys();
      location = Location != null ? Location : new GpsLocation(0, 0);

      name = Name;
      ipAddress = IpAddress;
      primaryPort = BasePort;
      serverNeighborPort = BasePort + 1;
      clientNonCustomerPort = BasePort + 2;

      primaryListener = new TcpListener(ipAddress, primaryPort);
      primaryListener.Server.LingerState = new LingerOption(true, 0);
      primaryListener.Server.NoDelay = true;

      serverNeighborListener = new TcpListener(ipAddress, serverNeighborPort);
      serverNeighborListener.Server.LingerState = new LingerOption(true, 0);
      serverNeighborListener.Server.NoDelay = true;

      clientNonCustomerListener = new TcpListener(ipAddress, clientNonCustomerPort);
      clientNonCustomerListener.Server.LingerState = new LingerOption(true, 0);
      clientNonCustomerListener.Server.NoDelay = true;

      tlsCertificate = new X509Certificate2("ps.pfx");

      messageList = new List<IncomingServerMessage>();

      log.Trace("(-)");
    }


    /// <summary>
    /// Starts the TCP servers.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Start()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        log.Trace("Listening on '{0}:{1}'.", ipAddress, primaryPort);
        primaryListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        primaryListener.Start();

        log.Trace("Listening on '{0}:{1}'.", ipAddress, serverNeighborPort);
        serverNeighborListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        serverNeighborListener.Start();

        log.Trace("Listening on '{0}:{1}'.", ipAddress, clientNonCustomerPort);
        clientNonCustomerListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        clientNonCustomerListener.Start();

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (res)
      {
        acceptThread = new Thread(new ThreadStart(AcceptThread));
        acceptThread.Start();
      }
      else
      {
        primaryListener.Stop();
        serverNeighborListener.Stop();
      }


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Stops the TCP servers.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private bool Stop()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (primaryListener != null) primaryListener.Stop();
        if (serverNeighborListener != null) serverNeighborListener.Stop();
        if (clientNonCustomerListener != null) clientNonCustomerListener.Stop();
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources used by the server.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      isShutdown = true;
      shutdownEvent.Set();
      shutdownCancellationTokenSource.Cancel();

      Stop();

      if ((acceptThread != null) && !acceptThreadFinished.WaitOne(10000))
        log.Error("Accept thread did not terminated in 10 seconds.");

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread procedure that is responsible for accepting new clients on the TCP server ports.
    /// </summary>
    public void AcceptThread()
    {
      log.Trace("()");

      acceptThreadFinished.Reset();

      AutoResetEvent acceptPrimaryTaskEvent = new AutoResetEvent(false);
      AutoResetEvent acceptServerNeighborTaskEvent = new AutoResetEvent(false);
      AutoResetEvent acceptClientNonCustomerTaskEvent = new AutoResetEvent(false);

      while (!isShutdown)
      {
        log.Debug("Waiting for new client.");
        Task<TcpClient> acceptPrimaryTask = primaryListener.AcceptTcpClientAsync();
        acceptPrimaryTask.ContinueWith(t => acceptPrimaryTaskEvent.Set());

        Task<TcpClient> acceptServerNeighborTask = serverNeighborListener.AcceptTcpClientAsync();
        acceptServerNeighborTask.ContinueWith(t => acceptServerNeighborTaskEvent.Set());

        Task<TcpClient> acceptClientNonCustomerTask = clientNonCustomerListener.AcceptTcpClientAsync();
        acceptClientNonCustomerTask.ContinueWith(t => acceptClientNonCustomerTaskEvent.Set());

        WaitHandle[] handles = new WaitHandle[] { acceptPrimaryTaskEvent, acceptServerNeighborTaskEvent, acceptClientNonCustomerTaskEvent, shutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == shutdownEvent)
        {
          log.Debug("Shutdown detected.");
          break;
        }

        try
        {
          if (handles[index] == acceptPrimaryTaskEvent)
          {
            // acceptPrimaryTaskEvent is finished here, asking for Result won't block.
            TcpClient client = acceptPrimaryTask.Result;
            log.Debug("New client '{0}' accepted on primary interface.", client.Client.RemoteEndPoint);
            ClientHandlerAsync(client, ServerRole.Primary);
          }
          else if (handles[index] == acceptServerNeighborTaskEvent)
          {
            // acceptServerNeighborTaskEvent is finished here, asking for Result won't block.
            TcpClient client = acceptServerNeighborTask.Result;
            log.Debug("New client '{0}' accepted on srNeighbor interface.", client.Client.RemoteEndPoint);
            ClientHandlerAsync(client, ServerRole.ServerNeighbor);
          }
          else if (handles[index] == acceptClientNonCustomerTaskEvent)
          {
            // acceptClientNonCustomerTaskEvent is finished here, asking for Result won't block.
            TcpClient client = acceptClientNonCustomerTask.Result;
            log.Debug("New client '{0}' accepted on clNonCustomer interface.", client.Client.RemoteEndPoint);
            ClientHandlerAsync(client, ServerRole.ClientNonCustomer);
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      acceptThreadFinished.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for each client that connects to the TCP server.
    /// </summary>
    /// <param name="Client">Client that is connected to TCP server.</param>
    /// <param name="Role"></param>
    /// <remarks>The client is being handled in the processing loop until the connection to it 
    /// is terminated by either side. This function implements reading the message from the network stream,
    /// which includes reading the message length prefix followed by the entire message.</remarks>
    private async void ClientHandlerAsync(TcpClient Client, ServerRole Role)
    {
      log.Debug("(Client.Client.RemoteEndPoint:{0})", Client.Client.RemoteEndPoint);

      bool useTls = Role != ServerRole.Primary;
      IncomingClient client = new IncomingClient(this, Client, useTls, Role);
      await client.ReceiveMessageLoop();
      client.Dispose();

      log.Debug("(-)");
    }


    /// <summary>
    /// Retrieves the list of processed messages and clears the list.
    /// </summary>
    /// <returns>List of processed messages since the last call of this method..</returns>
    public List<IncomingServerMessage> GetMessageList()
    {
      log.Trace("()");

      List<IncomingServerMessage> res = null;
      lock (messageListLock)
      {
        res = messageList;
        messageList = new List<IncomingServerMessage>();
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }

    /// <summary>
    /// Adds a new message to the message list.
    /// </summary>
    /// <param name="IncomingMessage">New message to add to the message list.</param>
    public void AddMessage(Message IncomingMessage, ServerRole Role, IncomingClient Client)
    {
      log.Trace("()");

      IncomingServerMessage msg = new IncomingServerMessage()
      {
        Role = Role,
        IncomingMessage = IncomingMessage,
        Client = Client
      };

      lock (messageListLock)
      {
        messageList.Add(msg);
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Returns location network node information.
    /// </summary>
    /// <param name="LocPort">Port of the associated LOC server.</param>
    /// <returns>NodeInfo structure describing the server in location network.</returns>
    public Iop.Locnet.NodeInfo GetNodeInfo(int LocPort)
    {
      Iop.Locnet.NodeInfo res = new Iop.Locnet.NodeInfo()
      {
        NodeId = ProtocolHelper.ByteArrayToByteString(new byte[0]),
        Location = new Iop.Locnet.GpsLocation()
        {
          Latitude = location.GetLocationTypeLatitude(),
          Longitude = location.GetLocationTypeLongitude()
        },
        Contact = new Iop.Locnet.NodeContact()
        {
          IpAddress = ProtocolHelper.ByteArrayToByteString(ipAddress.GetAddressBytes()),
          NodePort = (uint)LocPort,
          ClientPort = (uint)LocPort
        },
      };

      Iop.Locnet.ServiceInfo serviceInfo = new Iop.Locnet.ServiceInfo()
      {
        Type = Iop.Locnet.ServiceType.Profile,
        Port = (uint)primaryPort,
        ServiceData = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(keys.PublicKey))
      };

      res.Services.Add(serviceInfo); 
      return res;
    }


    /// <summary>
    /// Waits until the profile server receives a conversation request message of certain type.
    /// </summary>
    /// <param name="Role">Specifies the role of the server to which the message had to arrive to be accepted by this wait function.</param>
    /// <param name="RequestType">Type of the conversation request to wait for.</param>
    /// <param name="ClearMessageList">If set to true, the message list will be cleared once the wait is finished.</param>
    /// <returns>Message the profile server received.</returns>
    public async Task<IncomingServerMessage> WaitForConversationRequest(ServerRole Role, ConversationRequest.RequestTypeOneofCase RequestType, bool ClearMessageList = true)
    {
      log.Trace("()");
      IncomingServerMessage res = null;
      bool done = false;
      while (!done)
      {
        lock (messageListLock)
        {
          foreach (IncomingServerMessage ism in messageList)
          {
            if (!Role.HasFlag(ism.Role)) continue;

            Message message = ism.IncomingMessage;
            if (message.MessageTypeCase != Message.MessageTypeOneofCase.Request) continue;

            Request request = message.Request;
            if (request.ConversationTypeCase != Request.ConversationTypeOneofCase.ConversationRequest) continue;

            ConversationRequest conversationRequest = request.ConversationRequest;
            if (conversationRequest.RequestTypeCase != RequestType) continue;

            res = ism;
            done = true;
            break;
          }

          if (done && ClearMessageList)
            messageList.Clear();
        }

        if (!done)
        {
          try
          {
            await Task.Delay(1000, shutdownCancellationTokenSource.Token);
          }
          catch
          {
          }
        }
      }

      log.Trace("(-)");
      return res;
    }



    /// <summary>
    /// Waits until the profile server receives a response message to the specific request message.
    /// </summary>
    /// <param name="Role">Specifies the role of the server to which the message had to arrive to be accepted by this wait function.</param>
    /// <param name="RequestMessage">Request message for which corresponding response we are waiting for.</param>
    /// <param name="ClearMessageList">If set to true, the message list will be cleared once the wait is finished.</param>
    /// <returns>Message the profile server received.</returns>
    public async Task<IncomingServerMessage> WaitForResponse(ServerRole Role, Message RequestMessage, bool ClearMessageList = true)
    {
      log.Trace("()");
      IncomingServerMessage res = null;
      bool done = false;
      while (!done)
      {
        lock (messageListLock)
        {
          foreach (IncomingServerMessage ism in messageList)
          {
            if (!Role.HasFlag(ism.Role)) continue;

            Message message = ism.IncomingMessage;
            if (message.MessageTypeCase != Message.MessageTypeOneofCase.Response) continue;

            if (message.Id != RequestMessage.Id) continue;

            res = ism;
            done = true;
            break;
          }

          if (done && ClearMessageList)
            messageList.Clear();
        }

        if (!done)
        {
          try
          {
            await Task.Delay(1000, shutdownCancellationTokenSource.Token);
          }
          catch
          {
          }
        }
      }

      log.Trace("(-)");
      return res;
    }


    /// <summary>
    /// Sends FinishNeighborhoodInitializationRequest message to the connected client of the profile server.
    /// </summary>
    /// <param name="Client">Client connected to the profile server, to which the message should be sent.</param>
    /// <returns>Message that was sent to the profile server, or null if the function failed.</returns>
    public async Task<Message> SendFinishNeighborhoodInitializationRequest(IncomingClient Client)
    {
      log.Trace("()");

      Message res = null;
      Message request = Client.MessageBuilder.CreateFinishNeighborhoodInitializationRequest();
      if (await Client.SendMessageAndSaveUnfinishedRequestAsync(request, null))
        res = request;

      if (res != null) log.Trace("(-):*.Id", res.Id);
      else log.Trace("(-):null");
      return res;
    }

    /// <summary>
    /// Sends NeighborhoodSharedProfileUpdateRequest message to the connected client of the profile server.
    /// </summary>
    /// <param name="Client">Client connected to the profile server, to which the message should be sent.</param>
    /// <param name="UpdateItems">List of update items to send to the client.</param>
    /// <returns>Message that was sent to the profile server, or null if the function failed.</returns>
    public async Task<Message> SendNeighborhoodSharedProfileUpdateRequest(IncomingClient Client, List<SharedProfileUpdateItem> UpdateItems)
    {
      log.Trace("()");

      Message res = null;
      Message request = Client.MessageBuilder.CreateNeighborhoodSharedProfileUpdateRequest(UpdateItems);
      if (await Client.SendMessageAndSaveUnfinishedRequestAsync(request, null))
        res = request;

      if (res != null) log.Trace("(-):*.Id", res.Id);
      else log.Trace("(-):null");
      return res;
    }
  }
}
