using System;
using ProfileServer.Kernel;
using ProfileServer.Kernel.Config;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using ProfileServer.Utils;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO;
using ProfileServerProtocol;
using Iop.Locnet;
using ProfileServer.Data;
using System.Linq;
using ProfileServer.Data.Models;
using Microsoft.EntityFrameworkCore.Storage;
using ProfileServerCrypto;
using Newtonsoft.Json;

namespace ProfileServer.Network
{
  /// <summary>
  /// Location based network (LOC) is a part of IoP that the profile server relies on.
  /// When the profile server starts, this component connects to LOC and obtains information about the server's neighborhood.
  /// Then it keeps receiving updates from LOC about changes in the neighborhood structure.
  /// The profile server needs to share its database of hosted identities with its neighbors and it also accepts 
  /// requests to share foreign profiles and consider them during its own search queries.
  /// </summary>
  public class LocationBasedNetwork : Component
  {
#warning Regtest needed - LOC server refresh
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.LocationBasedNetwork");

    /// <summary>Event that is set when LocConnectionThread is not running.</summary>
    private ManualResetEvent locConnectionThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for communication with LOC.</summary>
    private Thread locConnectionThread;

    /// <summary>TCP client to connect with LOC server.</summary>
    private TcpClient client;

    /// <summary>Network stream of the TCP connection to LOC server.</summary>
    private NetworkStream stream;

    /// <summary>Lock object for writing to the stream.</summary>
    private SemaphoreSlim streamWriteLock = new SemaphoreSlim(1);

    /// <summary>LOC message builder for the TCP client.</summary>
    private MessageBuilderLocNet messageBuilder;

    /// <summary>true if the component received current information about the server's neighborhood from the LOC server.</summary>
    private bool locServerInitialized = false;
    /// <summary>true if the component received current information about the server's neighborhood from the LOC server.</summary>
    public bool LocServerInitialized { get { return locServerInitialized; } }

    /// <summary>
    /// When this event triggers the message loop of the currently established connection to the LOC server is interrupted, 
    /// which causes the profile server to disconnect from LOC server
    /// </summary>
    private AutoResetEvent interruptLocMessageLoopEvent = new AutoResetEvent(false);


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        locConnectionThread = new Thread(new ThreadStart(LocConnectionThread));
        locConnectionThread.Start();

        res = true;
        Initialized = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();

        CloseClient();

        if ((locConnectionThread != null) && !locConnectionThreadFinished.WaitOne(10000))
          log.Error("LOC connection thread did not terminated in 10 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      CloseClient();

      if ((locConnectionThread != null) && !locConnectionThreadFinished.WaitOne(10000))
        log.Error("LOC connection thread did not terminated in 10 seconds.");

      log.Info("(-)");
    }

    /// <summary>
    /// Frees resources used by the TCP client.
    /// </summary>
    private void CloseClient()
    {
      log.Info("()");

      if (stream != null) stream.Dispose();
      if (client != null) client.Dispose();

      log.Info("(-)");
    }


    /// <summary>
    /// Thread that is responsible for connection to LOC and processing LOC updates.
    /// If the LOC is not reachable, the thread will wait until it is reachable.
    /// If connection to LOC is established and closed for any reason, the thread will try to reconnect.
    /// </summary>
    private async void LocConnectionThread()
    {
      LogDiagnosticContext.Start();

      log.Info("()");

      locConnectionThreadFinished.Reset();

      try
      {
        while (!ShutdownSignaling.IsShutdown)
        {
          // Connect to LOC server.
          if (await Connect())
          {
            // Announce our primary server interface to LOC.
            if (await RegisterPrimaryServerRole())
            {
              // Ask LOC server about initial set of neighborhood nodes.
              if (await GetNeighborhoodInformation())
              {
                // Receive and process updates.
                await ReceiveMessageLoop();
              }

              await DeregisterPrimaryServerRole();
            }
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred (and rethrowing): {0}", e.ToString());
        await Task.Delay(5000);
        throw e;
      }

      CloseClient();
      locConnectionThreadFinished.Set();

      log.Info("(-)");

      LogDiagnosticContext.Stop();
    }


    /// <summary>
    /// Attempts to connect to LOC server in a loop.
    /// </summary>
    /// <returns>true if the function succeeded, false if connection was established before the component shutdown.</returns>
    private async Task<bool> Connect()
    {
      log.Trace("()");
      bool res = false;

      // Close TCP connection and dispose client in case it is connected.
      CloseClient();

      // Create new TCP client.
      client = new TcpClient();
      client.NoDelay = true;
      client.LingerState = new LingerOption(true, 0);
      messageBuilder = new MessageBuilderLocNet(0, new List<SemVer> { SemVer.V100 });

      while (!res && !ShutdownSignaling.IsShutdown)
      {
        try
        {
          log.Trace("Connecting to LOC server '{0}'.", Base.Configuration.LocEndPoint);
          await client.ConnectAsync(Base.Configuration.LocEndPoint.Address, Base.Configuration.LocEndPoint.Port);
          stream = client.GetStream();
          res = true;
        }
        catch
        {
          log.Warn("Unable to connect to LOC server '{0}', waiting 10 seconds and then retrying.", Base.Configuration.LocEndPoint);
        }

        if (!res)
        {
          try
          {
            await Task.Delay(10000, ShutdownSignaling.ShutdownCancellationTokenSource.Token);
          }
          catch
          {
            // Catch cancellation exception.
          }
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Announces profile server's primary server role interface to the LOC server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> RegisterPrimaryServerRole()
    {
      log.Info("()");

      bool res = false;

      uint primaryPort = (uint)Base.Configuration.ServerRoles.GetRolePort(ServerRole.Primary);
      ServiceInfo serviceInfo = new ServiceInfo()
      {
        Port = primaryPort,
        Type = ServiceType.Profile,
        ServiceData = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(Base.Configuration.Keys.PublicKey))
      };
      
      Message request = messageBuilder.CreateRegisterServiceRequest(serviceInfo);
      if (await SendMessageAsync(request))
      {
        RawMessageReader messageReader = new RawMessageReader(stream);
        RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(ShutdownSignaling.ShutdownCancellationTokenSource.Token);
        if (rawMessage.Data != null)
        {
          Message response = CreateMessageFromRawData(rawMessage.Data);
          if (response != null)
          {
            res = (response.Id == request.Id)
              && (response.MessageTypeCase == Message.MessageTypeOneofCase.Response)
              && (response.Response.Status == Status.Ok)
              && (response.Response.ResponseTypeCase == Response.ResponseTypeOneofCase.LocalService)
              && (response.Response.LocalService.LocalServiceResponseTypeCase == LocalServiceResponse.LocalServiceResponseTypeOneofCase.RegisterService);

            if (res) log.Debug("Primary interface has been registered successfully on LOC server.");
            else log.Error("Registration failed, response status is {0}.", response.Response != null ? response.Response.Status.ToString() : "n/a");
          }
          else log.Error("Invalid message received from LOC server.");
        }
        else log.Error("Connection to LOC server has been terminated.");
      }
      else log.Error("Unable to send register server request to LOC server.");

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Cancels registration of profile server's primary server role interface on the LOC server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> DeregisterPrimaryServerRole()
    {
      log.Info("()");

      bool res = false;

      Message request = messageBuilder.CreateDeregisterServiceRequest(ServiceType.Profile);
      if (await SendMessageAsync(request))
      {
        RawMessageReader messageReader = new RawMessageReader(stream);
        RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(ShutdownSignaling.ShutdownCancellationTokenSource.Token);
        if (rawMessage.Data != null)
        {
          Message response = CreateMessageFromRawData(rawMessage.Data);
          if (response != null)
          {
            res = (response.Id == request.Id)
              && (response.MessageTypeCase == Message.MessageTypeOneofCase.Response)
              && (response.Response.Status == Status.Ok)
              && (response.Response.ResponseTypeCase == Response.ResponseTypeOneofCase.LocalService)
              && (response.Response.LocalService.LocalServiceResponseTypeCase == LocalServiceResponse.LocalServiceResponseTypeOneofCase.DeregisterService);

            if (res) log.Debug("Primary interface has been unregistered successfully on LOC server.");
            else log.Error("Deregistration failed, response status is {0}.", response.Response != null ? response.Response.Status.ToString() : "n/a");
          }
          else log.Error("Invalid message received from LOC server.");
        }
        else log.Debug("Connection to LOC server has been terminated.");
      }
      else log.Debug("Unable to send deregister server request to LOC server.");

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends a request to the LOC server to obtain an initial neighborhood information and then reads the response and processes it.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> GetNeighborhoodInformation()
    {
      log.Info("()");

      bool res = false;
      Message request = messageBuilder.CreateGetNeighbourNodesByDistanceLocalRequest();
      if (await SendMessageAsync(request))
      {
        // Read response.
        bool responseOk = false;
        Message response = null;
        RawMessageReader messageReader = new RawMessageReader(stream);
        RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(ShutdownSignaling.ShutdownCancellationTokenSource.Token);
        if (rawMessage.Data != null)
        {
          response = CreateMessageFromRawData(rawMessage.Data);
          if (response != null)
          {
            responseOk = (response.Id == request.Id)
              && (response.MessageTypeCase == Message.MessageTypeOneofCase.Response)
              && (response.Response.Status == Status.Ok)
              && (response.Response.ResponseTypeCase == Response.ResponseTypeOneofCase.LocalService)
              && (response.Response.LocalService.LocalServiceResponseTypeCase == LocalServiceResponse.LocalServiceResponseTypeOneofCase.GetNeighbourNodes);

            if (!responseOk) log.Error("Obtaining neighborhood information failed, response status is {0}.", response.Response != null ? response.Response.Status.ToString() : "n/a");
          }
          else log.Error("Invalid message received from LOC server.");
        }
        else log.Error("Connection to LOC server has been terminated.");

        // Process the response if valid and contains neighbors.
        if (responseOk)
          res = await ProcessMessageGetNeighbourNodesByDistanceResponseAsync(response, true);
      }
      else log.Error("Unable to send GetNeighbourNodesByDistanceLocalRequest to LOC server.");

      log.Info("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Reads update messages from network stream and processes them in a loop until the connection terminates 
    /// or until an action (such as a protocol violation) that leads to termination of the connection occurs.
    /// </summary>
    private async Task ReceiveMessageLoop()
    {
      log.Info("()");

      try
      {
        RawMessageReader messageReader = new RawMessageReader(stream);
        while (!ShutdownSignaling.IsShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(ShutdownSignaling.ShutdownCancellationTokenSource.Token);
          bool disconnect = rawMessage.Data == null;
          bool protocolViolation = rawMessage.ProtocolViolation;
          if (rawMessage.Data != null)
          {
            Message message = CreateMessageFromRawData(rawMessage.Data);
            if (message != null) disconnect = !await ProcessMessageAsync(message);
            else protocolViolation = true;
          }

          if (protocolViolation)
          {
            await SendProtocolViolation();
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

      log.Info("(-)");
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
        string msgStr = res.ToString();
        log.Trace("Received message:\n{0}", msgStr.SubstrMax(512));
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
    public async Task SendProtocolViolation()
    {
      MessageBuilderLocNet mb = new MessageBuilderLocNet(0, new List<SemVer> { SemVer.V100 });
      Message response = mb.CreateErrorProtocolViolationResponse(new Message() { Id = 0x0BADC0DE });

      await SendMessageAsync(response);
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public async Task<bool> SendMessageAsync(Message Message)
    {
      log.Trace("()");

      bool res = await SendMessageInternalAsync(Message);
      if (res)
      {
        // If the message was sent successfully to the target, we close the connection only in case of protocol violation error.
        if (Message.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          res = Message.Response.Status != Status.ErrorProtocolViolation;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the message was sent successfully to the target recipient.</returns>
    private async Task<bool> SendMessageInternalAsync(Message Message)
    {
      log.Trace("()");

      bool res = false;

      string msgStr = Message.ToString();
      log.Trace("Sending message:\n{0}", msgStr.SubstrMax(512));
      byte[] responseBytes = ProtocolHelper.GetMessageBytes(Message);

      await streamWriteLock.WaitAsync();
      try
      {
        if (stream != null)
        {
          await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
          res = true;
        }
        else log.Info("Connection to the client has been terminated.");
      }
      catch (Exception e)
      {
        if ((e is IOException) || (e is ObjectDisposedException))
        {
          log.Info("Connection to the client has been terminated.");
        }
        else
        {
          log.Error("Exception occurred (and rethrowing): {0}", e.ToString());
          await Task.Delay(5000);
          throw e;
        }
      }
      finally
      {
        streamWriteLock.Release();
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(Message IncomingMessage)
    {
      log.Debug("()");

      bool res = false;
      try
      {
        log.Trace("Received message type is {0}, message ID is {1}.", IncomingMessage.MessageTypeCase, IncomingMessage.Id);

        switch (IncomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              Message responseMessage = messageBuilder.CreateErrorProtocolViolationResponse(IncomingMessage);
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
                      case LocalServiceRequest.LocalServiceRequestTypeOneofCase.NeighbourhoodChanged:
                        {
                          responseMessage = await ProcessMessageNeighbourhoodChangedNotificationRequestAsync(IncomingMessage);
                          break;
                        }

                      default:
                        log.Warn("Invalid local service request type '{0}'.", request.LocalService.LocalServiceRequestTypeCase);
                        break;
                    }

                    break;
                  }

                default:
                  log.Warn("Invalid request type '{0}'.", request.RequestTypeCase);
                  break;
              }


              if (responseMessage != null)
              {
                // Send response to client.
                res = await SendMessageAsync(responseMessage);
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

              // The only response we should ever receive here is GetNeighbourNodesByDistanceResponse in response to our refresh request that we do from time to time.
              bool isGetNeighbourNodesByDistanceResponse = (response.Status == Status.Ok)
                && (response.ResponseTypeCase == Response.ResponseTypeOneofCase.LocalService)
                && (response.LocalService.LocalServiceResponseTypeCase == LocalServiceResponse.LocalServiceResponseTypeOneofCase.GetNeighbourNodes);

              if (!isGetNeighbourNodesByDistanceResponse)
              {
                log.Error("Unexpected response type {0} received, status code {1}.", response.ResponseTypeCase, response.Status);
                break;
              }

              // Process the response.
              res = await ProcessMessageGetNeighbourNodesByDistanceResponseAsync(IncomingMessage, false);
              break;
            }

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", IncomingMessage.MessageTypeCase);
            await SendProtocolViolation();
            // Connection will be closed in ReceiveMessageLoop.
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        await SendProtocolViolation();
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Debug("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes GetNeighbourNodesByDistanceResponse message received from LOC server.
    /// <para>This message contains information about profile server's neighbors, with which it should share its profile database.</para>
    /// </summary>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="IsInitialization">true if the response was received to the request during the LOC initialization, false if it was received to the refresh request after the initialization.</param>
    /// <returns>true if the connection to the LOC server should remain open, false if it should be closed.</returns>
    public async Task<bool> ProcessMessageGetNeighbourNodesByDistanceResponseAsync(Message ResponseMessage, bool IsInitialization)
    {
      log.Trace("(IsInitialization:{0})", IsInitialization);

      bool res = false;
      bool signalActionProcessor = false;

      GetNeighbourNodesByDistanceResponse getNeighbourNodesByDistanceResponse = ResponseMessage.Response.LocalService.GetNeighbourNodes;
      if (getNeighbourNodesByDistanceResponse.Nodes.Count > 0)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborLock, UnitOfWork.NeighborhoodActionLock };
          using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
          {
            bool success = false;
            bool saveDb = false;
            try
            {
              int neighborhoodSize = await unitOfWork.NeighborRepository.CountAsync();

              foreach (NodeInfo nodeInfo in getNeighbourNodesByDistanceResponse.Nodes)
              {
                // Check whether a profile server is running on this node.
                // If not, it is not interesting for us at all, skip it.
                int profileServerPort;
                byte[] profileServerId;
                if (!HasProfileServerService(nodeInfo, out profileServerPort, out profileServerId)) continue;

                NodeContact contact = nodeInfo.Contact;
                IPAddress ipAddress = new IPAddress(contact.IpAddress.ToByteArray());
                
                int latitude = nodeInfo.Location.Latitude;
                int longitude = nodeInfo.Location.Longitude;

                AddOrChangeNeighborResult addChangeRes = await AddOrChangeNeighbor(unitOfWork, profileServerId, ipAddress, profileServerPort, latitude, longitude, neighborhoodSize);

                neighborhoodSize = addChangeRes.NeighborhoodSize;

                if (addChangeRes.SaveDb)
                  saveDb = true;

                if (addChangeRes.SignalActionProcessor)
                  signalActionProcessor = true;

                // We do ignore errors here and just continue processing another item from the list.
              }

              if (saveDb)
              {
                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
              }
              success = true;
              res = true;
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

            unitOfWork.ReleaseLock(lockObjects);
          }
        }
      }
      else
      {
        log.Debug("No neighbors announced by LOC server.");
        res = true;
      }

      if (signalActionProcessor) 
      {
        NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary["Network.NeighborhoodActionProcessor"];
        neighborhoodActionProcessor.Signal();
      }

      if (res && IsInitialization)
      {
        log.Debug("LOC component is now considered in sync with LOC server.");
        locServerInitialized = true;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>Result of AddOrChangeNeighbor call.</summary>
    public struct AddOrChangeNeighborResult
    {
      /// <summary>If an error occurs, this is set to true.</summary>
      public bool Error;

      /// <summary>If a change was made to the database and we require it to be saved, this is set to true.</summary>
      public bool SaveDb;
      
      /// <summary>If a new neighborhood action was added to the database and we want to signal action processor, this is set to true.</summary>
      public bool SignalActionProcessor;

      /// <summary>Size of the neighborhood including newly added servers, if any.</summary>
      public int NeighborhoodSize;
    }

    /// <summary>
    /// Processes update received from LOC server that informs the profile server about a new neighbor server or a change in existing neighbor server contact information.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="ServerId">Network identifier of the neighbor server.</param>
    /// <param name="IpAddress">IP address of the neighbor server.</param>
    /// <param name="Port">Primary interface port of the neighbor server.</param>
    /// <param name="Latitude">GPS location latitude of the neighbor server.</param>
    /// <param name="Longitude">GPS location longitude of the neighbor server.</param>
    /// <param name="NeighborhoodSize">Size of the profile server's neighborhood at the moment the function is called.</param>
    /// <returns>Information related to how should the caller proceed further, described in AddOrChangeNeighborResult structure.</returns>
    /// <remarks>The caller is responsible for calling this function within a database transaction with NeighborLock and NeighborhoodActionLock locks.</remarks>
    public async Task<AddOrChangeNeighborResult> AddOrChangeNeighbor(UnitOfWork UnitOfWork, byte[] ServerId, IPAddress IpAddress, int Port, int Latitude, int Longitude, int NeighborhoodSize)
    {
      log.Trace("(ServerId:'{0}',IpAddress:{1},Port:{2},Latitude:{3},Longitude:{4},NeighborhoodSize:{5})", ServerId.ToHex(), IpAddress, Port, Latitude, Longitude, NeighborhoodSize);

      AddOrChangeNeighborResult res = new AddOrChangeNeighborResult();
      res.NeighborhoodSize = NeighborhoodSize;

      // Data validation.
      bool serverIdValid = ServerId.Length == IdentityBase.IdentifierLength;
      if (!serverIdValid)
      {
        log.Error("Received invalid neighbor server ID '{0}' from LOC server.", ServerId.ToHex());
        res.Error = true;
        log.Trace("(-):*.Error={0},*.SaveDb={1},*.SignalActionProcessor={2},*.NeighborhoodSize={3}", res.Error, res.SaveDb, res.SignalActionProcessor, res.NeighborhoodSize);
        return res;
      }

      bool portValid = (0 < Port) && (Port <= 65535);
      if (!portValid)
      {
        log.Error("Received invalid neighbor server port '{0}' from LOC server.", Port);
        res.Error = true;
        log.Trace("(-):*.Error={0},*.SaveDb={1},*.SignalActionProcessor={2},*.NeighborhoodSize={3}", res.Error, res.SaveDb, res.SignalActionProcessor, res.NeighborhoodSize);
        return res;
      }

      ProfileServerProtocol.GpsLocation location = new ProfileServerProtocol.GpsLocation(Latitude, Longitude);
      if (!location.IsValid())
      {
        log.Error("Received invalid neighbor server location '{0}' from LOC server.", location);
        res.Error = true;
        log.Trace("(-):*.Error={0},*.SaveDb={1},*.SignalActionProcessor={2},*.NeighborhoodSize={3}", res.Error, res.SaveDb, res.SignalActionProcessor, res.NeighborhoodSize);
        return res;
      }

      // Data processing.
      Neighbor existingNeighbor = (await UnitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == ServerId)).FirstOrDefault();
      if (existingNeighbor == null)
      {
        // New neighbor server.
        if (NeighborhoodSize < Base.Configuration.MaxNeighborhoodSize)
        {
          // We have not reached the maximal size of the neighborhood yet, the server can be added.
          log.Trace("New neighbor ID '{0}' detected, IP address {1}, port {2}, latitude {3}, longitude {4}.", ServerId.ToHex(), IpAddress, Port, Latitude, Longitude);

          // Add neighbor to the database of neighbors.
          // The neighbor is not initialized (LastRefreshTime is not set), so we will not allow it to send us
          // any updates. First, we need to contact it and start the neighborhood initialization process.
          Neighbor neighbor = new Neighbor()
          {
            NeighborId = ServerId,
            IpAddress = IpAddress.ToString(),
            PrimaryPort = Port,
            SrNeighborPort = null,
            LocationLatitude = location.Latitude,
            LocationLongitude = location.Longitude,
            LastRefreshTime = null,
            SharedProfiles = 0
          };
          await UnitOfWork.NeighborRepository.InsertAsync(neighbor);
          res.NeighborhoodSize++;

          // This action will cause our profile server to contact the new neighbor server and ask it to share its profile database,
          // i.e. the neighborhood initialization process will be started.
          // We set a delay depending on the number of neighbors, so that a new server joining a neighborhood is not overwhelmed with requests.
          int delay = RandomSource.Generator.Next(0, 3 * res.NeighborhoodSize);

          NeighborhoodAction action = new NeighborhoodAction()
          {
            ServerId = ServerId,
            Timestamp = DateTime.UtcNow,
            Type = NeighborhoodActionType.AddNeighbor,
            ExecuteAfter = DateTime.UtcNow.AddSeconds(delay),
            TargetIdentityId = null,
            AdditionalData = null,            
          };
          await UnitOfWork.NeighborhoodActionRepository.InsertAsync(action);

          res.SignalActionProcessor = true;
          res.SaveDb = true;
        }
        else log.Error("Unable to add new neighbor ID '{0}', the profile server reached its neighborhood size limit {1}.", ServerId.ToHex(), Base.Configuration.MaxNeighborhoodSize);
      }
      else
      {
        // This is a neighbor we already know about. Just check that its information is up to date and if not, update it.
        string ipAddress = IpAddress.ToString();
        if (existingNeighbor.IpAddress != ipAddress)
        {
          log.Trace("Existing neighbor ID '{0}' changed its IP address from {1} to {2}.", ServerId.ToHex(), existingNeighbor.IpAddress, ipAddress);
          existingNeighbor.IpAddress = ipAddress;
        }

        if (existingNeighbor.PrimaryPort != Port)
        {
          // Primary port was change, so we also expect that the neighbors interface port was changed as well.
          log.Trace("Existing neighbor ID '{0}' changed its primary port from {1} to {2}, invalidating neighbors interface port as well.", ServerId.ToHex(), existingNeighbor.PrimaryPort, Port);
          existingNeighbor.PrimaryPort = Port;
          existingNeighbor.SrNeighborPort = null;
        }

        if (existingNeighbor.LocationLatitude != location.Latitude)
        {
          log.Trace("Existing neighbor ID '{0}' changed its latitude from {1} to {2}.", ServerId.ToHex(), existingNeighbor.LocationLatitude, location.Latitude);
          existingNeighbor.LocationLatitude = Latitude;
        }

        if (existingNeighbor.LocationLongitude != location.Longitude)
        {
          log.Trace("Existing neighbor ID '{0}' changed its longitude from {1} to {2}.", ServerId.ToHex(), existingNeighbor.LocationLongitude, location.Longitude);
          existingNeighbor.LocationLongitude = Longitude;
        }

        // We consider a fresh LOC info to be accurate, so we do not want to delete the neighbors received here
        // and hence we update their refresh time..
        existingNeighbor.LastRefreshTime = DateTime.UtcNow;

        UnitOfWork.NeighborRepository.Update(existingNeighbor);
        res.SaveDb = true;
      }

      log.Trace("(-):*.Error={0},*.SaveDb={1},*.SignalActionProcessor={2},*.NeighborhoodSize={3}", res.Error, res.SaveDb, res.SignalActionProcessor, res.NeighborhoodSize);
      return res;
    }


    /// <summary>
    /// Processes NeighbourhoodChangedNotificationRequest message from LOC server.
    /// <para>Adds corresponding neighborhood action to the database.</para>
    /// </summary>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageNeighbourhoodChangedNotificationRequestAsync(Message RequestMessage)
    {
      log.Trace("()");

      Message res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      bool signalActionProcessor = false;

      NeighbourhoodChangedNotificationRequest neighbourhoodChangedNotificationRequest = RequestMessage.Request.LocalService.NeighbourhoodChanged;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborLock, UnitOfWork.NeighborhoodActionLock };
        using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
        {
          bool success = false;
          bool saveDb = false;
          try
          {
            int neighborhoodSize = await unitOfWork.NeighborRepository.CountAsync();

            foreach (NeighbourhoodChange change in neighbourhoodChangedNotificationRequest.Changes)
            {
              // We do ignore errors here for each individual change and just continue processing a next item from the list.
              log.Trace("Neighborhood change type is {0}.", change.ChangeTypeCase);
              switch (change.ChangeTypeCase)
              {
                case NeighbourhoodChange.ChangeTypeOneofCase.AddedNodeInfo:
                case NeighbourhoodChange.ChangeTypeOneofCase.UpdatedNodeInfo:
                  {
                    bool isAdd = change.ChangeTypeCase == NeighbourhoodChange.ChangeTypeOneofCase.AddedNodeInfo;
                    NodeInfo nodeInfo = isAdd ? change.AddedNodeInfo : change.UpdatedNodeInfo;

                    // Check whether a profile server is running on this node.
                    // If not, it is not interesting for us at all, skip it.
                    int profileServerPort;
                    byte[] profileServerId;
                    if (!HasProfileServerService(nodeInfo, out profileServerPort, out profileServerId)) break;

                    NodeContact contact = nodeInfo.Contact;
                    IPAddress ipAddress = new IPAddress(contact.IpAddress.ToByteArray());
                    Iop.Locnet.GpsLocation location = nodeInfo.Location;
                    int latitude =  location.Latitude;
                    int longitude = location.Longitude;

                    AddOrChangeNeighborResult addChangeRes = await AddOrChangeNeighbor(unitOfWork, profileServerId, ipAddress, profileServerPort, latitude, longitude, neighborhoodSize);

                    neighborhoodSize = addChangeRes.NeighborhoodSize;

                    if (addChangeRes.SaveDb)
                      saveDb = true;

                    if (addChangeRes.SignalActionProcessor)
                      signalActionProcessor = true;

                    break;
                  }

                case NeighbourhoodChange.ChangeTypeOneofCase.RemovedNodeId:
                  {
                    byte[] serverId = change.RemovedNodeId.ToByteArray();

                    bool serverIdValid = serverId.Length == IdentityBase.IdentifierLength;
                    if (!serverIdValid)
                    {
                      log.Error("Received invalid neighbor server ID '{0}' from LOC server.", serverId.ToHex());
                      break;
                    }

                    // Data processing.
                    Neighbor existingNeighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == serverId)).FirstOrDefault();
                    if (existingNeighbor != null)
                    {
                      log.Trace("Creating neighborhood action to deleting neighbor ID '{0}' from the database.", serverId.ToHex());

                      string neighborInfo = JsonConvert.SerializeObject(existingNeighbor);

                      // Delete neighbor completely.
                      // This will cause our profile server to erase all profiles of the neighbor that has been removed.
                      bool deleted = await unitOfWork.NeighborRepository.DeleteNeighbor(unitOfWork, serverId, -1, true);
                      if (deleted)
                      {
                        // Add action that will contact the neighbor and ask it to stop sending updates.
                        // Note that the neighbor information will be deleted by the time this action 
                        // is executed and this is why we have to fill in AdditionalData.
                        NeighborhoodAction stopUpdatesAction = new NeighborhoodAction()
                        {
                          ServerId = serverId,
                          Type = NeighborhoodActionType.StopNeighborhoodUpdates,
                          TargetIdentityId = null,
                          ExecuteAfter = DateTime.UtcNow,
                          Timestamp = DateTime.UtcNow,
                          AdditionalData = neighborInfo
                        };
                        await unitOfWork.NeighborhoodActionRepository.InsertAsync(stopUpdatesAction);

                        signalActionProcessor = true;
                        saveDb = true;
                      }
                      else
                      {
                        log.Error("Failed to remove neighbor ID '{0}' from the database.", serverId.ToHex());
                        // This is actually bad, we failed to remove a record from the database, which should never happen.
                        // We try to insert action to remove this neighbor later, but adding the action might fail as well.
                        NeighborhoodAction action = new NeighborhoodAction()
                        {
                          ServerId = serverId,
                          Timestamp = DateTime.UtcNow,
                          Type = NeighborhoodActionType.RemoveNeighbor,
                          TargetIdentityId = null,
                          AdditionalData = null
                        };
                        await unitOfWork.NeighborhoodActionRepository.InsertAsync(action);

                        signalActionProcessor = true;
                        saveDb = true;
                      }
                    }
                    else
                    {
                      log.Debug("Neighbor ID '{0}' not found, can not be removed.", serverId.ToHex());
                      // It can be the case that this node has not an associated profile server, so in that case we should ignore it.
                      // If the node has an associated profile server, then nothing bad really happens here if we have profiles 
                      // of such a neighbor in NeighborIdentity table. Those entries will expire and will be deleted.
                    }
                    break;
                  }

                default:
                  log.Error("Invalid neighborhood change type '{0}'.", change.ChangeTypeCase);
                  break;
              }
            }

            if (saveDb)
            {
              await unitOfWork.SaveThrowAsync();
              transaction.Commit();
            }
            success = true;
            res = messageBuilder.CreateNeighbourhoodChangedNotificationResponse(RequestMessage);
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

          unitOfWork.ReleaseLock(lockObjects);
        }
      }

      if (signalActionProcessor)
      {
        NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary["Network.NeighborhoodActionProcessor"];
        neighborhoodActionProcessor.Signal();
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// If the component is connected to LOC server, it sends a new GetNeighbourNodesByDistanceLocalRequest request 
    /// to get fresh information about the profile server's neighborhood.
    /// </summary>
    public void RefreshLoc()
    {
      log.Trace("()");

      if (!ShutdownSignaling.IsShutdown && locServerInitialized)
      {
        try
        {
          Message request = messageBuilder.CreateGetNeighbourNodesByDistanceLocalRequest();
          if (SendMessageAsync(request).Result)
          {
            log.Trace("GetNeighbourNodesByDistanceLocalRequest sent to LOC server to get fresh neighborhood data.");
          }
          else log.Warn("Unable to send message to LOC server.");
        }
        catch (Exception e)
        {
          log.Warn("Exception occurred: {0}", e.ToString());
        }
      }

      log.Trace("(-)");
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
          bool serviceDataValid = si.ServiceData.Length == IdentityBase.IdentifierLength;
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
