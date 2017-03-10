using Iop.Locnet;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerNetworkSimulator
{
  /// <summary>
  /// Simulator of LOC server. With each profile server we spawn a LOC server 
  /// which will provide information about the neighborhood to the profile server.
  /// </summary>
  public class LocServer
  {
    private PrefixLogger log;

    /// <summary>Interface IP address to listen on.</summary>
    private IPAddress ipAddress;

    /// <summary>TCP port to listen on.</summary>
    private int port;

    /// <summary>Associated profile server.</summary>
    private ProfileServer profileServer;

    /// <summary>Lock object to protect access to Neighbors.</summary>
    private object neighborsLock = new object();

    /// <summary>List of profile servers that are neighbors of ProfileServer.</summary>
    private Dictionary<string, ProfileServer> neighbors = new Dictionary<string, ProfileServer>(StringComparer.Ordinal);

    /// <summary>TCP server that provides information about the neighborhood via LocNet protocol.</summary>
    private TcpListener listener;

    /// <summary>If profile server is connected, this is its connection.</summary>
    private TcpClient connectedProfileServer;

    /// <summary>true if profile server sent us GetNeighbourNodesByDistanceLocalRequest request and wants to receive updates.</summary>
    private bool connectedProfileServerWantsUpdates;

    /// <summary>If profile server is connected, this is its message builder.</summary>
    private LocMessageBuilder connectedProfileServerMessageBuilder;

    /// <summary>Event that is set when acceptThread is not running.</summary>
    private ManualResetEvent acceptThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for the new clients to connect to the TCP server port.</summary>
    private Thread acceptThread;

    /// <summary>True if the shutdown was initiated, false otherwise.</summary>
    private bool isShutdown = false;

    /// <summary>Shutdown event is set once the shutdown was initiated.</summary>
    private ManualResetEvent shutdownEvent = new ManualResetEvent(false);

    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the shutdown is initiated.</summary>
    private CancellationTokenSource shutdownCancellationTokenSource = new CancellationTokenSource();

    /// <summary>Lock object for writing to client streams. This is simulation only, we do not expect more than one client.</summary>
    private SemaphoreSlim StreamWriteLock = new SemaphoreSlim(1);

    /// <summary>Node location.</summary>
    private Iop.Locnet.GpsLocation nodeLocation;

    /// <summary>
    /// Initializes the LOC server instance.
    /// </summary>
    /// <param name="ProfileServer">Associated profile server.</param>
    public LocServer(ProfileServer ProfileServer)
    {
      log = new PrefixLogger("ProfileServerSimulator.LocServer", "[" + ProfileServer.Name + "] ");
      log.Trace("()");

      this.profileServer = ProfileServer;
      ipAddress = ProfileServer.IpAddress;
      port = ProfileServer.LocPort;

      nodeLocation = ProfileServer.NodeLocation;

      listener = new TcpListener(ipAddress, port);
      listener.Server.LingerState = new LingerOption(true, 0);
      listener.Server.NoDelay = true;

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
        log.Trace("Listening on '{0}:{1}'.", ipAddress, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
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
        if (listener != null)
        {
          listener.Stop();
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
    /// Frees resources used by the LOC server.
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
    /// Thread procedure that is responsible for accepting new clients on the TCP server port.
    /// </summary>
    public void AcceptThread()
    {
      log.Trace("()");

      acceptThreadFinished.Reset();

      AutoResetEvent acceptTaskEvent = new AutoResetEvent(false);

      while (!isShutdown)
      {
        log.Debug("Waiting for new client.");
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        acceptTask.ContinueWith(t => acceptTaskEvent.Set());

        WaitHandle[] handles = new WaitHandle[] { acceptTaskEvent, shutdownEvent };
        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == shutdownEvent)
        {
          log.Debug("Shutdown detected.");
          break;
        }

        try
        {
          // acceptTask is finished here, asking for Result won't block.
          TcpClient client = acceptTask.Result;
          log.Debug("New client '{0}' accepted.", client.Client.RemoteEndPoint);
          ClientHandlerAsync(client);
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
    private async void ClientHandlerAsync(TcpClient Client)
    {
      LogDiagnosticContext.Start();

      log.Debug("(Client.Client.RemoteEndPoint:{0})", Client.Client.RemoteEndPoint);

      connectedProfileServer = Client;
      connectedProfileServerMessageBuilder = new LocMessageBuilder(0, new List<SemVer>() { SemVer.V100 });

      await ReceiveMessageLoop(Client, connectedProfileServerMessageBuilder);

      connectedProfileServerWantsUpdates = false;
      connectedProfileServer = null;
      Client.Dispose();

      log.Debug("(-)");

      LogDiagnosticContext.Stop();
    }


    /// <summary>
    /// Reads messages from the client stream and processes them in a loop until the client disconnects 
    /// or until an action (such as a protocol violation) that leads to disconnecting of the client occurs.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    public async Task ReceiveMessageLoop(TcpClient Client, LocMessageBuilder MessageBuilder)
    {
      log.Trace("()");

      try
      {
        NetworkStream stream = Client.GetStream();
        RawMessageReader messageReader = new RawMessageReader(stream);
        while (!isShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(shutdownCancellationTokenSource.Token);
          bool disconnect = rawMessage.Data == null;
          bool protocolViolation = rawMessage.ProtocolViolation;
          if (rawMessage.Data != null)
          {
            LocProtocolMessage message = (LocProtocolMessage)LocMessageBuilder.CreateMessageFromRawData(rawMessage.Data);
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
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation(TcpClient Client)
    {
      LocMessageBuilder mb = new LocMessageBuilder(0, new List<SemVer>() { SemVer.V100 });
      LocProtocolMessage response = mb.CreateErrorProtocolViolationResponse(new LocProtocolMessage(new Message() { Id = 0x0BADC0DE }));

      await SendMessageAsync(Client, response);
    }


    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage IncomingMessage)
    {
      bool res = false;
      log.Debug("()");
      try
      {
        log.Trace("Received message type is {0}, message ID is {1}.", IncomingMessage.MessageTypeCase, IncomingMessage.Id);
        switch (IncomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              LocProtocolMessage responseMessage = MessageBuilder.CreateErrorProtocolViolationResponse(IncomingMessage);
              Request request = IncomingMessage.Request;

              bool setKeepAlive = false;

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
                        responseMessage = ProcessMessageRegisterServiceRequest(Client, MessageBuilder, IncomingMessage);
                        break;

                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.DeregisterService:
                        responseMessage = ProcessMessageDeregisterServiceRequest(Client, MessageBuilder, IncomingMessage);
                        break;

                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.GetNeighbourNodes:
                        responseMessage = ProcessMessageGetNeighbourNodesByDistanceLocalRequest(Client, MessageBuilder, IncomingMessage, out setKeepAlive);
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

                if (res)
                {
                  // If the message was sent successfully to the target, we close the connection only in case of protocol violation error.
                  if (responseMessage.MessageTypeCase == Message.MessageTypeOneofCase.Response)
                    res = responseMessage.Response.Status != Status.ErrorProtocolViolation;
                }

                if (res && setKeepAlive)
                {
                  connectedProfileServerWantsUpdates = true;
                  log.Debug("Profile server '{0}' is now connected to its LOC server and waiting for updates.", profileServer.Name);
                }
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
              log.Trace("Response status is {0}, details are '{1}', response type is {2}.", response.Status, response.Details, response.ResponseTypeCase);

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
    public async Task<bool> SendMessageAsync(TcpClient Client, LocProtocolMessage Message)
    {
      log.Trace("()");

      bool res = await SendMessageInternalAsync(Client, Message);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Client">TCP client.</param>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the message was sent successfully to the target recipient.</returns>
    private async Task<bool> SendMessageInternalAsync(TcpClient Client, LocProtocolMessage Message)
    {
      log.Trace("()");

      bool res = false;

      string msgStr = Message.ToString();
      log.Trace("Sending message:\n{0}", msgStr);
      byte[] responseBytes = LocMessageBuilder.MessageToByteArray(Message);

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
    /// <param name="KeepAlive">This is set to true if KeepAliveAndSendUpdates in the request was set.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public LocProtocolMessage ProcessMessageGetNeighbourNodesByDistanceLocalRequest(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage RequestMessage, out bool KeepAlive)
    {
      log.Trace("()");

      LocProtocolMessage res = null;

      GetNeighbourNodesByDistanceLocalRequest getNeighbourNodesByDistanceLocalRequest = RequestMessage.Request.LocalService.GetNeighbourNodes;
      KeepAlive = getNeighbourNodesByDistanceLocalRequest.KeepAliveAndSendUpdates;

      List<NodeInfo> neighborList = new List<NodeInfo>();
      lock (neighborsLock)
      {
        foreach (ProfileServer ps in neighbors.Values)
        {
          NodeInfo ni = ps.GetNodeInfo();
          neighborList.Add(ni);
        }
      }

      res = MessageBuilder.CreateGetNeighbourNodesByDistanceLocalResponse(RequestMessage, neighborList);

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

      List<NeighbourhoodChange> changes = new List<NeighbourhoodChange>();
      bool res = false;
      lock (neighborsLock)
      {
        foreach (ProfileServer ps in NeighborhoodList)
        {
          // Do not add your own profile server.
          if (ps.Name == profileServer.Name) continue;

          // Ignore neighbors that we already have in the list.
          if (neighbors.ContainsKey(ps.Name))
          {
            log.Debug("Profile server '{0}' already has '{1}' as its neighbor.", profileServer.Name, ps.Name);
            continue;
          }

          ps.Lock();
          if (ps.IsInitialized())
          {
            neighbors.Add(ps.Name, ps);
            log.Debug("Profile server '{0}' added to the neighborhood of server '{1}'.", ps.Name, profileServer.Name);

            // This neighbor server already runs, so we know its profile 
            // we can inform our profile server about it.
            NeighbourhoodChange change = new NeighbourhoodChange();
            change.AddedNodeInfo = ps.GetNodeInfo();
            changes.Add(change);
          }
          else
          {
            // This neighbor server does not run yet, so we do not have its profile.
            // We will install an event to be triggered when this server starts.
            log.Debug("Profile server '{0}' is not initialized yet, installing notification for server '{1}'.", ps.Name, profileServer.Name);
            ps.InstallInitializationNeighborhoodNotification(profileServer);
          }
          ps.Unlock();
        }
      }

      if ((connectedProfileServer != null) && connectedProfileServerWantsUpdates)
      {
        // If our profile server is running already, adding servers to its neighborhood 
        // ends with sending update notification to the profile server.
        if (changes.Count > 0)
        {
          log.Debug("Sending {0} neighborhood changes to profile server '{1}'.", changes.Count, profileServer.Name);
          LocProtocolMessage message = connectedProfileServerMessageBuilder.CreateNeighbourhoodChangedNotificationRequest(changes);
          res = SendMessageAsync(connectedProfileServer, message).Result;
        }
        else
        {
          log.Debug("No neighborhood changes to send to profile server '{0}'.", profileServer.Name);
          res = true;
        }
      }
      else
      {
        // Our profile server is not started/connected yet, which means we just modified its neighborhood,
        // and the information about its neighborhood will be send to it once it sends us GetNeighbourNodesByDistanceLocalRequest.
        log.Debug("Profile server '{0}' is not connected or not fully initialized yet, can't send changes.", profileServer.Name);
        res = true;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sets neighborhood of the profile server during the load from the snapshot.
    /// </summary>
    /// <param name="NeighborhoodList">List of all servers in the server's neighborhood.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool SetNeighborhood(List<ProfileServer> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      bool res = false;
      lock (neighborsLock)
      {
        neighbors.Clear();

        foreach (ProfileServer ps in NeighborhoodList)
          neighbors.Add(ps.Name, ps);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Removes servers from the profile server's neighborhood.
    /// </summary>
    /// <param name="NeighborhoodList">List of servers to cancel neighbor connection with..</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool CancelNeighborhood(List<ProfileServer> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      List<NeighbourhoodChange> changes = new List<NeighbourhoodChange>();
      bool res = false;
      lock (neighborsLock)
      {
        foreach (ProfileServer ps in NeighborhoodList)
        {
          // Do not process your own profile server.
          if (ps.Name == profileServer.Name) continue;

          ps.Lock();
          if (ps.IsInitialized())
          {
            // Ignore servers that are not in the neighborhood.
            if (neighbors.ContainsKey(ps.Name))
            {
              neighbors.Remove(ps.Name);
              log.Trace("Profile server '{0}' removed from the neighborhood of server '{1}'.", ps.Name, profileServer.Name);

              // This neighbor server already runs, so we know its profile 
              // we can inform our profile server about it.
              NeighbourhoodChange change = new NeighbourhoodChange();
              change.RemovedNodeId = ProtocolHelper.ByteArrayToByteString(ps.GetNetworkId());
              changes.Add(change);
            }
          }
          else
          {
            // This neighbor server does not run yet, so we do not have its profile.
            // We will uninstall a possibly installed event.
            log.Debug("Profile server '{0}' is not initialized yet, uninstalling notification for server '{1}'.", ps.Name, profileServer.Name);
            ps.UninstallInitializationNeighborhoodNotification(profileServer);
          }
          ps.Unlock();
        }
      }

      if ((connectedProfileServer != null) && connectedProfileServerWantsUpdates)
      {
        // If our profile server is running already, removing servers to its neighborhood 
        // ends with sending update notification to the profile server.
        if (changes.Count > 0)
        {
          log.Debug("Sending {0} neighborhood changes to profile server '{1}'.", changes.Count, profileServer.Name);
          LocProtocolMessage message = connectedProfileServerMessageBuilder.CreateNeighbourhoodChangedNotificationRequest(changes);
          res = SendMessageAsync(connectedProfileServer, message).Result;
        }
        else
        {
          log.Debug("No neighborhood changes to send to profile server '{0}'.", profileServer.Name);
          res = true;
        }
      }
      else
      {
        // Our profile server is not started/connected yet, which means we just modified its neighborhood,
        // and the information about its neighborhood will be send to it once it sends us GetNeighbourNodesByDistanceLocalRequest.
        log.Debug("Profile server '{0}' is not connected or not fully initialized yet, can't send changes.", profileServer.Name);
        res = true;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes RegisterServiceRequest message from client.
    /// <para>Obtains information about the profile server's NodeProfile.</para>
    /// </summary>
    /// <param name="Client">TCP client that sent the request.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public LocProtocolMessage ProcessMessageRegisterServiceRequest(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage RequestMessage)
    {
      log.Trace("()");

      LocProtocolMessage res = MessageBuilder.CreateRegisterServiceResponse(RequestMessage);

      RegisterServiceRequest registerServiceRequest = RequestMessage.Request.LocalService.RegisterService;

      byte[] serverId = registerServiceRequest.Service.ServiceData.ToByteArray();
      if ((registerServiceRequest.Service.Type == ServiceType.Profile) && (serverId.Length == 32))
      {
        profileServer.SetNetworkId(serverId);
      }
      else log.Error("Received register service request is invalid.");

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

    /// <summary>
    /// Processes DeregisterServiceRequest message from client.
    /// <para>Removes information about the profile server's NodeProfile.</para>
    /// </summary>
    /// <param name="Client">TCP client that sent the request.</param>
    /// <param name="MessageBuilder">Client's message builder.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public LocProtocolMessage ProcessMessageDeregisterServiceRequest(TcpClient Client, LocMessageBuilder MessageBuilder, LocProtocolMessage RequestMessage)
    {
      log.Trace("()");

      LocProtocolMessage res = MessageBuilder.CreateDeregisterServiceResponse(RequestMessage);

      DeregisterServiceRequest deregisterServiceRequest = RequestMessage.Request.LocalService.DeregisterService;
      profileServer.Uninitialize();

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

    /// <summary>
    /// Returns list of related profile server's neighbors.
    /// </summary>
    /// <returns>List of related profile server's neigbhors.</returns>
    public List<ProfileServer> GetNeighbors()
    {
      List<ProfileServer> res = null;

      lock (neighborsLock)
      {
        res = neighbors.Values.ToList();
      }

      return res;
    }

    /// <summary>
    /// Creates LOC server's snapshot.
    /// </summary>
    /// <returns>LOC server's snapshot.</returns>
    public LocServerSnapshot CreateSnapshot()
    {
      LocServerSnapshot res = new LocServerSnapshot()
      {
        IpAddress = this.ipAddress.ToString(),
        NeighborsNames = this.neighbors.Keys.ToList(),
        Port = this.port,
      };
      return res;
    }

    /// <summary>
    /// Adds a neighbor profile server to the list of neighbors when loading simulation from snapshot.
    /// </summary>
    public void AddNeighborSnapshot(ProfileServer Neighbor)
    {
      lock (neighborsLock)
      {
        neighbors.Add(Neighbor.Name, Neighbor);
      }
    }


    /// <summary>
    /// Checks whether LOC node information contains a Profile Server service and if so, it returns its port and network ID.
    /// </summary>
    /// <param name="NodeInfo">Node information structure to scan.</param>
    /// <param name="ProfileServerPort">If the node informatino contains Profile Server type of service, this is filled with the Profile Server port.</param>
    /// <param name="ProfileServerId">If the node informatino contains Profile Server type of service, this is filled with the Profile Server network ID.</param>
    /// <returns>true if the node information contains Profile Server type of service, false otherwise.</returns>
    public bool HasProfileServerService(NodeInfo NodeInfo, out int ProfileServerPort, out byte[] ProfileServerId)
    {
      log.Trace("()");

      bool res = false;
      ProfileServerPort = 0;
      ProfileServerId = null;
      foreach (ServiceInfo si in NodeInfo.Services)
      {
        if (si.Type == ServiceType.Profile)
        {
          bool portValid = (0 < si.Port) && (si.Port <= 65535);
          bool serviceDataValid = si.ServiceData.Length == 32;
          if (portValid && serviceDataValid)
          {
            ProfileServerPort = (int)si.Port;
            ProfileServerId = si.ServiceData.ToByteArray();
            res = true;
          }
          else
          {
            if (!portValid) log.Warn("Invalid service port {0}.", si.Port);
            if (!serviceDataValid) log.Warn("Invalid identifier length in ServiceData: {0} bytes.", si.ServiceData.Length);
          }

          break;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }

  }
}
