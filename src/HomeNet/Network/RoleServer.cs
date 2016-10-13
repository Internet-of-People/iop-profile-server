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
using HomeNet.Utils;
using HomeNetCrypto;
using Google.Protobuf;
using HomeNet.Data;
using Microsoft.EntityFrameworkCore.Storage;
using HomeNet.Data.Models;
using System.Collections;

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
    ClientCustomer = 32,

    /// <summary>Non Customer Clients Interface server role.</summary>
    ClientNonCustomer = 64,

    /// <summary>Application Service Interface server role.</summary>
    ClientAppService = 128
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
      { ServerRole.ClientAppService,  true  },
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
      { ServerRole.ClientAppService,  false },
    };


    /// <summary>Shutdown signaling object.</summary>
    private ComponentShutdown shutdownSignaling;

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


    /// <summary>Internal server ID that is formed of server roles. It is used as the base of message numbering.</summary>
    public uint IdBase;

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

    /// <summary>List of server's network peers and clients.</summary>
    private ClientList clientList;

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
      this.shutdownSignaling = new ComponentShutdown(Base.Components.GlobalShutdown);

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


      logPrefix = string.Format("[{0}/tcp{1}] ", this.EndPoint.Port, this.UseTls ? "_tls" : "");
      logName = "HomeNet.Network.RoleServer";
      this.log = new PrefixLogger(logName, logPrefix);

      // Initialize last client ID to have different values for each role server.
      IdBase = ((uint)Roles << 24);
      clientList = new ClientList((ulong)IdBase << 32, logPrefix);
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

      shutdownSignaling.SignalShutdown();

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

      AutoResetEvent acceptTaskEvent = new AutoResetEvent(false);

      while (!shutdownSignaling.IsShutdown)
      {
        log.Info("Waiting for new client.");
        Task<TcpClient> acceptTask = Listener.AcceptTcpClientAsync();
        acceptTask.ContinueWith(t => acceptTaskEvent.Set());

        WaitHandle[] handles = new WaitHandle[] { acceptTaskEvent, shutdownSignaling.ShutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == shutdownSignaling.ShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        try
        {
          // acceptTask is finished here, asking for Result won't block.
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

      while (!shutdownSignaling.IsShutdown)
      {
        WaitHandle[] handles = new WaitHandle[] { clientQueueEvent, shutdownSignaling.ShutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == shutdownSignaling.ShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        log.Debug("New client in the queue detected, queue count is {0}.", clientQueue.Count);
        bool queueEmpty = false;
        while (!queueEmpty && !shutdownSignaling.IsShutdown)
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
            Client client = new Client(this, tcpClient, UseTls, keepAliveInterval);
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

      clientList.AddNetworkPeer(Client);
      log.Debug("Client ID set to 0x{0:X16}.", Client.Id);

      try
      {
        if (UseTls)
        {
          SslStream sslStream = (SslStream)Client.Stream;
          await sslStream.AuthenticateAsServerAsync(Base.Configuration.TcpServerTlsCertificate, false, SslProtocols.Tls12, false);
        }

        byte[] messageHeaderBuffer = new byte[ProtocolHelper.HeaderSize];
        byte[] messageBuffer = null;
        ClientStatus clientStatus = ClientStatus.ReadingHeader;
        uint messageSize = 0;
        int messageHeaderBytesRead = 0;
        int messageBytesRead = 0;

        while (!shutdownSignaling.IsShutdown)
        {
          Task<int> readTask = null;
          int remain = 0;

          log.Trace("Client status is '{0}'.", clientStatus);
          switch (clientStatus)
          {
            case ClientStatus.ReadingHeader:
              {
                remain = ProtocolHelper.HeaderSize - messageHeaderBytesRead;
                readTask = Client.Stream.ReadAsync(messageHeaderBuffer, messageHeaderBytesRead, remain, shutdownSignaling.ShutdownCancellationTokenSource.Token);
                break;
              }

            case ClientStatus.ReadingBody:
              {
                remain = (int)messageSize - messageBytesRead;
                readTask = Client.Stream.ReadAsync(messageBuffer, ProtocolHelper.HeaderSize + messageBytesRead, remain, shutdownSignaling.ShutdownCancellationTokenSource.Token);
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

          bool protoViolationDisconnect = false;
          bool disconnect = false;
          switch (clientStatus)
          {
            case ClientStatus.ReadingHeader:
              {
                messageHeaderBytesRead += readAmount;
                if (readAmount == remain)
                {
                  if (messageHeaderBuffer[0] == 0x0D)
                  {
                    uint hdr = ProtocolHelper.GetValueLittleEndian(messageHeaderBuffer, 1);
                    if (hdr + ProtocolHelper.HeaderSize <= ProtocolHelper.MaxSize)
                    {
                      messageSize = hdr;
                      clientStatus = ClientStatus.ReadingBody;
                      messageBuffer = new byte[ProtocolHelper.HeaderSize + messageSize];
                      Array.Copy(messageHeaderBuffer, messageBuffer, messageHeaderBuffer.Length);
                      log.Trace("Reading of message header completed. Message size is {0} bytes.", messageSize);
                    }
                    else
                    {
                      log.Warn("Client claimed message of size {0} which exceeds the maximum.", hdr + ProtocolHelper.HeaderSize);
                      protoViolationDisconnect = true;
                    }
                  }
                  else
                  {
                    log.Warn("Message has invalid format - it's first byte is 0x{0:X2}, should be 0x0D.", messageHeaderBuffer[0]);
                    protoViolationDisconnect = true;
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
                  log.Trace("Reading of message size {0} completed.", messageSize);

                  disconnect = !await ProcessMessageAsync(messageBuffer, Client);
                }
                break;
              }
          }

          if (protoViolationDisconnect)
          {
            await SendProtocolViolation(Client);
            break;
          }

          if (disconnect)
            break;
        }
      }
      catch (Exception e)
      {
        if ((e is ObjectDisposedException) || (e is IOException)) log.Info("Connection to client has been terminated.");
        else log.Error("Exception occurred: {0}", e.ToString());
      }

      clientList.RemoveNetworkPeer(Client);

      Client.Dispose();

      log.Info("(-)");
    }


    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation(Client Client)
    {
      MessageBuilder mb = new MessageBuilder(0, new List<byte[]>() { new byte[] { 1, 0, 0 } }, null);
      Message response = mb.CreateErrorProtocolViolationResponse(new Message() { Id = 0x0BADC0DE });

      // Send response to client.
      log.Trace("Sending response to client:\n{0}", response);
      byte[] responseBytes = ProtocolHelper.GetMessageBytes(response);
      await Client.Stream.WriteAsync(responseBytes, 0, responseBytes.Length);
    }





    /// <summary>
    /// Creates a copy of list of existing clients that are connected to the server.
    /// </summary>
    /// <returns>List of clients.</returns>
    public List<Client> GetClientListCopy()
    {
      log.Trace("()");

      List<Client> res = clientList.GetNetworkClientList();

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Data">Full ProtoBuf message to be processed.</param>
    /// <param name="Client">TCP client who send the message.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(byte[] Data, Client Client)
    {
      string prefix = string.Format("{0}[{1}] ", this.logPrefix, Client.RemoteEndPoint);
      PrefixLogger log = new PrefixLogger(this.logName, prefix);

      MessageBuilder messageBuilder = Client.MessageBuilder;

      bool res = false;
      log.Debug("()");
      try
      {
        // Update time until this client's connection is considered inactive.
        Client.NextKeepAliveTime = DateTime.UtcNow.AddSeconds(Client.KeepAliveIntervalSeconds);
        log.Trace("Client ID 0x{0:X16} NextKeepAliveTime updated to {1}.", Client.Id, Client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));

        Message requestMessage = MessageWithHeader.Parser.ParseFrom(Data).Body;
        log.Trace("Received message type is {0}, message ID is {1}:\n{2}", requestMessage.MessageTypeCase, requestMessage.Id, requestMessage);
        switch (requestMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              Message responseMessage = messageBuilder.CreateErrorProtocolViolationResponse(requestMessage);
              Request request = requestMessage.Request;
              log.Trace("Request conversation type is {0}.", request.ConversationTypeCase);
              switch (request.ConversationTypeCase)
              {
                case Request.ConversationTypeOneofCase.SingleRequest:
                  {
                    SingleRequest singleRequest = request.SingleRequest;
                    log.Trace("Single request type is {0}, version is {1}.", singleRequest.RequestTypeCase, ProtocolHelper.VersionBytesToString(singleRequest.Version.ToByteArray()));

                    if (!ProtocolHelper.IsValidVersion(singleRequest.Version.ToByteArray()))
                    {
                      responseMessage.Response.Details = "version";
                      break; 
                    }

                    switch (singleRequest.RequestTypeCase)
                    {
                      case SingleRequest.RequestTypeOneofCase.Ping:
                        responseMessage = ProcessMessagePingRequest(Client, requestMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ListRoles:
                        responseMessage = ProcessMessageListRolesRequest(Client, requestMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.GetIdentityInformation:
                        responseMessage = await ProcessMessageGetIdentityInformationRequestAsync(Client, requestMessage);
                        break;

                      default:
                        log.Error("Unknown request type '{0}'.", singleRequest.RequestTypeCase);
                        break;
                    }

                    break;
                  }

                case Request.ConversationTypeOneofCase.ConversationRequest:
                  {
                    ConversationRequest conversationRequest = request.ConversationRequest;
                    if (conversationRequest.Signature.Length > 0) log.Trace("Conversation signature is '{0}'.", Crypto.ToHex(conversationRequest.Signature.ToByteArray()));
                    else log.Trace("No signature provided.");

                    switch (conversationRequest.RequestTypeCase)
                    {
                      case ConversationRequest.RequestTypeOneofCase.Start:
                        responseMessage = ProcessMessageStartConversationRequest(Client, requestMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.HomeNodeRequest:
                        responseMessage = await ProcessMessageHomeNodeRequestRequestAsync(Client, requestMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CheckIn:
                        responseMessage = await ProcessMessageCheckInRequestAsync(Client, requestMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.VerifyIdentity:
                        responseMessage = ProcessMessageVerifyIdentityRequest(Client, requestMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.UpdateProfile:
                        responseMessage = await ProcessMessageUpdateProfileRequestAsync(Client, requestMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CancelHomeNodeAgreement:
                        responseMessage = await ProcessMessageCancelHomeNodeAgreementRequestAsync(Client, requestMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ApplicationServiceAdd:
                        responseMessage = ProcessMessageApplicationServiceAddRequest(Client, requestMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ApplicationServiceRemove:
                        responseMessage = ProcessMessageApplicationServiceRemoveRequest(Client, requestMessage);
                        break;

                      default:
                        log.Error("Unknown request type '{0}'.", conversationRequest.RequestTypeCase);
                        break;
                    }

                    break;
                  }

                default:
                  log.Error("Unknown conversation type '{0}'.", request.ConversationTypeCase);
                  break;
              }


              // Send response to client.
              try
              {
                log.Trace("Sending response to client:\n{0}", responseMessage);
                byte[] responseBytes = ProtocolHelper.GetMessageBytes(responseMessage);
                await Client.Stream.WriteAsync(responseBytes, 0, responseBytes.Length);

                res = responseMessage.Response.Status != Status.ErrorProtocolViolation;
              }
              catch (IOException)
              {
                log.Info("Connection to client has been terminated, connection to the client will be closed.");
                // Connection will be closed in ClientHandlerAsync.
              }
              break;
            }

          case Message.MessageTypeOneofCase.Response:
            log.Fatal("NOT UNIMPLEMENTED, connection to the client will be closed");
            await SendProtocolViolation(Client);
            // Connection will be closed in ClientHandlerAsync.
            break;

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", requestMessage.MessageTypeCase);
            await SendProtocolViolation(Client);
            // Connection will be closed in ClientHandlerAsync.
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, disposing client: {0}", e.ToString());
        await SendProtocolViolation(Client);
        // Connection will be closed in ClientHandlerAsync.
      }

      log.Debug("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Creates a list of role servers to be sent to the requesting client.
    /// The information about role servers can be obtained from Network.Server component.
    /// </summary>
    /// <returns>List of role server descriptions.</returns>
    private List<Iop.Homenode.ServerRole> GetRolesFromServerComponent()
    {
      log.Trace("()");

      List<Iop.Homenode.ServerRole> res = new List<Iop.Homenode.ServerRole>();

      Server serverComponent = (Server)Base.ComponentDictionary["Network.Server"];

      foreach (TcpRoleServer roleServer in serverComponent.GetRoleServers())
      {
        foreach (ServerRole role in Enum.GetValues(typeof(ServerRole)))
        {
          if (roleServer.Roles.HasFlag(role))
          {
            bool skip = false;
            ServerRoleType srt = ServerRoleType.Primary;
            switch (role)
            {
              case ServerRole.PrimaryUnrelated: srt = ServerRoleType.Primary; break;
              case ServerRole.NodeNeighbor: srt = ServerRoleType.NdNeighbor; break;
              case ServerRole.NodeColleague: srt = ServerRoleType.NdColleague; break;
              case ServerRole.ClientNonCustomer: srt = ServerRoleType.ClNonCustomer; break;
              case ServerRole.ClientCustomer: srt = ServerRoleType.ClCustomer; break;
              case ServerRole.ClientAppService: srt = ServerRoleType.ClAppService; break;
              default:
                skip = true;
                break;
            }

            if (!skip)
            {
              Iop.Homenode.ServerRole item = new Iop.Homenode.ServerRole()
              {
                Role = srt,
                Port = (uint)roleServer.EndPoint.Port,
                IsTcp = true,
                IsTls = roleServer.UseTls
              };
              res.Add(item);
            }
          }
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }

    /// <summary>
    /// Selects a common protocol version that both server and client support.
    /// </summary>
    /// <param name="ClientVersions">List of versions that the client supports. The list is ordered by client's preference.</param>
    /// <param name="SelectedCommonVersion">If the function succeeds, this is set to the selected version that both client and server support.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool GetCommonSupportedVersion(IEnumerable<ByteString> ClientVersions, out byte[] SelectedCommonVersion)
    {
#warning TODO: This function is currently implemented only to support version 1.0.0.
      log.Trace("()");
      log.Warn("TODO UNIMPLEMENTED");
      SelectedCommonVersion = null;

      string selectedVersion = null;
      bool res = false;
      foreach (ByteString clVersion in ClientVersions)
      {
        byte[] version = clVersion.ToByteArray();
        string clVersionString = ProtocolHelper.VersionBytesToString(version);
        if (clVersionString == "1.0.0")
        {
          SelectedCommonVersion = version;
          selectedVersion = clVersionString;
          res = true;
          break;
        }
      }

      if (res) log.Trace("(-):{0},SelectedCommonVersion='{1}'", res, selectedVersion);
      else log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Verifies that client's request was not sent against the protocol rules - i.e. that the role server
    /// that received the message is serving the role the message was designed for and that the conversation 
    /// status with the clients matches the required status for the particular message.
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <param name="RequiredRole">Server role required for the message, or null if all roles servers can handle this message.</param>
    /// <param name="RequiredConversationStatus">Required conversation status for the message, or null for single messages.</param>
    /// <param name="ResponseMessage">If the verification fails, this is filled with error response to be sent to the client.</param>
    /// <returns>true if the function succeeds (i.e. required conditions are met and the message can be processed), false otherwise.</returns>
    public bool CheckSessionConditions(Client Client, Message RequestMessage, ServerRole? RequiredRole, ClientConversationStatus? RequiredConversationStatus, out Message ResponseMessage)
    {
      log.Trace("(RequiredRole:{0},RequiredConversationStatus:{1})", RequiredRole != null ? RequiredRole.ToString() : "null", RequiredConversationStatus != null ? RequiredConversationStatus.ToString() : "null");

      bool res = false;
      ResponseMessage = null;

      string requestName = RequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest ? "single request " + RequestMessage.Request.SingleRequest.RequestTypeCase.ToString() : "conversation request " + RequestMessage.Request.ConversationRequest.RequestTypeCase.ToString();

      // RequiredRole contains one or more roles and the current server has to have at least one of them.
      if ((RequiredRole == null) || ((Roles & RequiredRole.Value) != 0))
      {
        if (RequiredConversationStatus == null)
        {
          res = true;
        }
        else
        {
          switch (RequiredConversationStatus.Value)
          {
            case ClientConversationStatus.NoConversation:
            case ClientConversationStatus.ConversationStarted:
              res = Client.ConversationStatus == RequiredConversationStatus.Value;
              if (!res)
              {
                log.Warn("Client sent {0} but the conversation status is {1}.", requestName, Client.ConversationStatus);
                ResponseMessage = Client.MessageBuilder.CreateErrorBadConversationStatusResponse(RequestMessage);
              }
              break;

            case ClientConversationStatus.Verified:
            case ClientConversationStatus.Authenticated:
              // In case of Verified status requirement, the Authenticated status satisfies the condition as well.
              res = (Client.ConversationStatus == RequiredConversationStatus.Value) || (Client.ConversationStatus == ClientConversationStatus.Authenticated);
              if (!res)
              {
                log.Warn("Client sent {0} but the conversation status is {1}.", requestName, Client.ConversationStatus);
                ResponseMessage = Client.MessageBuilder.CreateErrorUnauthorizedResponse(RequestMessage);
              }
              break;

            default:
              log.Error("Unknown conversation status '{0}'.", Client.ConversationStatus);
              ResponseMessage = Client.MessageBuilder.CreateErrorInternalResponse(RequestMessage);
              break;
          }
        }
      }
      else
      {
        log.Warn("Received {0} on server without {1} role(s) (server roles are {2}).", requestName, RequiredRole.Value, Roles);
        ResponseMessage = Client.MessageBuilder.CreateErrorBadRoleResponse(RequestMessage);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes PingRequest message from client.
    /// <para>Simply copies the payload to a new ping response message.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessagePingRequest(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      MessageBuilder messageBuilder = Client.MessageBuilder;
      PingRequest pingRequest = RequestMessage.Request.SingleRequest.Ping;

      Message res = messageBuilder.CreatePingResponse(RequestMessage, pingRequest.Payload.ToByteArray(), ProtocolHelper.GetUnixTimestampMs());

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes ListRolesRequest message from client.
    /// <para>Obtains a list of role servers and returns it in the response.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageListRolesRequest(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.PrimaryUnrelated, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      ListRolesRequest listRolesRequest = RequestMessage.Request.SingleRequest.ListRoles;

      List<Iop.Homenode.ServerRole> roles = GetRolesFromServerComponent();
      res = messageBuilder.CreateListRolesResponse(RequestMessage, roles);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes GetIdentityInformationRequest message from client.
    /// <para>Obtains information about identity that is hosted by the node.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageGetIdentityInformationRequestAsync(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      GetIdentityInformationRequest getIdentityInformationRequest = RequestMessage.Request.SingleRequest.GetIdentityInformation;

      byte[] identityId = getIdentityInformationRequest.IdentityNetworkId.ToByteArray();
      if (identityId.Length == Identity.IdentifierLength)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          Identity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => i.IdentityId == identityId)).FirstOrDefault();
          if (identity != null)
          {
            if (identity.IsProfileInitialized())
            {
              bool isHosted = identity.ExpirationDate == null;
              if (isHosted)
              {
                Client targetClient = clientList.GetCheckedInClient(identityId);
                bool isOnline = targetClient != null;
                byte[] publicKey = identity.PublicKey;
                string name = identity.Name;
                string extraData = identity.ExtraData;

                byte[] profileImage = null;
                byte[] thumbnailImage = null;
                HashSet<string> applicationServices = null;

                if (getIdentityInformationRequest.IncludeProfileImage)
                  profileImage = await identity.GetProfileImageDataAsync();

                if (getIdentityInformationRequest.IncludeThumbnailImage)
                  thumbnailImage = await identity.GetThumbnailImageDataAsync();

                if (getIdentityInformationRequest.IncludeApplicationServices)
                  applicationServices = targetClient.GetApplicationServices();

                res = messageBuilder.CreateGetIdentityInformationResponse(RequestMessage, isHosted, null, isOnline, publicKey, name, extraData, profileImage, thumbnailImage, applicationServices);
              }
              else
              {
                byte[] targetHomeNode = identity.HomeNodeId;
                res = messageBuilder.CreateGetIdentityInformationResponse(RequestMessage, isHosted, targetHomeNode);
              }
            }
            else
            {
              log.Trace("Identity ID '{0}' profile not initialized.", Crypto.ToHex(identityId));
              res = messageBuilder.CreateErrorUninitializedResponse(RequestMessage);
            }
          }
          else 
          {
            log.Trace("Identity ID '{0}' is not hosted by this node.", Crypto.ToHex(identityId));
            res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          }
        }
      }
      else
      {
        log.Trace("Invalid length of identity ID - {0} bytes.", identityId.Length);
        res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes StartConversationRequest message from client.
    /// <para>Initiates a conversation with the client provided that there is a common version of the protocol supported by both sides.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageStartConversationRequest(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, null, ClientConversationStatus.NoConversation, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      MessageBuilder messageBuilder = Client.MessageBuilder;
      StartConversationRequest startConversationRequest = RequestMessage.Request.ConversationRequest.Start;

      byte[] version;
      if (GetCommonSupportedVersion(startConversationRequest.SupportedVersions, out version))
      {
        Client.PublicKey = startConversationRequest.PublicKey.ToByteArray();
        Client.IdentityId = Crypto.Sha1(Client.PublicKey);

        if (clientList.AddNetworkPeerWithIdentity(Client))
        {
          Client.ConversationStatus = ClientConversationStatus.ConversationStarted;
          Client.MessageBuilder.SetProtocolVersion(version);

          byte[] challenge = new byte[32];
          Crypto.Rng.GetBytes(challenge);
          Client.AuthenticationChallenge = challenge;

          log.Debug("Client {0} conversation status updated to {1}, selected version is '{2}', client public key set to '{3}', client identity ID set to '{4}', challenge set to '{5}'.",
            Client.RemoteEndPoint, Client.ConversationStatus, ProtocolHelper.VersionBytesToString(version), Crypto.ToHex(Client.PublicKey), Crypto.ToHex(Client.IdentityId), Crypto.ToHex(Client.AuthenticationChallenge));

          res = messageBuilder.CreateStartConversationResponse(RequestMessage, version, Base.Configuration.Keys.PublicKey, Client.AuthenticationChallenge);
        }
        else res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      }
      else
      {
        log.Warn("Client and server are incompatible in protocol versions.");
        res = messageBuilder.CreateErrorUnsupportedResponse(RequestMessage);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes HomeNodeRequestRequest message from client.
    /// <para>Registers a new customer client identity. The identity must not be hosted by the node already 
    /// and the node must not have reached the maximal number of hosted clients. The newly created profile 
    /// is empty and has to be initialized by the identity using UpdateProfileRequest.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageHomeNodeRequestRequestAsync(Client Client, Message RequestMessage)
    {
#warning TODO: This function is currently implemented only to ignore contracts.
      // TODO: CHECK CONTRACT:
      // * signature is valid 
      // * plan refers to on of our plans
      // * startTime is per specification
      // * nodePublicKey is our key
      // * identityPublicKey is client's key 
      log.Trace("()");
      log.Fatal("TODO UNIMPLEMENTED");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }
      
      MessageBuilder messageBuilder = Client.MessageBuilder;
      HomeNodeRequestRequest homeNodeRequestRequest = RequestMessage.Request.ConversationRequest.HomeNodeRequest;
      HomeNodePlanContract contract = homeNodeRequestRequest.Contract;


      bool success = false;
      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.HomeIdentityLock;
        using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObject))
        {
          try
          {
            // We need to recheck the number of hosted identities within the transaction.
            int hostedIdentities = await unitOfWork.HomeIdentityRepository.CountAsync(null);
            log.Trace("Currently hosting {0} clients.", hostedIdentities);
            if (hostedIdentities < Base.Configuration.MaxHostedIdentities)
            {
              Identity existingIdentity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => i.IdentityId == Client.IdentityId)).FirstOrDefault();
              // Identity does not exist at all, or it has been cancelled so that ExpirationDate was set.
              if ((existingIdentity == null) || (existingIdentity.ExpirationDate != null))
              {
                // We do not have the identity in our client's database,
                // OR we do have the identity in our client's database, but it's contract has been cancelled.
                if (existingIdentity != null)
                  log.Debug("Identity ID '{0}' is already a client of this node, but its contract has been cancelled.", Crypto.ToHex(Client.IdentityId));

                Identity identity = existingIdentity == null ? new Identity() : existingIdentity;

                // We can't change primary identifier in existing entity.
                if (existingIdentity == null) identity.IdentityId = Client.IdentityId;

                identity.HomeNodeId = new byte[0];
                identity.PublicKey = Client.PublicKey;
                identity.Version = new byte[] { 0, 0, 0 };
                identity.Name = "";
                identity.Type = "<new>";
                // Existing cancelled identity profile does not have images, no need to delete anything at this point.
                identity.ProfileImage = null;
                identity.ThumbnailImage = null;
                identity.InitialLocationEncoded = 0;
                identity.ExtraData = null;
                identity.ExpirationDate = null;

                if (existingIdentity == null) unitOfWork.HomeIdentityRepository.Insert(identity);
                else unitOfWork.HomeIdentityRepository.Update(identity);

                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
                success = true;
              }
              else 
              {
                // We have the identity in our client's database with an active contract.
                log.Debug("Identity ID '{0}' is already a client of this node.", Crypto.ToHex(Client.IdentityId));
                res = messageBuilder.CreateErrorAlreadyExistsResponse(RequestMessage);
              }
            }
            else
            {
              log.Debug("MaxHostedIdentities {0} has been reached.", Base.Configuration.MaxHostedIdentities);
              res = messageBuilder.CreateErrorQuotaExceededResponse(RequestMessage);
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }

          if (!success)
          {
            log.Warn("Rolling back transaction.");
            unitOfWork.SafeTransactionRollback(transaction);
          }

          unitOfWork.ReleaseLock(lockObject);
        }
      }


      if (success)
      {
        log.Debug("Identity '{0}' added to database.", Crypto.ToHex(Client.IdentityId));
        res = messageBuilder.CreateHomeNodeRequestResponse(RequestMessage, contract);
      }


      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes CheckInRequest message from client.
    /// <para>It verifies the identity's public key against the signature of the challenge provided during the start of the conversation. 
    /// The identity must be hosted on this node. If everything is OK, the identity is checked-in and the status of the conversation
    /// is upgraded to Authenticated.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageCheckInRequestAsync(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      MessageBuilder messageBuilder = Client.MessageBuilder;
      CheckInRequest checkInRequest = RequestMessage.Request.ConversationRequest.CheckIn;

      byte[] challenge = checkInRequest.Challenge.ToByteArray();
      if (StructuralComparisons.StructuralComparer.Compare(challenge, Client.AuthenticationChallenge) == 0)
      {
        if (messageBuilder.VerifySignedConversationRequestBody(RequestMessage, checkInRequest, Client.PublicKey))
        {
          log.Debug("Identity '{0}' is about to check in ...", Crypto.ToHex(Client.IdentityId));

          bool success = false;
          res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
          using (UnitOfWork unitOfWork = new UnitOfWork())
          {
            try
            {
              Identity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
              if (identity != null)
              {
                if (clientList.AddCheckedInClient(Client))
                {
                  string prefix = string.Format("[{0}:{1}:0x{2:X16}] ", this.EndPoint.Address, this.EndPoint.Port, Client.Id);
                  Client.ApplicationServices = new ApplicationServices(prefix);
                  Client.ConversationStatus = ClientConversationStatus.Authenticated;

                  success = true;
                }
                else log.Error("Identity '{0}' failed to check-in.", Crypto.ToHex(Client.IdentityId));
              }
              else
              {
                log.Debug("Identity '{0}' is not a client of this node.", Crypto.ToHex(Client.IdentityId));
                res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
              }
            }
            catch (Exception e)
            {
              log.Info("Exception occurred: {0}", e.ToString());
            }
          }


          if (success)
          {
            log.Debug("Identity '{0}' successfully checked in ...", Crypto.ToHex(Client.IdentityId));
            res = messageBuilder.CreateCheckInResponse(RequestMessage);
          }
        }
        else
        {
          log.Warn("Client's challenge signature is invalid.");
          res = messageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
        }
      }
      else
      {
        log.Warn("Challenge provided in the request does not match the challenge created by the node.");
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "challenge");
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes VerifyIdentityRequest message from client.
    /// <para>It verifies the identity's public key against the signature of the challenge provided during the start of the conversation. 
    /// If everything is OK, the status of the conversation is upgraded to Verified.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageVerifyIdentityRequest(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      MessageBuilder messageBuilder = Client.MessageBuilder;
      VerifyIdentityRequest verifyIdentityRequest = RequestMessage.Request.ConversationRequest.VerifyIdentity;

      byte[] challenge = verifyIdentityRequest.Challenge.ToByteArray();
      if (StructuralComparisons.StructuralComparer.Compare(challenge, Client.AuthenticationChallenge) == 0)
      {
        if (messageBuilder.VerifySignedConversationRequestBody(RequestMessage, verifyIdentityRequest, Client.PublicKey))
        {
          log.Debug("Identity '{0}' successfully verified its public key.", Crypto.ToHex(Client.IdentityId));
          Client.ConversationStatus = ClientConversationStatus.Verified;
          res = messageBuilder.CreateVerifyIdentityResponse(RequestMessage);
        }
        else
        {
          log.Warn("Client's challenge signature is invalid.");
          res = messageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
        }
      }
      else
      {
        log.Warn("Challenge provided in the request does not match the challenge created by the node.");
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "challenge");
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes UpdateProfileRequest message from client.
    /// <para>Updates one or more fields in the identity's profile.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    /// <remarks>If a profile image or a thumbnail image is changed during this process, 
    /// the old files are deleted. It may happen that if the machine loses a power during 
    /// this process just before old files are to be deleted, they will remain 
    /// on the disk without any reference from the database, thus possibly creates a resource leak.
    /// As this should not occur very often, we the implementation is left unsafe 
    /// and if this is a problem for a long term existance of the node, 
    /// the unreferenced files can be cleaned using some kind of maintanence process that will 
    /// delete all image files unreferenced from the database.</remarks>
    public async Task<Message> ProcessMessageUpdateProfileRequestAsync(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      UpdateProfileRequest updateProfileRequest = RequestMessage.Request.ConversationRequest.UpdateProfile;

      bool success = false;
      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        try
        {
          Identity identityForValidation = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
          if (identityForValidation != null)
          {
            Message errorResponse;
            if (ValidateUpdateProfileRequest(identityForValidation, updateProfileRequest, messageBuilder, RequestMessage, out errorResponse))
            {
              // If an identity has a profile image and a thumbnail image, they are saved on the disk.
              // If we are replacing those images, we have to create new files and delete the old files.
              // First, we create the new files and then in DB transaction, we get information about 
              // whether to delete existing files and which ones.
#warning TODO: Change this when EF Core 1.1 is out with Reload() method
              unitOfWork.Context.Entry(identityForValidation).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
              Guid? profileImageToDelete = null;
              Guid? thumbnailImageToDelete = null;

              if (updateProfileRequest.SetImage)
              {
                identityForValidation.ProfileImage = Guid.NewGuid();
                identityForValidation.ThumbnailImage = Guid.NewGuid();

                byte[] profileImage = updateProfileRequest.Image.ToByteArray();
                byte[] thumbnailImage;
                ProfileImageToThumbnailImage(profileImage, out thumbnailImage);

                await identityForValidation.SaveProfileImageDataAsync(profileImage);
                await identityForValidation.SaveThumbnailImageDataAsync(thumbnailImage);
              }


              // Update database record.
              DatabaseLock lockObject = UnitOfWork.HomeIdentityLock;
              await unitOfWork.AcquireLockAsync(lockObject);
              try
              {
                Identity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();

                if (identity != null)
                {
                  if (updateProfileRequest.SetVersion)
                    identity.Version = updateProfileRequest.Version.ToByteArray();

                  if (updateProfileRequest.SetName)
                    identity.Name = updateProfileRequest.Name;

                  if (updateProfileRequest.SetImage)
                  {
                    // Here we replace existing images with new ones
                    // and we save the old images GUIDs so we can delete them later.
                    profileImageToDelete = identity.ProfileImage;
                    thumbnailImageToDelete = identity.ThumbnailImage;

                    identity.ProfileImage = identityForValidation.ProfileImage;
                    identity.ThumbnailImage = identityForValidation.ThumbnailImage;
                  }

                  if (updateProfileRequest.SetLocation)
                    identity.InitialLocationEncoded = updateProfileRequest.Location;

                  if (updateProfileRequest.SetExtraData)
                    identity.ExtraData = updateProfileRequest.ExtraData;

                  unitOfWork.HomeIdentityRepository.Update(identity);
                  success = await unitOfWork.SaveAsync();
                }
                else
                {
                  log.Debug("Identity '{0}' is not a client of this node.", Crypto.ToHex(Client.IdentityId));
                  res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
                }
              } 
              catch (Exception e)
              {
                log.Error("Exception occurred: {0}", e);
              }
              unitOfWork.ReleaseLock(lockObject);

              if (success)
              {
                log.Debug("Identity '{0}' updated its profile in the database.", Crypto.ToHex(Client.IdentityId));
                res = messageBuilder.CreateUpdateProfileResponse(RequestMessage);
              }

              // Delete old files, if there are any.
              if (profileImageToDelete != null)
              {
                if (ImageHelper.DeleteImageFile(profileImageToDelete.Value)) log.Trace("Old file of image {0} deleted.", profileImageToDelete.Value);
                else log.Error("Unable to delete old file of image {0}.", profileImageToDelete.Value);
              }
              if (thumbnailImageToDelete != null)
              {
                if (ImageHelper.DeleteImageFile(thumbnailImageToDelete.Value)) log.Trace("Old file of image {0} deleted.", thumbnailImageToDelete.Value);
                else log.Error("Unable to delete old file of image {0}.", thumbnailImageToDelete.Value);
              }
            }
            else res = errorResponse;
          }
          else
          {
            log.Debug("Identity '{0}' is not a client of this node.", Crypto.ToHex(Client.IdentityId));
            res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e);
        }
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Checks whether the update profile request is valid.
    /// </summary>
    /// <param name="Identity"></param>
    /// <param name="UpdateProfileRequest"></param>
    /// <param name="MessageBuilder"></param>
    /// <param name="RequestMessage"></param>
    /// <param name="ErrorResponse"></param>
    /// <returns>true if the profile update request can be applied, false otherwise.</returns>
    bool ValidateUpdateProfileRequest(Identity Identity, UpdateProfileRequest UpdateProfileRequest, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("(Identity.IdentityId:'{0}')", Crypto.ToHex(Identity.IdentityId));

      bool res = false;
      ErrorResponse = null;

      // Check if the update is a valid profile initialization.
      // If the profile is updated for the first time (aka is being initialized),
      // SetVersion, SetName and SetLocation must be true.
      if (!Identity.IsProfileInitialized())
      {
        log.Debug("Profile initialization detected.");

        if (!UpdateProfileRequest.SetVersion || !UpdateProfileRequest.SetName || !UpdateProfileRequest.SetLocation)
        {
          string details = null;
          if (!UpdateProfileRequest.SetVersion) details = "setVersion";
          else if (!UpdateProfileRequest.SetName) details = "setName";
          else if (!UpdateProfileRequest.SetLocation) details = "setLocation";

          log.Debug("Attempt to initialize profile without '{0}' being set.", details);
          ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
        }
      }
      else
      {
        // Nothing to update?
        if (!UpdateProfileRequest.SetVersion
          && !UpdateProfileRequest.SetName
          && !UpdateProfileRequest.SetImage
          && !UpdateProfileRequest.SetLocation
          && !UpdateProfileRequest.SetExtraData)
        {
          log.Debug("Update request updates nothing.");
          ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "set*");
        }
      }

      if (ErrorResponse == null)
      {
        string details = null;

        // Now check if the values we received are valid.
        if (UpdateProfileRequest.SetVersion)
        {
          byte[] version = UpdateProfileRequest.Version.ToByteArray();

          // Currently only supported version is 1,0,0.
          if (StructuralComparisons.StructuralComparer.Compare(version, new byte[] { 1, 0, 0 }) != 0)
          {
            log.Debug("Unsupported version '{0}'.", ProtocolHelper.VersionBytesToString(version));
            details = "version";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetName)
        {
          string name = UpdateProfileRequest.Name;

          // Name is non-empty string, max Identity.MaxProfileNameLengthBytes bytes long.
          if (string.IsNullOrEmpty(name) || (Encoding.UTF8.GetByteCount(name) > Identity.MaxProfileNameLengthBytes))
          {
            log.Debug("Invalid name '{0}'.", name);
            details = "name";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetImage)
        {
          byte[] image = UpdateProfileRequest.Image.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxProfileImageLengthBytes.
          if ((image.Length == 0) || (image.Length > Identity.MaxProfileImageLengthBytes) || !ValidateImageFormat(image))
          {
            log.Debug("Invalid image.");
            details = "image";
          }
        }


        if ((details == null) && UpdateProfileRequest.SetLocation)
        {
          // No validation currently needed.
        }

        if ((details == null) && UpdateProfileRequest.SetExtraData)
        {
          string extraData = UpdateProfileRequest.ExtraData;
          if (extraData == null) extraData = "";

          // Extra data is semicolon separated 'key=value' list, max Identity.MaxProfileExtraDataLengthBytes bytes long.
          if (Encoding.UTF8.GetByteCount(extraData) > Identity.MaxProfileNameLengthBytes)
          {
            log.Debug("Extra data too large ({0} bytes, limit is {1}).", Encoding.UTF8.GetByteCount(extraData), Identity.MaxProfileNameLengthBytes);
            details = "extraData";
          }
        }

        if (details == null)
        {
          res = true;
        }
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes CancelHomeNodeAgreementRequest message from client.
    /// <para>Cancels a home node agreement with an identity.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    /// <remarks>Cancelling home node agreement causes identity's image files to be deleted. 
    /// The profile itself is not immediately deleted, but its expiration date is set, 
    /// which will lead to its deletion. If the home node redirection is installed, the expiration date 
    /// is set to a later time.</remarks>
    public async Task<Message> ProcessMessageCancelHomeNodeAgreementRequestAsync(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      CancelHomeNodeAgreementRequest cancelHomeNodeAgreementRequest = RequestMessage.Request.ConversationRequest.CancelHomeNodeAgreement;

      if (!cancelHomeNodeAgreementRequest.RedirectToNewHomeNode || (cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId.Length == Identity.IdentifierLength))
      {
        Guid? profileImageToDelete = null;
        Guid? thumbnailImageToDelete = null;

        bool success = false;
        res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          DatabaseLock lockObject = UnitOfWork.HomeIdentityLock;
          await unitOfWork.AcquireLockAsync(lockObject);
          try
          {
            Identity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
            if (identity != null)
            {
              // We artificially initialize the profile when we cancel it in order to allow queries towards this profile.
              if (!identity.IsProfileInitialized())
                identity.Version = new byte[] { 1, 0, 0 };

              profileImageToDelete = identity.ProfileImage;
              thumbnailImageToDelete = identity.ThumbnailImage;

              if (cancelHomeNodeAgreementRequest.RedirectToNewHomeNode)
              {
                // The customer cancelled the contract, but left a redirect, which we will maintain for 14 days.
                identity.ExpirationDate = DateTime.UtcNow.AddDays(14);
                identity.HomeNodeId = cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId.ToByteArray();
              }
              else
              {
                // The customer cancelled the contract, no redirect is being maintained, we can delete the record at any time.
                identity.ExpirationDate = DateTime.UtcNow;
              }

              unitOfWork.HomeIdentityRepository.Update(identity);
              success = await unitOfWork.SaveAsync();
            }
            else
            {
              log.Debug("Identity '{0}' is not a client of this node.", Crypto.ToHex(Client.IdentityId));
              res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e);

          }
          unitOfWork.ReleaseLock(lockObject);

          if (success)
          {
            if (cancelHomeNodeAgreementRequest.RedirectToNewHomeNode) log.Debug("Identity '{0}' home node agreement cancelled and redirection set to node '{1}'.", Crypto.ToHex(Client.IdentityId), Crypto.ToHex(cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId.ToByteArray()));
            else log.Debug("Identity '{0}' home node agreement cancelled and no redirection set.", Crypto.ToHex(Client.IdentityId));

            res = messageBuilder.CreateCancelHomeNodeAgreementResponse(RequestMessage);
          }
        }

        // Delete old files, if there are any.
        if (profileImageToDelete != null)
        {
          if (ImageHelper.DeleteImageFile(profileImageToDelete.Value)) log.Trace("Old file of image {0} deleted.", profileImageToDelete.Value);
          else log.Error("Unable to delete old file of image {0}.", profileImageToDelete.Value);
        }
        if (thumbnailImageToDelete != null)
        {
          if (ImageHelper.DeleteImageFile(thumbnailImageToDelete.Value)) log.Trace("Old file of image {0} deleted.", thumbnailImageToDelete.Value);
          else log.Error("Unable to delete old file of image {0}.", thumbnailImageToDelete.Value);
        }
      }
      else
      {
        log.Debug("Invalid home node identifier '{0}'.", Crypto.ToHex(cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId.ToByteArray()));
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "newHomeNodeNetworkId");
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes ApplicationServiceAddRequest message from client.
    /// <para>Adds one or more application services to the list of available services of a customer client.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageApplicationServiceAddRequest(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      ApplicationServiceAddRequest applicationServiceAddRequest = RequestMessage.Request.ConversationRequest.ApplicationServiceAdd;

      // Validation of service names.
      for (int i = 0; i < applicationServiceAddRequest.ServiceNames.Count; i++)
      {
        string serviceName = applicationServiceAddRequest.ServiceNames[i];
        if (string.IsNullOrEmpty(serviceName) || (Encoding.UTF8.GetByteCount(serviceName) > Client.MaxApplicationServiceNameLengthBytes))
        {
          log.Warn("Invalid service name '{0}'.", serviceName);
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, string.Format("serviceNames[{0}]", i));
          break;
        }
      }

      if (res == null)
      {
        if (Client.ApplicationServices.AddLimit(applicationServiceAddRequest.ServiceNames))
        {
          log.Debug("Service names added to identity '{0}': {1}", Crypto.ToHex(Client.IdentityId), string.Join(", ", applicationServiceAddRequest.ServiceNames));
          res = messageBuilder.CreateApplicationServiceAddResponse(RequestMessage);
        }
        else
        {
          log.Error("Identity '{0}' application services list not changed, number of services would exceed the limit {1}.", Crypto.ToHex(Client.IdentityId), Client.MaxClientApplicationServices);
          res = messageBuilder.CreateErrorQuotaExceededResponse(RequestMessage);
        }
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }





    /// <summary>
    /// Processes ApplicationServiceRemoveRequest message from client.
    /// <para>Removes an application service from the list of available services of a customer client.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageApplicationServiceRemoveRequest(Client Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      ApplicationServiceRemoveRequest applicationServiceRemoveRequest = RequestMessage.Request.ConversationRequest.ApplicationServiceRemove;

      string serviceName = applicationServiceRemoveRequest.ServiceName;
      if (Client.ApplicationServices.Remove(serviceName))
      {
        res = messageBuilder.CreateApplicationServiceRemoveResponse(RequestMessage);
        log.Debug("Service name '{0}' removed from identity '{1}'.", serviceName, Crypto.ToHex(Client.IdentityId));
      }
      else
      {
        log.Warn("Service name '{0}' not found on the list of supported services of identity '{1}'.", serviceName, Crypto.ToHex(Client.IdentityId));
        res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

    /// <summary>
    /// Checks whether binary data represent a valid PNG or JPEG image.
    /// </summary>
    /// <param name="Data">Binary data to check.</param>
    /// <returns>true if the data represents a valid PNG or JPEG image, false otherwise</returns>
    public bool ValidateImageFormat(byte[] Data)
    {
      log.Trace("(Data.Length:{0})", Data.Length);
#warning TODO: This function currently does nothing, waiting for some libraries to be released.
      // TODO: 
      // * check image is valid PNG or JPEG format
      // * waiting for https://github.com/JimBobSquarePants/ImageProcessor/ to release
      //   or https://magick.codeplex.com/documentation to support all OS with NET Core releases
      log.Fatal("TODO UNIMPLEMENTED");

      bool res = Data.Length > 2;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Creates a thumbnail image from a profile image.
    /// </summary>
    /// <param name="ProfileImage">Binary data of the profile image data.</param>
    /// <param name="ThumbnailImage">On the output, this is filled with thumbnail image data.</param>
    public void ProfileImageToThumbnailImage(byte[] ProfileImage, out byte[] ThumbnailImage)
    {
      log.Trace("(ProfileImage.Length:{0})", ProfileImage.Length);

#warning TODO: This function currently does nothing, waiting for some libraries to be released.
      // TODO: 
      // * check if ProfileImage is small enough to represent thumbnail image without changes
      // * if it is too big, check if it is PNG or JPEG
      // * if it is PNG, convert to JPEG
      // * resize and increase compression until small enough
      // * waiting for https://github.com/JimBobSquarePants/ImageProcessor/ to release

      log.Fatal("TODO UNIMPLEMENTED");

      ThumbnailImage = ProfileImage;

      log.Trace("(-):{0})", ThumbnailImage.Length);
    }
  }
}
