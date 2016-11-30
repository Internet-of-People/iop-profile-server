using Iop.Locnet;
using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerSimulator
{
  /// <summary>
  /// Simulator of LBN server. With each profile server we spawn a LBN server 
  /// which will provide information about the neighborhood to the profile server.
  /// </summary>
  public class LbnServer
  {
    private static PrefixLogger log;

    /// <summary>Interface IP address to listen on.</summary>
    public IPAddress IpAddress;

    /// <summary>TCP port to listen on.</summary>
    public int Port;

    /// <summary>Associated profile server.</summary>
    public ProfileServer ProfileServer;

    /// <summary>Lock object to protect access to Neighbors.</summary>
    public object NeighborsLock = new object();

    /// <summary>List of profile servers that are neighbors of ProfileServer.</summary>
    public Dictionary<string, ProfileServer> Neighbors = new Dictionary<string, ProfileServer>(StringComparer.Ordinal);

    /// <summary>TCP server that provides information about the neighborhood via LocNet protocol.</summary>
    public TcpListener Listener;

    /// <summary>If profile server is connected, this is its connection.</summary>
    public TcpClient ConnectedProfileServer;

    /// <summary>If profile server is connected, this is its message builder.</summary>
    public MessageBuilderLocNet ConnectedProfileServerMessageBuilder;

    /// <summary>If profile server is connected, this is its node profile.</summary>
    public NodeProfile ConnectedProfileServerNodeProfile;

    /// <summary>If profile server is connected, this is its location.</summary>
    public Iop.Locnet.GpsLocation ConnectedProfileServerLocation;

    /// <summary>Event that is set when acceptThread is not running.</summary>
    private ManualResetEvent acceptThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for the new clients to connect to the TCP server port.</summary>
    private Thread acceptThread;

    /// <summary>True if the shutdown was initiated, false otherwise.</summary>
    public bool IsShutdown = false;

    /// <summary>Shutdown event is set once the shutdown was initiated.</summary>
    public ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the shutdown is initiated.</summary>
    public CancellationTokenSource ShutdownCancellationTokenSource = new CancellationTokenSource();

    /// <summary>Lock object for writing to client streams. This is simulation only, we do not expect more than one client.</summary>
    private SemaphoreSlim StreamWriteLock = new SemaphoreSlim(1);

    /// <summary>
    /// Initializes the LBN server instance.
    /// </summary>
    /// <param name="ProfileServer">Associated profile server.</param>
    public LbnServer(ProfileServer ProfileServer)
    {
      log = new PrefixLogger("ProfileServerSimulator.LbnServer", ProfileServer.Name);
      log.Trace("()");

      this.ProfileServer = ProfileServer;
      IpAddress = ProfileServer.IpAddress;
      Port = ProfileServer.LbnPort;

      Listener = new TcpListener(IpAddress, Port);
      Listener.Server.LingerState = new LingerOption(true, 0);
      Listener.Server.NoDelay = true;

      log.Trace("(-)");
    }

    /// <summary>
    /// Starts the TCP server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Start()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        Listener.Start();
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


      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Stops the TCP server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Stop()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (Listener != null)
        {
          Listener.Stop();
          res = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources used by the LBN server.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      IsShutdown = true;
      ShutdownEvent.Set();
      ShutdownCancellationTokenSource.Cancel();

      Stop();

      if ((acceptThread != null) && !acceptThreadFinished.WaitOne(10000))
        log.Error("Accept thread did not terminated in 10 seconds.");

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread procedure that is responsible for accepting new clients on the TCP server port.
    /// </summary>
    public void AcceptThread()
    {
      log.Info("()");

      acceptThreadFinished.Reset();

      AutoResetEvent acceptTaskEvent = new AutoResetEvent(false);

      while (!IsShutdown)
      {
        log.Info("Waiting for new client.");
        Task<TcpClient> acceptTask = Listener.AcceptTcpClientAsync();
        acceptTask.ContinueWith(t => acceptTaskEvent.Set());

        WaitHandle[] handles = new WaitHandle[] { acceptTaskEvent, ShutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == ShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        try
        {
          // acceptTask is finished here, asking for Result won't block.
          TcpClient client = acceptTask.Result;
          log.Info("New client '{0}' accepted.", client.Client.RemoteEndPoint);
          ClientHandlerAsync(client);
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
    /// Handler for each client that connects to the TCP server.
    /// </summary>
    /// <param name="Client">Client that is connected to TCP server.</param>
    private async void ClientHandlerAsync(TcpClient Client)
    {
      log.Info("(Client.Client.RemoteEndPoint:{0})", Client.Client.RemoteEndPoint);

      ConnectedProfileServer = Client;
      ConnectedProfileServerMessageBuilder = new MessageBuilderLocNet(0, new List<SemVer>() { SemVer.V100 });

      await ReceiveMessageLoop(Client, ConnectedProfileServerMessageBuilder);

      Client.Dispose();

      log.Info("(-)");
    }


    /// <summary>
    /// Reads messages from the client stream and processes them in a loop until the client disconnects 
    /// or until an action (such as a protocol violation) that leads to disconnecting of the client occurs.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    public async Task ReceiveMessageLoop(TcpClient Client, MessageBuilderLocNet MessageBuilder)
    {
      log.Trace("()");

      try
      {
        NetworkStream stream = Client.GetStream();
        RawMessageReader messageReader = new RawMessageReader(stream);
        while (!IsShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessage(ShutdownCancellationTokenSource.Token);
          bool disconnect = rawMessage.Disconnect;
          bool protocolViolation = rawMessage.ProtocolViolation;
          if (rawMessage.Data != null)
          {
            Message message = CreateMessageFromRawData(rawMessage.Data);
            if (message != null) disconnect = !await ProcessMessageAsync(Client, MessageBuilder, message);
            else protocolViolation = true;
          }

          if (protocolViolation)
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
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public Message CreateMessageFromRawData(byte[] Data)
    {
      log.Trace("()");

      Message res = null;
      try
      {
        res = MessageWithHeader.Parser.ParseFrom(Data).Body;
      }
      catch (Exception e)
      {
        log.Warn("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Trace("(-):{0}", res != null ? "Message" : "null");
      return res;
    }


    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation(TcpClient Client)
    {
      MessageBuilderLocNet mb = new MessageBuilderLocNet(0, new List<SemVer>() { SemVer.V100 });
      Message response = mb.CreateErrorProtocolViolationResponse(new Message() { Id = 0x0BADC0DE });

      await SendMessageAsync(Client, response);
    }


    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(TcpClient Client, MessageBuilderLocNet MessageBuilder, Message IncomingMessage)
    {
      bool res = false;
      log.Debug("()");
      try
      {
        string msgStr = IncomingMessage.ToString();
        log.Trace("Received message type is {0}, message ID is {1}:\n{2}", IncomingMessage.MessageTypeCase, IncomingMessage.Id, msgStr);
        switch (IncomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              Message responseMessage = MessageBuilder.CreateErrorProtocolViolationResponse(IncomingMessage);
              Request request = IncomingMessage.Request;

              SemVer version = new SemVer(request.Version);
              log.Trace("Request type is {0}, version is {1}.", request.RequestTypeCase, version);
              switch (request.RequestTypeCase)
              {
                case Request.RequestTypeOneofCase.LocalService:
                  {
                    log.Trace("Local service request type is {0}.", request.LocalService.LocalServiceRequestTypeCase);
                    switch (request.LocalService.LocalServiceRequestTypeCase)
                    {
                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.RegisterService:
                        // We simulate this by doing nothing, just send successful reply.
                        responseMessage = MessageBuilder.CreateRegisterServiceResponse(IncomingMessage);
                        break;

                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.DeregisterService:
                        responseMessage = MessageBuilder.CreateRegisterServiceResponse(IncomingMessage);
                        // We simulate this by doing nothing, just send successful reply.
                        break;

                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.GetNeighbourNodes:
                        responseMessage = await ProcessMessageGetNeighbourNodesByDistanceLocalRequestAsync(Client, MessageBuilder, IncomingMessage);
                        break;

                      default:
                        log.Error("Invalid local service request type '{0}'.", request.LocalService.LocalServiceRequestTypeCase);
                        break;
                    }
                    break;
                  }

                default:
                  log.Error("Invalid request type '{0}'.", request.RequestTypeCase);
                  break;
              }


              if (responseMessage != null)
              {
                // Send response to client.
                res = await SendMessageAsync(Client, responseMessage);
              }
              else
              {
                // If there is no response to send immediately to the client,
                // we want to keep the connection open.
                res = true;
              }
              break;
            }

          case Message.MessageTypeOneofCase.Response:
            {
              Response response = IncomingMessage.Response;
              log.Trace("Response status is {0}, details are '{1}', response type is {0}.", response.Status, response.Details, response.ResponseTypeCase);

              switch (response.ResponseTypeCase)
              {
                case Response.ResponseTypeOneofCase.LocalService:
                  {
                    log.Trace("Local service response type is {0}.", response.LocalService.LocalServiceResponseTypeCase);
                    switch (response.LocalService.LocalServiceResponseTypeCase)
                    {
                      case LocalServiceResponse.LocalServiceResponseTypeOneofCase.NeighbourhoodUpdated:
                        // Nothing to be done here.
                        res = true;
                        break;

                      default:
                        log.Error("Invalid local service response type '{0}'.", response.LocalService.LocalServiceResponseTypeCase);
                        break;
                    }

                    break;
                  }

                default:
                  log.Error("Unknown response type '{0}'.", response.ResponseTypeCase);
                  // Connection will be closed in ReceiveMessageLoop.
                  break;
              }

              break;
            }

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", IncomingMessage.MessageTypeCase);
            await SendProtocolViolation(Client);
            // Connection will be closed in ReceiveMessageLoop.
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        await SendProtocolViolation(Client);
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Debug("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public async Task<bool> SendMessageAsync(TcpClient Client, Message Message)
    {
      log.Trace("()");

      bool res = await SendMessageInternalAsync(Client, Message);
      if (res)
      {
        // If the message was sent successfully to the target, we close the connection only in case of protocol violation error.
        res = Message.Response.Status != Status.ErrorProtocolViolation;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the message was sent successfully to the target recipient.</returns>
    private async Task<bool> SendMessageInternalAsync(TcpClient Client, Message Message)
    {
      log.Trace("()");

      bool res = false;

      string msgStr = Message.ToString();
      log.Trace("Sending response to client:\n{0}", msgStr);
      byte[] responseBytes = ProtocolHelper.GetMessageBytes(Message);

      await StreamWriteLock.WaitAsync();
      try
      {
        NetworkStream stream = Client.GetStream();
        if (stream != null)
        {
          await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
          res = true;
        }
        else log.Info("Connection to the client has been terminated.");
      }
      catch (IOException)
      {
        log.Info("Connection to the client has been terminated.");
      }
      finally
      {
        StreamWriteLock.Release();
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Processes GetNeighbourNodesByDistanceLocalRequest message from client.
    /// <para>Obtains information about the profile server's neighborhood and initiates sending updates to it.</para>
    /// </summary>
    /// <param name="Client">TCP client that sent the request.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageGetNeighbourNodesByDistanceLocalRequestAsync(TcpClient Client, MessageBuilderLocNet MessageBuilder, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;

      GetNeighbourNodesByDistanceLocalRequest getNeighbourNodesByDistanceLocalRequest = RequestMessage.Request.LocalService.GetNeighbourNodes;

      res = MessageBuilder.CreateErrorInternalResponse(RequestMessage);
      await Task.Delay(1);


      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Adds neighbors to the profile server.
    /// </summary>
    /// <param name="NeighborhoodList">List of all servers in the new neighborhood.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool AddNeighborhood(List<ProfileServer> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      List<ProfileServer> newNeighbors = new List<ProfileServer>();
      bool res = false;
      lock (NeighborsLock)
      {
        foreach (ProfileServer ps in NeighborhoodList)
        {
          // Do not add your own profile server.
          if (ps.Name == ProfileServer.Name) continue;

          // Ignore neighbors that we already have in the list.
          if (Neighbors.ContainsKey(ps.Name)) continue;

          Neighbors.Add(ps.Name, ps);
          log.Trace("Profile server '{0}' added to the neighborhood of server '{1}'.", ps.Name, ProfileServer.Name);

          newNeighbors.Add(ps);
        }
      }

      List<NeighbourhoodChange> changes = new List<NeighbourhoodChange>();
      foreach (ProfileServer ps in newNeighbors)
      {
        NeighbourhoodChange change = new NeighbourhoodChange();
        change.AddedNodeInfo = new NodeInfo()
        {
          Profile = ps.NodeProfile,
          Location = ps.NodeLocation
        };
        changes.Add(change);
      }

      Message message = ConnectedProfileServerMessageBuilder.CreateNeighbourhoodChangedNotificationRequest(changes);
      res = SendMessageAsync(ConnectedProfileServer, message).Result;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Cancels neighbor connections to the profile server.
    /// </summary>
    /// <param name="NeighborhoodList">List of servers to cancel neighbor connection with..</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool CancelNeighborhood(List<ProfileServer> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      List<ProfileServer> removedNeighbors = new List<ProfileServer>();
      bool res = false;
      lock (NeighborsLock)
      {
        foreach (ProfileServer ps in NeighborhoodList)
        {
          // Do not process your own profile server.
          if (ps.Name == ProfileServer.Name) continue;

          // Ignore servers that are not in the neighborhood.
          if (!Neighbors.ContainsKey(ps.Name)) continue;

          Neighbors.Remove(ps.Name);
          log.Trace("Profile server '{0}' removed from the neighborhood of server '{1}'.", ps.Name, ProfileServer.Name);

          removedNeighbors.Add(ps);
        }
      }

      List<NeighbourhoodChange> changes = new List<NeighbourhoodChange>();
      foreach (ProfileServer ps in removedNeighbors)
      {
        NeighbourhoodChange change = new NeighbourhoodChange();
        change.RemovedNodeId = ps.NodeProfile.NodeId;
        changes.Add(change);
      }

      Message message = ConnectedProfileServerMessageBuilder.CreateNeighbourhoodChangedNotificationRequest(changes);
      res = SendMessageAsync(ConnectedProfileServer, message).Result;

      log.Trace("(-):{0}", res);
      return res;
    }

  }
}
