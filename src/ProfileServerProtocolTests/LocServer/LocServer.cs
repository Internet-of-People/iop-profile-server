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

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Simulator of LOC server.
  /// </summary>
  public class LocServer
  {
    private NLog.Logger log;

    /// <summary>Interface IP address to listen on.</summary>
    private IPAddress ipAddress;

    /// <summary>TCP port to listen on.</summary>
    private int port;
    /// <summary>TCP port to listen on.</summary>
    public int Port { get { return port; } }

    /// <summary>Lock object to protect access to Neighbors.</summary>
    private object neighborsLock = new object();

    /// <summary>List of profile servers that are neighbors of the target profile server being tested.</summary>
    private List<NodeInfo> neighbors = new List<NodeInfo>();

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


    /// <summary>Port of the associated profile server, or 0 if the value was not initialized yet.</summary>
    private int profileServerPort = 0;


    /// <summary>
    /// Initializes the LOC server instance.
    /// </summary>
    /// <param name="Name">Name of the instance.</param>
    /// <param name="IpAddress">IP address on which this server will listen.</param>
    /// <param name="Port">Port on which LOC server will listen.</param>
    public LocServer(string Name, IPAddress IpAddress, int Port)
    {
      log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests.LocServer." + Name);
      log.Trace("(IpAddress:'{0}',Port:{1})", IpAddress, Port);

      ipAddress = IpAddress;
      port = Port;

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
      log.Debug("(Client.Client.RemoteEndPoint:{0})", Client.Client.RemoteEndPoint);

      connectedProfileServer = Client;
      connectedProfileServerMessageBuilder = new LocMessageBuilder(0, new List<SemVer>() { SemVer.V100 });

      await ReceiveMessageLoop(Client, connectedProfileServerMessageBuilder);

      connectedProfileServerWantsUpdates = false;
      connectedProfileServer = null;
      Client.Dispose();

      log.Debug("(-)");
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
                  log.Debug("Profile server is now connected to its LOC server and waiting for updates.");
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

      List<NodeInfo> neighborList;
      lock (neighborsLock)
      {
        neighborList = new List<NodeInfo>(neighbors);
      }

      res = MessageBuilder.CreateGetNeighbourNodesByDistanceLocalResponse(RequestMessage, neighborList);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }




    /// <summary>
    /// Sets neighborhood of the profile server during the load from the snapshot.
    /// </summary>
    /// <param name="NeighborhoodList">List of neighbors the profile server's neighborhood.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool SetNeighborhood(List<NodeInfo> NeighborhoodList)
    {
      log.Trace("(NeighborhoodList.Count:{0})", NeighborhoodList.Count);

      bool res = false;
      lock (neighborsLock)
      {
        neighbors = new List<NodeInfo>(NeighborhoodList);
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
      profileServerPort = (int)registerServiceRequest.Service.Port;

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
      profileServerPort = 0;

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }

    /// <summary>
    /// Returns list of related profile server's neighbors.
    /// </summary>
    /// <returns>List of related profile server's neigbhors.</returns>
    public List<NodeInfo> GetNeighbors()
    {
      List<NodeInfo> res = null;

      lock (neighborsLock)
      {
        res = new List<NodeInfo>(neighbors);
      }

      return res;
    }


    /// <summary>
    /// Waits until the profile server connects and asks for updates.
    /// </summary>
    public async Task WaitForProfileServerConnectionAsync()
    {
      while (!connectedProfileServerWantsUpdates)
      {
        try
        {
          await Task.Delay(500, shutdownCancellationTokenSource.Token);
        }
        catch
        {
          break;
        }
      }

      await Task.Delay(1000);
    }


    /// <summary>
    /// Sends changes notifications to the connected profile server.
    /// </summary>
    /// <param name="Change">Change to send.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SendChangeNotification(NeighbourhoodChange Change)
    {
      return await SendChangeNotifications(new List<NeighbourhoodChange>() { Change });
    }

    /// <summary>
    /// Sends changes notifications to the connected profile server.
    /// </summary>
    /// <param name="Changes">List of changes to send.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SendChangeNotifications(List<NeighbourhoodChange> Changes)
    {
      log.Trace("()");

      bool res = false;

      LocProtocolMessage message = connectedProfileServerMessageBuilder.CreateNeighbourhoodChangedNotificationRequest(Changes);
      res = await SendMessageAsync(connectedProfileServer, message);

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
