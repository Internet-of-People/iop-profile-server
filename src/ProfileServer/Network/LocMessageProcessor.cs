using Google.Protobuf;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using ProfileServer.Data.Repositories;
using ProfileServer.Kernel;
using IopCrypto;
using IopProtocol;
using Iop.Locnet;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;
using IopServerCore.Network;
using IopServerCore.Network.CAN;
using IopServerCore.Network.LOC;
using System.Net;
using Newtonsoft.Json;

namespace ProfileServer.Network
{
  /// <summary>
  /// Implements the logic behind processing incoming messages from the LOC server.
  /// </summary>
  public class LocMessageProcessor: IMessageProcessor
  {
    /// <summary>Instance logger.</summary>
    private Logger log;

    /// <summary>Pointer to the Network.LocationBasedNetwork component.</summary>
    private LocationBasedNetwork serverComponent;

    /// <summary>
    /// Creates a new instance connected to the parent role server.
    /// </summary>
    public LocMessageProcessor()
    {
      log = new Logger("ProfileServer.Network.LocMessageProcessor");
      serverComponent = (LocationBasedNetwork)Base.ComponentDictionary[LocationBasedNetwork.ComponentName];
    }


    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client who received the message.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(ClientBase Client, IProtocolMessage IncomingMessage)
    {
      LocClient client = (LocClient)Client;
      LocProtocolMessage incomingMessage = (LocProtocolMessage)IncomingMessage;

      log.Debug("()");

      bool res = false;
      try
      {
        log.Trace("Received message type is {0}, message ID is {1}.", incomingMessage.MessageTypeCase, incomingMessage.Id);

        switch (incomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              LocProtocolMessage responseMessage = client.MessageBuilder.CreateErrorProtocolViolationResponse(incomingMessage);
              Request request = incomingMessage.Request;

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
                          responseMessage = await ProcessMessageNeighbourhoodChangedNotificationRequestAsync(client, incomingMessage);
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
                res = await client.SendMessageAsync(responseMessage);

                if (res)
                {
                  // If the message was sent successfully to the target, we close the connection only in case of protocol violation error.
                  if (responseMessage.MessageTypeCase == Message.MessageTypeOneofCase.Response)
                    res = responseMessage.Response.Status != Status.ErrorProtocolViolation;
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
              Response response = incomingMessage.Response;
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
              res = await ProcessMessageGetNeighbourNodesByDistanceResponseAsync(incomingMessage, false);
              break;
            }

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", incomingMessage.MessageTypeCase);
            await SendProtocolViolation(client);
            // Connection will be closed in ReceiveMessageLoop.
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        await SendProtocolViolation(client);
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Debug("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation(ClientBase Client)
    {
      LocMessageBuilder mb = new LocMessageBuilder(0, new List<SemVer> { SemVer.V100 });
      LocProtocolMessage response = mb.CreateErrorProtocolViolationResponse(new LocProtocolMessage(new Message() { Id = 0x0BADC0DE }));

      await Client.SendMessageAsync(response);
    }


    /// <summary>
    /// Processes GetNeighbourNodesByDistanceResponse message received from LOC server.
    /// <para>This message contains information about profile server's neighbors, with which it should share its profile database.</para>
    /// </summary>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="IsInitialization">true if the response was received to the request during the LOC initialization, false if it was received to the refresh request after the initialization.</param>
    /// <returns>true if the connection to the LOC server should remain open, false if it should be closed.</returns>
    public async Task<bool> ProcessMessageGetNeighbourNodesByDistanceResponseAsync(LocProtocolMessage ResponseMessage, bool IsInitialization)
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
                byte[] ipBytes = contact.IpAddress.ToByteArray();
                IPAddress ipAddress = new IPAddress(ipBytes);

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
        NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary[NeighborhoodActionProcessor.ComponentName];
        neighborhoodActionProcessor.Signal();
      }

      if (res && IsInitialization)
      {
        log.Debug("LOC component is now considered in sync with LOC server.");
        serverComponent.SetLocServerInitialized(true);
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

      IopProtocol.GpsLocation location = new IopProtocol.GpsLocation(Latitude, Longitude);
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
        if (NeighborhoodSize < Config.Configuration.MaxNeighborhoodSize)
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
        else log.Error("Unable to add new neighbor ID '{0}', the profile server reached its neighborhood size limit {1}.", ServerId.ToHex(), Config.Configuration.MaxNeighborhoodSize);
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
        // and hence we update their refresh time.
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
    /// <param name="Client">TCP client who received the message.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<LocProtocolMessage> ProcessMessageNeighbourhoodChangedNotificationRequestAsync(LocClient Client, LocProtocolMessage RequestMessage)
    {
      log.Trace("()");

      LocProtocolMessage res = Client.MessageBuilder.CreateErrorInternalResponse(RequestMessage);
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
                    int latitude = location.Latitude;
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
            res = Client.MessageBuilder.CreateNeighbourhoodChangedNotificationResponse(RequestMessage);
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
        NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary[NeighborhoodActionProcessor.ComponentName];
        neighborhoodActionProcessor.Signal();
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
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
