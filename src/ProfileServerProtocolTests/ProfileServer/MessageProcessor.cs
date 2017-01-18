using Google.Protobuf;
using ProfileServerCrypto;
using ProfileServerProtocol;
using Iop.Profileserver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Implements the logic behind processing incoming messages to the node.
  /// </summary>
  public class MessageProcessor
  {
    private NLog.Logger log;

    /// <summary>Parent profile server.</summary>
    private ProfileServer profileServer;

    /// <summary>
    /// Creates a new instance connected to the parent profile server.
    /// </summary>
    /// <param name="ProfileServer">Parent profile server.</param>
    /// <param name="LogPrefix">Log prefix of the parent role server.</param>
    public MessageProcessor(ProfileServer ProfileServer)
    {
      log = NLog.LogManager.GetLogger(string.Format("Test.ProfileServer.{0}.MessageProcessor", ProfileServer.Name));
      log.Trace("()");

      profileServer = ProfileServer;

      log.Trace("(-)");
    }


    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client who send the message.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(IncomingClient Client, Message IncomingMessage)
    {
      MessageBuilder messageBuilder = Client.MessageBuilder;

      bool res = false;
      log.Debug("()");

      profileServer.AddMessage(IncomingMessage, Client.ServerRole);

      try
      {
        log.Trace("Received message type is {0}, message ID is {1}.", IncomingMessage.MessageTypeCase, IncomingMessage.Id);
        switch (IncomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              Message responseMessage = messageBuilder.CreateErrorProtocolViolationResponse(IncomingMessage);
              Request request = IncomingMessage.Request;
              log.Trace("Request conversation type is {0}.", request.ConversationTypeCase);
              switch (request.ConversationTypeCase)
              {
                case Request.ConversationTypeOneofCase.SingleRequest:
                  {
                    SingleRequest singleRequest = request.SingleRequest;
                    SemVer version = new SemVer(singleRequest.Version);
                    log.Trace("Single request type is {0}, version is {1}.", singleRequest.RequestTypeCase, version);

                    if (!version.IsValid())
                    {
                      responseMessage.Response.Details = "version";
                      break;
                    }

                    switch (singleRequest.RequestTypeCase)
                    {
                      case SingleRequest.RequestTypeOneofCase.Ping:
                        responseMessage = ProcessMessagePingRequest(Client, IncomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ListRoles:
                        responseMessage = ProcessMessageListRolesRequest(Client, IncomingMessage);
                        break;

                      default:
                        log.Warn("Invalid request type '{0}'.", singleRequest.RequestTypeCase);
                        break;
                    }

                    break;
                  }

                case Request.ConversationTypeOneofCase.ConversationRequest:
                  {
                    ConversationRequest conversationRequest = request.ConversationRequest;
                    log.Trace("Conversation request type is {0}.", conversationRequest.RequestTypeCase);
                    if (conversationRequest.Signature.Length > 0) log.Trace("Conversation signature is '{0}'.", Crypto.ToHex(conversationRequest.Signature.ToByteArray()));
                    else log.Trace("No signature provided.");

                    switch (conversationRequest.RequestTypeCase)
                    {
                      case ConversationRequest.RequestTypeOneofCase.Start:
                        responseMessage = ProcessMessageStartConversationRequest(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.VerifyIdentity:
                        responseMessage = ProcessMessageVerifyIdentityRequest(Client, IncomingMessage);
                        break;

                        /*
                      case ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization:
                        responseMessage = await ProcessMessageStartNeighborhoodInitializationRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                        responseMessage = ProcessMessageFinishNeighborhoodInitializationRequest(Client, IncomingMessage);
                        break;
                        */
                      case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate:
                        responseMessage = ProcessMessageNeighborhoodSharedProfileUpdateRequest(Client, IncomingMessage);
                        break;
                        /*
                      case ConversationRequest.RequestTypeOneofCase.StopNeighborhoodUpdates:
                        responseMessage = await ProcessMessageStopNeighborhoodUpdatesRequest(Client, IncomingMessage);
                        break;
                        */
                      default:
                        log.Warn("Invalid request type '{0}'.", conversationRequest.RequestTypeCase);
                        // Connection will be closed in ReceiveMessageLoop.
                        break;
                    }

                    break;
                  }

                default:
                  log.Error("Unknown conversation type '{0}'.", request.ConversationTypeCase);
                  // Connection will be closed in ReceiveMessageLoop.
                  break;
              }

              if (responseMessage != null)
              {
                // Send response to client.
                res = await Client.SendMessageAsync(responseMessage);
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
              log.Trace("Response status is {0}, details are '{1}', conversation type is {2}.", response.Status, response.Details, response.ConversationTypeCase);

              // Find associated request. If it does not exist, disconnect the client as it 
              // send a response without receiving a request. This is protocol violation, 
              // but as this is a reponse, we have no how to inform the client about it, 
              // so we just disconnect it.
              UnfinishedRequest unfinishedRequest = Client.GetAndRemoveUnfinishedRequest(IncomingMessage.Id);
              if ((unfinishedRequest != null) && (unfinishedRequest.RequestMessage != null))
              {
                Message requestMessage = unfinishedRequest.RequestMessage;
                Request request = requestMessage.Request;
                // We now check whether the response message type corresponds with the request type.
                // This is only valid if the status is Ok. If the message types do not match, we disconnect 
                // for the protocol violation again.
                bool typeMatch = false;
                bool isErrorResponse = response.Status != Status.Ok;
                if (!isErrorResponse)
                {
                  if (response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
                  {
                    typeMatch = (request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
                      && ((int)response.SingleResponse.ResponseTypeCase == (int)request.SingleRequest.RequestTypeCase);
                  }
                  else
                  {
                    typeMatch = (request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
                      && ((int)response.ConversationResponse.ResponseTypeCase == (int)request.ConversationRequest.RequestTypeCase);
                  }
                }
                else typeMatch = true;

                if (typeMatch)
                {
                  // Now we know the types match, so we can rely on request type even if response is just an error.
                  switch (request.ConversationTypeCase)
                  {
                    case Request.ConversationTypeOneofCase.SingleRequest:
                      {
                        SingleRequest singleRequest = request.SingleRequest;
                        switch (singleRequest.RequestTypeCase)
                        {
                          default:
                            log.Warn("Invalid conversation type '{0}' of the corresponding request.", request.ConversationTypeCase);
                            // Connection will be closed in ReceiveMessageLoop.
                            break;
                        }

                        break;
                      }

                    case Request.ConversationTypeOneofCase.ConversationRequest:
                      {
                        ConversationRequest conversationRequest = request.ConversationRequest;
                        switch (conversationRequest.RequestTypeCase)
                        {
                          /*case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate:
                            res = await ProcessMessageNeighborhoodSharedProfileUpdateResponseAsync(Client, IncomingMessage, unfinishedRequest);
                            break;

                          case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                            res = await ProcessMessageFinishNeighborhoodInitializationResponseAsync(Client, IncomingMessage, unfinishedRequest);
                            break;*/

                          default:
                            log.Warn("Invalid type '{0}' of the corresponding request.", conversationRequest.RequestTypeCase);
                            // Connection will be closed in ReceiveMessageLoop.
                            break;
                        }
                        break;
                      }

                    default:
                      log.Error("Unknown conversation type '{0}' of the corresponding request.", request.ConversationTypeCase);
                      // Connection will be closed in ReceiveMessageLoop.
                      break;
                  }
                }
                else
                {
                  log.Warn("Message type of the response ID {0} does not match the message type of the request ID {1}, the connection will be closed.", IncomingMessage.Id);
                  // Connection will be closed in ReceiveMessageLoop.
                }
              }
              else
              {
                log.Warn("No unfinished request found for incoming response ID {0}, the connection will be closed.", IncomingMessage.Id);
                // Connection will be closed in ReceiveMessageLoop.
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

      if (res && Client.ForceDisconnect)
      {
        log.Debug("Connection to the client will be forcefully closed.");
        res = false;
      }

      log.Debug("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation(IncomingClient Client)
    {
      MessageBuilder mb = new MessageBuilder(0, new List<SemVer>() { SemVer.V100 }, null);
      Message response = mb.CreateErrorProtocolViolationResponse(new Message() { Id = 0x0BADC0DE });

      await Client.SendMessageAsync(response);
    }



    /// <summary>
    /// Selects a common protocol version that both server and client support.
    /// </summary>
    /// <param name="ClientVersions">List of versions that the client supports. The list is ordered by client's preference.</param>
    /// <param name="SelectedCommonVersion">If the function succeeds, this is set to the selected version that both client and server support.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool GetCommonSupportedVersion(IEnumerable<ByteString> ClientVersions, out SemVer SelectedCommonVersion)
    {
      log.Trace("()");
      SelectedCommonVersion = SemVer.Invalid;

      SemVer selectedVersion = SemVer.Invalid;
      bool res = false;
      foreach (ByteString clVersion in ClientVersions)
      {
        SemVer version = new SemVer(clVersion);
        if (version.Equals(SemVer.V100))
        {
          SelectedCommonVersion = version;
          selectedVersion = version;
          res = true;
          break;
        }
      }

      if (res) log.Trace("(-):{0},SelectedCommonVersion='{1}'", res, selectedVersion);
      else log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Processes PingRequest message from client.
    /// <para>Simply copies the payload to a new ping response message.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessagePingRequest(IncomingClient Client, Message RequestMessage)
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
    public Message ProcessMessageListRolesRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      MessageBuilder messageBuilder = Client.MessageBuilder;

      Message res = messageBuilder.CreateErrorBadRoleResponse(RequestMessage);
      if (Client.ServerRole != ServerRole.Primary)
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      ListRolesRequest listRolesRequest = RequestMessage.Request.SingleRequest.ListRoles;

      List<Iop.Profileserver.ServerRole> roles = GetRolesFromServerComponent();
      res = messageBuilder.CreateListRolesResponse(RequestMessage, roles);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Creates a list of role servers to be sent to the requesting client.
    /// The information about role servers can be obtained from Network.Server component.
    /// </summary>
    /// <returns>List of role server descriptions.</returns>
    private List<Iop.Profileserver.ServerRole> GetRolesFromServerComponent()
    {
      log.Trace("()");

      List<Iop.Profileserver.ServerRole> res = new List<Iop.Profileserver.ServerRole>();
      Iop.Profileserver.ServerRole item = new Iop.Profileserver.ServerRole()
      {
        Role = ServerRoleType.Primary,
        Port = (uint)profileServer.PrimaryPort,
        IsTcp = true,
        IsTls = false
      };
      res.Add(item);

      item = new Iop.Profileserver.ServerRole()
      {
        Role = ServerRoleType.SrNeighbor,
        Port = (uint)profileServer.ServerNeighborPort,
        IsTcp = true,
        IsTls = true
      };
      res.Add(item);

      item = new Iop.Profileserver.ServerRole()
      {
        Role = ServerRoleType.ClNonCustomer,
        Port = (uint)profileServer.ServerNeighborPort,
        IsTcp = true,
        IsTls = true
      };
      res.Add(item);

      item = new Iop.Profileserver.ServerRole()
      {
        Role = ServerRoleType.ClCustomer,
        Port = (uint)profileServer.ServerNeighborPort,
        IsTcp = true,
        IsTls = true
      };
      res.Add(item);

      item = new Iop.Profileserver.ServerRole()
      {
        Role = ServerRoleType.ClAppService,
        Port = (uint)profileServer.ServerNeighborPort,
        IsTcp = true,
        IsTls = true
      };

      res.Add(item);

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }




    /// <summary>
    /// Processes StartConversationRequest message from client.
    /// <para>Initiates a conversation with the client provided that there is a common version of the protocol supported by both sides.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageStartConversationRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;


      MessageBuilder messageBuilder = Client.MessageBuilder;
      StartConversationRequest startConversationRequest = RequestMessage.Request.ConversationRequest.Start;
      byte[] clientChallenge = startConversationRequest.ClientChallenge.ToByteArray();
      byte[] pubKey = startConversationRequest.PublicKey.ToByteArray();

      if (clientChallenge.Length == ProtocolHelper.ChallengeDataSize)
      {
        SemVer version;
        if (GetCommonSupportedVersion(startConversationRequest.SupportedVersions, out version))
        {
          Client.PublicKey = pubKey;
          Client.IdentityId = Crypto.Sha256(Client.PublicKey);

          Client.MessageBuilder.SetProtocolVersion(version);

          byte[] challenge = new byte[ProtocolHelper.ChallengeDataSize];
          Crypto.Rng.GetBytes(challenge);
          Client.AuthenticationChallenge = challenge;
          Client.ConversationStatus = ClientConversationStatus.ConversationStarted;

          log.Debug("Client {0} conversation status updated to {1}, selected version is '{2}', client public key set to '{3}', client identity ID set to '{4}', challenge set to '{5}'.",
            Client.RemoteEndPoint, Client.ConversationStatus, version, Crypto.ToHex(Client.PublicKey), Crypto.ToHex(Client.IdentityId), Crypto.ToHex(Client.AuthenticationChallenge));

          res = messageBuilder.CreateStartConversationResponse(RequestMessage, version, profileServer.Keys.PublicKey, Client.AuthenticationChallenge, clientChallenge);
        }
        else
        {
          log.Warn("Client and server are incompatible in protocol versions.");
          res = messageBuilder.CreateErrorUnsupportedResponse(RequestMessage);
        }
      }
      else
      {
        log.Warn("Client send clientChallenge, which is {0} bytes long, but it should be {1} bytes long.", clientChallenge.Length, ProtocolHelper.ChallengeDataSize);
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "clientChallenge");
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
    public Message ProcessMessageVerifyIdentityRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      MessageBuilder messageBuilder = Client.MessageBuilder;
      Message res = messageBuilder.CreateErrorBadRoleResponse(RequestMessage);
      if ((Client.ServerRole != ServerRole.ServerNeighbor) && (Client.ServerRole != ServerRole.ClientNonCustomer))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      VerifyIdentityRequest verifyIdentityRequest = RequestMessage.Request.ConversationRequest.VerifyIdentity;

      byte[] challenge = verifyIdentityRequest.Challenge.ToByteArray();
      log.Debug("Identity '{0}' successfully verified its public key.", Crypto.ToHex(Client.IdentityId));
      Client.ConversationStatus = ClientConversationStatus.Verified;
      res = messageBuilder.CreateVerifyIdentityResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }





    /*
    /// <summary>
    /// Processes StartNeighborhoodInitializationRequest message from client.
    /// <para>If the server is not overloaded it accepts the neighborhood initialization request, 
    /// adds the client to the list of server for which the profile server acts as a neighbor,
    /// and starts sharing its profile database.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client, or null if no response is to be sent by the calling function.</returns>
    public async Task<Message> ProcessMessageStartNeighborhoodInitializationRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;

      MessageBuilder messageBuilder = Client.MessageBuilder;
      StartNeighborhoodInitializationRequest startNeighborhoodInitializationRequest = RequestMessage.Request.ConversationRequest.StartNeighborhoodInitialization;
      int primaryPort = (int)startNeighborhoodInitializationRequest.PrimaryPort;
      int srNeighborPort = (int)startNeighborhoodInitializationRequest.SrNeighborPort;
      byte[] followerId = Client.IdentityId;

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      
      bool success = false;

      NeighborhoodInitializationProcessContext nipContext = null;

      bool primaryPortValid = (0 < primaryPort) && (primaryPort <= 65535);
      bool srNeighborPortValid = (0 < srNeighborPort) && (srNeighborPort <= 65535);
      if (primaryPortValid && srNeighborPortValid)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          int blockActionId = await InstallInitializationProcessInProgress(unitOfWork, followerId);
          if (blockActionId != -1)
          {
            DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.HostedIdentityLock, UnitOfWork.FollowerLock };
            using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
            {
              try
              {
                int followerCount = await unitOfWork.FollowerRepository.CountAsync();
                if (followerCount < Base.Configuration.MaxFollowerServersCount)
                {
                  int neighborhoodInitializationsInProgress = await unitOfWork.FollowerRepository.CountAsync(f => f.LastRefreshTime == null);
                  if (neighborhoodInitializationsInProgress < Base.Configuration.NeighborhoodInitializationParallelism)
                  {
                    Follower existingFollower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == followerId)).FirstOrDefault();
                    if (existingFollower == null)
                    {
                      // Take snapshot of all our identities.
                      byte[] invalidVersion = SemVer.Invalid.ToByteArray();
                      List<HostedIdentity> allHostedIdentities = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.ExpirationDate == null) && (i.Version != invalidVersion), null, true)).ToList();

                      // Create new follower.
                      Follower follower = new Follower()
                      {
                        FollowerId = followerId,
                        IpAddress = Client.RemoteEndPoint.Address.ToString(),
                        PrimaryPort = primaryPort,
                        SrNeighborPort = srNeighborPort,
                        LastRefreshTime = null
                      };

                      await unitOfWork.FollowerRepository.InsertAsync(follower);
                      await unitOfWork.SaveThrowAsync();
                      transaction.Commit();
                      success = true;

                      // Set the client to be in the middle of neighbor initialization process.
                      Client.NeighborhoodInitializationProcessInProgress = true;
                      nipContext = new NeighborhoodInitializationProcessContext()
                      {
                        HostedIdentities = allHostedIdentities,
                        IdentitiesDone = 0
                      };
                    }
                    else
                    {
                      log.Warn("Follower ID '{0}' already exists in the database.", followerId.ToHex());
                      res = messageBuilder.CreateErrorAlreadyExistsResponse(RequestMessage);
                    }
                  }
                  else
                  {
                    log.Warn("Maximal number of neighborhood initialization processes {0} in progress has been reached.", Base.Configuration.NeighborhoodInitializationParallelism);
                    res = messageBuilder.CreateErrorBusyResponse(RequestMessage);
                  }
                }
                else
                {
                  log.Warn("Maximal number of follower servers {0} has been reached already. Will not accept another follower.", Base.Configuration.MaxFollowerServersCount);
                  res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
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

              unitOfWork.ReleaseLock(lockObjects);
            }

            if (!success)
            {
              // It may happen that due to power failure, this will not get executed but when the server runs next time, 
              // Data.Database.DeleteInvalidNeighborhoodActions will be executed during the startup and will delete the blocking action.
              if (!await UninstallInitializationProcessInProgress(unitOfWork, blockActionId))
                log.Error("Unable to uninstall blocking neighborhood action ID {0} for follower ID '{1}'.", blockActionId, followerId.ToHex());
            }
          }
          else log.Error("Unable to install blocking neighborhood action for follower ID '{0}'.", followerId.ToHex());
        }
      }
      else
      {
        if (primaryPortValid) res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "srNeighborPort");
        else res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "primaryPort");
      }

      if (success)
      {
        log.Info("New follower ID '{0}' added to the database.", followerId.ToHex());

        Message responseMessage = messageBuilder.CreateStartNeighborhoodInitializationResponse(RequestMessage);
        if (await Client.SendMessageAsync(responseMessage))
        {
          if (nipContext.HostedIdentities.Count > 0)
          {
            log.Trace("Sending first batch of our {0} hosted identities.", nipContext.HostedIdentities.Count);
            Message updateMessage = await BuildNeighborhoodSharedProfileUpdateRequest(Client, nipContext);
            if (!await Client.SendMessageAndSaveUnfinishedRequestAsync(updateMessage, nipContext))
            {
              log.Warn("Unable to send first update message to the client.");
              Client.ForceDisconnect = true;
            }
          }
          else
          {
            log.Trace("No hosted identities to be shared, finishing neighborhood initialization process.");

            // If the profile server hosts no identities, simply finish initialization process.
            Message finishMessage = messageBuilder.CreateFinishNeighborhoodInitializationRequest();
            if (!await Client.SendMessageAndSaveUnfinishedRequestAsync(finishMessage, null))
            {
              log.Warn("Unable to send finish message to the client.");
              Client.ForceDisconnect = true;
            }
          }
        }
        else
        {
          log.Warn("Unable to send reponse message to the client.");
          Client.ForceDisconnect = true;
        }

        res = null;
      }

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      else log.Trace("(-):null");
      return res;
    }
    


    /// <summary>
    /// Processes FinishNeighborhoodInitializationRequest message from client.
    /// <para>This message should never come here from a protocol conforming client,
    /// hence the only thing we can do is return an error.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageFinishNeighborhoodInitializationRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      MessageBuilder messageBuilder = Client.MessageBuilder;
      res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }*/



    /// <summary>
    /// Processes NeighborhoodSharedProfileUpdateRequest message from client.
    /// <para>Processes a shared profile update from a neighbor.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageNeighborhoodSharedProfileUpdateRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      MessageBuilder messageBuilder = Client.MessageBuilder;
      Message res = messageBuilder.CreateErrorBadRoleResponse(RequestMessage);
      if (Client.ServerRole != ServerRole.ServerNeighbor)
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      // Simply send OK to the neighbor server.
      res = messageBuilder.CreateNeighborhoodSharedProfileUpdateResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /*
    /// <summary>
    /// Processes StopNeighborhoodUpdatesRequest message from client.
    /// <para>Removes follower server from the database and also removes all pending actions to the follower.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageStopNeighborhoodUpdatesRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      MessageBuilder messageBuilder = Client.MessageBuilder;
      StopNeighborhoodUpdatesRequest stopNeighborhoodUpdatesRequest = RequestMessage.Request.ConversationRequest.StopNeighborhoodUpdates;
      
      
      byte[] followerId = Client.IdentityId;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        Status status = await unitOfWork.FollowerRepository.DeleteFollower(unitOfWork, followerId);

        if (status == Status.Ok) res = messageBuilder.CreateStopNeighborhoodUpdatesResponse(RequestMessage);
        else if (status == Status.ErrorNotFound) res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
        else res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      }
      

    log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }*/

      /*
    /// <summary>
    /// Processes NeighborhoodSharedProfileUpdateResponse message from client.
    /// <para>This response is received when the follower server accepted a batch of profiles and is ready to receive next batch.</para>
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="Request">Unfinished request message that corresponds to the response message.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageNeighborhoodSharedProfileUpdateResponseAsync(IncomingClient Client, Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;
      
      MessageBuilder messageBuilder = Client.MessageBuilder;
      if (Client.NeighborhoodInitializationProcessInProgress)
      {
        if (ResponseMessage.Response.Status == Status.Ok)
        {
          NeighborhoodInitializationProcessContext nipContext = (NeighborhoodInitializationProcessContext)Request.Context;
          if (nipContext.IdentitiesDone < nipContext.HostedIdentities.Count)
          {
            Message updateMessage = await BuildNeighborhoodSharedProfileUpdateRequest(Client, nipContext);
            if (await Client.SendMessageAndSaveUnfinishedRequestAsync(updateMessage, nipContext))
            {
              res = true;
            }
            else log.Warn("Unable to send update message to the client.");
          }
          else
          {
            // If all hosted identities were sent, finish initialization process.
            Message finishMessage = messageBuilder.CreateFinishNeighborhoodInitializationRequest();
            if (await Client.SendMessageAndSaveUnfinishedRequestAsync(finishMessage, null))
            {
              res = true;
            }
            else log.Warn("Unable to send finish message to the client.");
          }
        }
        else
        {
          // We are in the middle of the neighborhood initialization process, but the follower did not accepted our message with profiles.
          // We should disconnect from the follower and delete it from our follower database.
          // If it wants to retry later, it can.
          // Follower will be deleted automatically as the connection terminates in IncomingClient.HandleDisconnect.
          log.Warn("Client ID '{0}' is follower in the middle of the neighborhood initialization process, but it did not accept our profiles (error code {1}), so we will disconnect it and delete it from our database.", Client.IdentityId.ToHex(), ResponseMessage.Response.Status);
        }
      }
      else log.Error("Client ID '{0}' does not have neighborhood initialization process in progress, client will be disconnected.", Client.IdentityId.ToHex());
      
      log.Trace("(-):{0}", res);
      return res;
    }*/


    /// <summary>
    /// Processes FinishNeighborhoodInitializationResponse message from client.
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="Request">Unfinished request message that corresponds to the response message.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public bool ProcessMessageFinishNeighborhoodInitializationResponseAsync(IncomingClient Client, Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = true;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
