using Google.Protobuf;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using ProfileServer.Data.Repositories;
using ProfileServer.Kernel;
using ProfileServer.Utils;
using ProfileServerCrypto;
using ProfileServerProtocol;
using Iop.Profileserver;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ProfileServer.Network.CAN;

namespace ProfileServer.Network
{
  /// <summary>
  /// Implements the logic behind processing incoming messages to the profile server.
  /// </summary>
  public class MessageProcessor
  {
    private PrefixLogger log;

    /// <summary>Prefix used</summary>
    private string logPrefix;

    /// <summary>Parent role server.</summary>
    private TcpRoleServer roleServer;

    /// <summary>Pointer to the Network.Server component.</summary>
    private Server serverComponent;

    /// <summary>List of server's network peers and clients owned by Network.Server component.</summary>
    public IncomingClientList clientList;

    /// <summary>
    /// Creates a new instance connected to the parent role server.
    /// </summary>
    /// <param name="RoleServer">Parent role server.</param>
    /// <param name="LogPrefix">Log prefix of the parent role server.</param>
    public MessageProcessor(TcpRoleServer RoleServer, string LogPrefix)
    {
      roleServer = RoleServer;
      logPrefix = LogPrefix;
      log = new PrefixLogger("ProfileServer.Network.MessageProcessor", logPrefix);
      serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
      clientList = serverComponent.GetClientList();
    }



    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client who send the message.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(IncomingClient Client, Message IncomingMessage)
    {
      PrefixLogger log = new PrefixLogger("ProfileServer.Network.MessageProcessor", logPrefix);

      MessageBuilder messageBuilder = Client.MessageBuilder;

      bool res = false;
      log.Debug("()");
      try
      {
        // Update time until this client's connection is considered inactive.
        Client.NextKeepAliveTime = DateTime.UtcNow.AddSeconds(Client.KeepAliveIntervalSeconds);
        log.Trace("Client ID {0} NextKeepAliveTime updated to {1}.", Client.Id.ToHex(), Client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));

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

                      case SingleRequest.RequestTypeOneofCase.GetIdentityInformation:
                        responseMessage = await ProcessMessageGetIdentityInformationRequestAsync(Client, IncomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ApplicationServiceSendMessage:
                        responseMessage = await ProcessMessageApplicationServiceSendMessageRequestAsync(Client, IncomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ProfileStats:
                        responseMessage = await ProcessMessageProfileStatsRequestAsync(Client, IncomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.GetIdentityRelationshipsInformation:
                        responseMessage = await ProcessMessageGetIdentityRelationshipsInformationRequestAsync(Client, IncomingMessage);
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
                    if (conversationRequest.Signature.Length > 0) log.Trace("Conversation signature is '{0}'.", conversationRequest.Signature.ToByteArray().ToHex());
                    else log.Trace("No signature provided.");

                    switch (conversationRequest.RequestTypeCase)
                    {
                      case ConversationRequest.RequestTypeOneofCase.Start:
                        responseMessage = ProcessMessageStartConversationRequest(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.RegisterHosting:
                        responseMessage = await ProcessMessageRegisterHostingRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CheckIn:
                        responseMessage = await ProcessMessageCheckInRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.VerifyIdentity:
                        responseMessage = ProcessMessageVerifyIdentityRequest(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.UpdateProfile:
                        responseMessage = await ProcessMessageUpdateProfileRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CancelHostingAgreement:
                        responseMessage = await ProcessMessageCancelHostingAgreementRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ApplicationServiceAdd:
                        responseMessage = ProcessMessageApplicationServiceAddRequest(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ApplicationServiceRemove:
                        responseMessage = ProcessMessageApplicationServiceRemoveRequest(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CallIdentityApplicationService:
                        responseMessage = await ProcessMessageCallIdentityApplicationServiceRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ProfileSearch:
                        responseMessage = await ProcessMessageProfileSearchRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ProfileSearchPart:
                        responseMessage = ProcessMessageProfileSearchPartRequest(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.AddRelatedIdentity:
                        responseMessage = await ProcessMessageAddRelatedIdentityRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.RemoveRelatedIdentity:
                        responseMessage = await ProcessMessageRemoveRelatedIdentityRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization:
                        responseMessage = await ProcessMessageStartNeighborhoodInitializationRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                        responseMessage = ProcessMessageFinishNeighborhoodInitializationRequest(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate:
                        responseMessage = await ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.StopNeighborhoodUpdates:
                        responseMessage = await ProcessMessageStopNeighborhoodUpdatesRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CanStoreData:
                        responseMessage = await ProcessMessageCanStoreDataRequestAsync(Client, IncomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CanPublishIpnsRecord:
                        responseMessage = await ProcessMessageCanPublishIpnsRecordRequestAsync(Client, IncomingMessage);
                        break;

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
                          case SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification:
                            res = await ProcessMessageApplicationServiceReceiveMessageNotificationResponseAsync(Client, IncomingMessage, unfinishedRequest);
                            break;

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
                          case ConversationRequest.RequestTypeOneofCase.IncomingCallNotification:
                            res = await ProcessMessageIncomingCallNotificationResponseAsync(Client, IncomingMessage, unfinishedRequest);
                            break;

                          case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate:
                            res = await ProcessMessageNeighborhoodSharedProfileUpdateResponseAsync(Client, IncomingMessage, unfinishedRequest);
                            break;

                          case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                            res = await ProcessMessageFinishNeighborhoodInitializationResponseAsync(Client, IncomingMessage, unfinishedRequest);
                            break;

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
                  log.Warn("Message type of the response ID {0} does not match the message type of the request ID {1}, the connection will be closed.", IncomingMessage.Id, unfinishedRequest.RequestMessage.Id);
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
    public bool CheckSessionConditions(IncomingClient Client, Message RequestMessage, ServerRole? RequiredRole, ClientConversationStatus? RequiredConversationStatus, out Message ResponseMessage)
    {
      log.Trace("(RequiredRole:{0},RequiredConversationStatus:{1})", RequiredRole != null ? RequiredRole.ToString() : "null", RequiredConversationStatus != null ? RequiredConversationStatus.Value.ToString() : "null");

      bool res = false;
      ResponseMessage = null;

      string requestName = RequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest ? "single request " + RequestMessage.Request.SingleRequest.RequestTypeCase.ToString() : "conversation request " + RequestMessage.Request.ConversationRequest.RequestTypeCase.ToString();

      // RequiredRole contains one or more roles and the current server has to have at least one of them.
      if ((RequiredRole == null) || ((roleServer.Roles & RequiredRole.Value) != 0))
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


            case ClientConversationStatus.ConversationAny:
              res = (Client.ConversationStatus == ClientConversationStatus.ConversationStarted)
                || (Client.ConversationStatus == ClientConversationStatus.Verified)
                || (Client.ConversationStatus == ClientConversationStatus.Authenticated);

              if (!res)
              {
                log.Warn("Client sent {0} but the conversation status is {1}.", requestName, Client.ConversationStatus);
                ResponseMessage = Client.MessageBuilder.CreateErrorBadConversationStatusResponse(RequestMessage);
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
        log.Warn("Received {0} on server without {1} role(s) (server roles are {2}).", requestName, RequiredRole.Value, roleServer.Roles);
        ResponseMessage = Client.MessageBuilder.CreateErrorBadRoleResponse(RequestMessage);
      }

      log.Trace("(-):{0}", res);
      return res;
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

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Primary, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
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
              case ServerRole.Primary: srt = ServerRoleType.Primary; break;
              case ServerRole.ServerNeighbor: srt = ServerRoleType.SrNeighbor; break;
              case ServerRole.ClientNonCustomer: srt = ServerRoleType.ClNonCustomer; break;
              case ServerRole.ClientCustomer: srt = ServerRoleType.ClCustomer; break;
              case ServerRole.ClientAppService: srt = ServerRoleType.ClAppService; break;
              default:
                skip = true;
                break;
            }

            if (!skip)
            {
              Iop.Profileserver.ServerRole item = new Iop.Profileserver.ServerRole()
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
    /// Processes GetIdentityInformationRequest message from client.
    /// <para>Obtains information about identity that is hosted by the profile server.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageGetIdentityInformationRequestAsync(IncomingClient Client, Message RequestMessage)
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
      if (identityId.Length == IdentityBase.IdentifierLength)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.IdentityId == identityId)).FirstOrDefault();
          if (identity != null)
          {
            if (identity.IsProfileInitialized())
            {
              bool isHosted = identity.ExpirationDate == null;
              if (isHosted)
              {
                IncomingClient targetClient = clientList.GetCheckedInClient(identityId);
                bool isOnline = targetClient != null;
                byte[] publicKey = identity.PublicKey;
                SemVer version = new SemVer(identity.Version);
                string type = identity.Type;
                string name = identity.Name;
                GpsLocation location = identity.GetInitialLocation();
                string extraData = identity.ExtraData;

                byte[] profileImage = null;
                byte[] thumbnailImage = null;
                HashSet<string> applicationServices = null;

                if (getIdentityInformationRequest.IncludeProfileImage)
                  profileImage = await identity.GetProfileImageDataAsync();

                if (getIdentityInformationRequest.IncludeThumbnailImage)
                  thumbnailImage = await identity.GetThumbnailImageDataAsync();

                if (getIdentityInformationRequest.IncludeApplicationServices)
                  applicationServices = targetClient.ApplicationServices.GetServices();

                res = messageBuilder.CreateGetIdentityInformationResponse(RequestMessage, isHosted, null, version, isOnline, publicKey, name, type, location, extraData, profileImage, thumbnailImage, applicationServices);
              }
              else
              {
                byte[] targetHostingServer = identity.HostingServerId;
                res = messageBuilder.CreateGetIdentityInformationResponse(RequestMessage, isHosted, targetHostingServer, null);
              }
            }
            else
            {
              log.Trace("Identity ID '{0}' profile not initialized.", identityId.ToHex());
              res = messageBuilder.CreateErrorUninitializedResponse(RequestMessage);
            }
          }
          else
          {
            log.Trace("Identity ID '{0}' is not hosted by this profile server.", identityId.ToHex());
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
    public Message ProcessMessageStartConversationRequest(IncomingClient Client, Message RequestMessage)
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
      byte[] clientChallenge = startConversationRequest.ClientChallenge.ToByteArray();
      byte[] pubKey = startConversationRequest.PublicKey.ToByteArray();

      if (clientChallenge.Length == ProtocolHelper.ChallengeDataSize)
      {
        if ((0 < pubKey.Length) && (pubKey.Length <= IdentityBase.MaxPublicKeyLengthBytes))
        {
          SemVer version;
          if (GetCommonSupportedVersion(startConversationRequest.SupportedVersions, out version))
          {
            Client.PublicKey = pubKey;
            Client.IdentityId = Crypto.Sha256(Client.PublicKey);

            if (clientList.AddNetworkPeerWithIdentity(Client))
            {
              Client.MessageBuilder.SetProtocolVersion(version);

              byte[] challenge = new byte[ProtocolHelper.ChallengeDataSize];
              Crypto.Rng.GetBytes(challenge);
              Client.AuthenticationChallenge = challenge;
              Client.ConversationStatus = ClientConversationStatus.ConversationStarted;

              log.Debug("Client {0} conversation status updated to {1}, selected version is '{2}', client public key set to '{3}', client identity ID set to '{4}', challenge set to '{5}'.",
                Client.RemoteEndPoint, Client.ConversationStatus, version, Client.PublicKey.ToHex(), Client.IdentityId.ToHex(), Client.AuthenticationChallenge.ToHex());

              res = messageBuilder.CreateStartConversationResponse(RequestMessage, version, Base.Configuration.Keys.PublicKey, Client.AuthenticationChallenge, clientChallenge);
            }
            else res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
          }
          else
          {
            log.Warn("Client and server are incompatible in protocol versions.");
            res = messageBuilder.CreateErrorUnsupportedResponse(RequestMessage);
          }
        }
        else
        {
          log.Warn("Client send public key of invalid length of {0} bytes.", pubKey.Length);
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "publicKey");
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
    /// Processes RegisterHostingRequest message from client.
    /// <para>Registers a new customer client identity. The identity must not be hosted by the profile server already 
    /// and the profile server must not have reached the maximal number of hosted clients. The newly created profile 
    /// is empty and has to be initialized by the identity using UpdateProfileRequest.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageRegisterHostingRequestAsync(IncomingClient Client, Message RequestMessage)
    {
#warning TODO: This function is currently implemented to support empty contracts or contracts with just identity type.
      // TODO: CHECK CONTRACT:
      // * signature is valid 
      // * planId is valid
      // * startTime is per specification
      // * identityPublicKey is client's key 
      // * identityType is valid
      // * contract.IdentityType is not longer than 64 bytes and it does not contain '*'
      log.Trace("()");
      log.Fatal("TODO UNIMPLEMENTED");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      RegisterHostingRequest registerHostingRequest = RequestMessage.Request.ConversationRequest.RegisterHosting;
      HostingPlanContract contract = registerHostingRequest.Contract;
      string identityType = contract != null ? contract.IdentityType : "<new>";


      bool success = false;
      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.HostedIdentityLock;
        using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObject))
        {
          try
          {
            // We need to recheck the number of hosted identities within the transaction.
            int hostedIdentities = await unitOfWork.HostedIdentityRepository.CountAsync(null);
            log.Trace("Currently hosting {0} clients.", hostedIdentities);
            if (hostedIdentities < Base.Configuration.MaxHostedIdentities)
            {
              HostedIdentity existingIdentity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.IdentityId == Client.IdentityId)).FirstOrDefault();
              // Identity does not exist at all, or it has been cancelled so that ExpirationDate was set.
              if ((existingIdentity == null) || (existingIdentity.ExpirationDate != null))
              {
                // We do not have the identity in our client's database,
                // OR we do have the identity in our client's database, but it's contract has been cancelled.
                if (existingIdentity != null)
                  log.Debug("Identity ID '{0}' is already a client of this profile server, but its contract has been cancelled.", Client.IdentityId.ToHex());

                HostedIdentity identity = existingIdentity == null ? new HostedIdentity() : existingIdentity;

                // We can't change primary identifier in existing entity.
                if (existingIdentity == null) identity.IdentityId = Client.IdentityId;

                identity.HostingServerId = new byte[0];
                identity.PublicKey = Client.PublicKey;
                identity.Version = SemVer.Invalid.ToByteArray();
                identity.Name = "";
                identity.Type = identityType;
                // Existing cancelled identity profile does not have images, no need to delete anything at this point.
                identity.ProfileImage = null;
                identity.ThumbnailImage = null;
                identity.InitialLocationLatitude = GpsLocation.NoLocation.Latitude;
                identity.InitialLocationLongitude = GpsLocation.NoLocation.Longitude;
                identity.ExtraData = "";
                identity.ExpirationDate = null;

                if (existingIdentity == null) await unitOfWork.HostedIdentityRepository.InsertAsync(identity);
                else unitOfWork.HostedIdentityRepository.Update(identity);

                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
                success = true;
              }
              else
              {
                // We have the identity in our client's database with an active contract.
                log.Debug("Identity ID '{0}' is already a client of this profile server.", Client.IdentityId.ToHex());
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
        log.Debug("Identity '{0}' added to database.", Client.IdentityId.ToHex());
        res = messageBuilder.CreateRegisterHostingResponse(RequestMessage, contract);
      }


      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }




    /// <summary>
    /// Processes CheckInRequest message from client.
    /// <para>It verifies the identity's public key against the signature of the challenge provided during the start of the conversation. 
    /// The identity must be hosted on this profile server. If everything is OK, the identity is checked-in and the status of the conversation
    /// is upgraded to Authenticated.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageCheckInRequestAsync(IncomingClient Client, Message RequestMessage)
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
      if (StructuralEqualityComparer<byte[]>.Default.Equals(challenge, Client.AuthenticationChallenge))
      {
        if (messageBuilder.VerifySignedConversationRequestBody(RequestMessage, checkInRequest, Client.PublicKey))
        {
          log.Debug("Identity '{0}' is about to check in ...", Client.IdentityId.ToHex());

          bool success = false;
          res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
          using (UnitOfWork unitOfWork = new UnitOfWork())
          {
            try
            {
              HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
              if (identity != null)
              {
                if (await clientList.AddCheckedInClient(Client))
                {
                  Client.ConversationStatus = ClientConversationStatus.Authenticated;

                  success = true;
                }
                else log.Error("Identity '{0}' failed to check-in.", Client.IdentityId.ToHex());
              }
              else
              {
                log.Debug("Identity '{0}' is not a client of this profile server.", Client.IdentityId.ToHex());
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
            log.Debug("Identity '{0}' successfully checked in ...", Client.IdentityId.ToHex());
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
        log.Warn("Challenge provided in the request does not match the challenge created by the profile server.");
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
    public Message ProcessMessageVerifyIdentityRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer | ServerRole.ServerNeighbor, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      MessageBuilder messageBuilder = Client.MessageBuilder;
      VerifyIdentityRequest verifyIdentityRequest = RequestMessage.Request.ConversationRequest.VerifyIdentity;

      byte[] challenge = verifyIdentityRequest.Challenge.ToByteArray();
      if (StructuralEqualityComparer<byte[]>.Default.Equals(challenge, Client.AuthenticationChallenge))
      {
        if (messageBuilder.VerifySignedConversationRequestBody(RequestMessage, verifyIdentityRequest, Client.PublicKey))
        {
          log.Debug("Identity '{0}' successfully verified its public key.", Client.IdentityId.ToHex());
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
        log.Warn("Challenge provided in the request does not match the challenge created by the profile server.");
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
    /// this process just before old files are to be deleted, they will remain on the disk 
    /// without any reference from the database, thus possibly creates a resource leak.
    /// This is solved by cleanup routines during the profile server start - see 
    /// ProfileServer.Data.ImageManager.DeleteUnusedImages.</remarks>
    public async Task<Message> ProcessMessageUpdateProfileRequestAsync(IncomingClient Client, Message RequestMessage)
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
          HostedIdentity identityForValidation = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null), null, true)).FirstOrDefault();
          if (identityForValidation != null)
          {
            Message errorResponse;
            if (ValidateUpdateProfileRequest(identityForValidation, updateProfileRequest, messageBuilder, RequestMessage, out errorResponse))
            {
              // If an identity has a profile image and a thumbnail image, they are saved on the disk.
              // If we are replacing those images, we have to create new files and delete the old files.
              // First, we create the new files and then in DB transaction, we get information about 
              // whether to delete existing files and which ones.
              byte[] profileImageToDelete = null;
              byte[] thumbnailImageToDelete = null;

              if (updateProfileRequest.SetImage)
              {
                byte[] profileImage = updateProfileRequest.Image.ToByteArray();
                if (profileImage.Length > 0)
                {
                  byte[] thumbnailImage;
                  ImageManager.ProfileImageToThumbnailImage(profileImage, out thumbnailImage);

                  identityForValidation.ProfileImage = Crypto.Sha256(profileImage);
                  identityForValidation.ThumbnailImage = Crypto.Sha256(thumbnailImage);

                  await identityForValidation.SaveProfileImageDataAsync(profileImage);
                  await identityForValidation.SaveThumbnailImageDataAsync(thumbnailImage);
                }
                else
                {
                  // Erase image.
                  identityForValidation.ProfileImage = null;
                  identityForValidation.ThumbnailImage = null;
                }
              }

              bool signalNeighborhoodAction = false;

              // Update database record.
              DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.HostedIdentityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
              using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
              {
                try
                {
                  HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();

                  if (identity != null)
                  {
                    bool isProfileInitialization = !identity.IsProfileInitialized();

                    if (updateProfileRequest.SetVersion)
                      identity.Version = updateProfileRequest.Version.ToByteArray();

                    if (updateProfileRequest.SetName)
                      identity.Name = updateProfileRequest.Name;

                    if (updateProfileRequest.SetImage)
                    {
                      // Here we replace existing images with new ones
                      // and we save the old images hashes so we can delete them later.
                      profileImageToDelete = identity.ProfileImage;
                      thumbnailImageToDelete = identity.ThumbnailImage;

                      identity.ProfileImage = identityForValidation.ProfileImage;
                      identity.ThumbnailImage = identityForValidation.ThumbnailImage;
                    }

                    if (updateProfileRequest.SetLocation)
                    {
                      GpsLocation gpsLocation = new GpsLocation(updateProfileRequest.Latitude, updateProfileRequest.Longitude);
                      identity.SetInitialLocation(gpsLocation);
                    }

                    if (updateProfileRequest.SetExtraData)
                      identity.ExtraData = updateProfileRequest.ExtraData;

                    unitOfWork.HostedIdentityRepository.Update(identity);


                    // The profile change has to be propagated to all our followers
                    // we create database actions that will be processed by dedicated thread.
                    NeighborhoodActionType actionType = isProfileInitialization ? NeighborhoodActionType.AddProfile : NeighborhoodActionType.ChangeProfile;
                    string extraInfo = null;
                    if (actionType == NeighborhoodActionType.ChangeProfile)
                    {
                      SharedProfileChangeItem changeItem = new SharedProfileChangeItem()
                      {
                        SetVersion = updateProfileRequest.SetVersion,
                        SetName = updateProfileRequest.SetName,
                        SetThumbnailImage = updateProfileRequest.SetImage,
                        SetLocation = updateProfileRequest.SetLocation,
                        SetExtraData = updateProfileRequest.SetExtraData
                      };
                      extraInfo = changeItem.ToString();
                    }
                    signalNeighborhoodAction = await AddIdentityProfileFollowerActions(unitOfWork, actionType, identity.IdentityId, extraInfo);

                    await unitOfWork.SaveThrowAsync();
                    transaction.Commit();
                    success = true;
                  }
                  else
                  {
                    log.Debug("Identity '{0}' is not a client of this profile server.", Client.IdentityId.ToHex());
                    res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
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

              if (success)
              {
                log.Debug("Identity '{0}' updated its profile in the database.", Client.IdentityId.ToHex());
                res = messageBuilder.CreateUpdateProfileResponse(RequestMessage);

                // Send signal to neighborhood action processor to process the new series of actions.
                if (signalNeighborhoodAction)
                {
                  NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary["Network.NeighborhoodActionProcessor"];
                  neighborhoodActionProcessor.Signal();
                }

                // Delete old files, if there are any.
                ImageManager imageManager = (ImageManager)Base.ComponentDictionary["Data.ImageManager"];
                if (profileImageToDelete != null) imageManager.RemoveImageReference(profileImageToDelete);
                if (thumbnailImageToDelete != null) imageManager.RemoveImageReference(thumbnailImageToDelete);
              }
            }
            else res = errorResponse;
          }
          else
          {
            log.Debug("Identity '{0}' is not a client of this profile server.", Client.IdentityId.ToHex());
            res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Checks whether the update profile request is valid.
    /// </summary>
    /// <param name="Identity">Identity on which the update operation is about to be performed.</param>
    /// <param name="UpdateProfileRequest">Update profile request part of the client's request message.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the profile update request can be applied, false otherwise.</returns>
    private bool ValidateUpdateProfileRequest(HostedIdentity Identity, UpdateProfileRequest UpdateProfileRequest, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("(Identity.IdentityId:'{0}')", Identity.IdentityId.ToHex());

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
          SemVer version = new SemVer(UpdateProfileRequest.Version);

          // Currently only supported version is 1.0.0.
          if (!version.Equals(SemVer.V100))
          {
            log.Debug("Unsupported version '{0}'.", version);
            details = "version";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetName)
        {
          string name = UpdateProfileRequest.Name;

          // Name is non-empty string, max Identity.MaxProfileNameLengthBytes bytes long.
          if (string.IsNullOrEmpty(name) || (Encoding.UTF8.GetByteCount(name) > IdentityBase.MaxProfileNameLengthBytes))
          {
            log.Debug("Invalid name '{0}'.", name);
            details = "name";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetImage)
        {
          byte[] image = UpdateProfileRequest.Image.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxProfileImageLengthBytes.
          bool eraseImage = image.Length == 0;
          bool imageValid = (image.Length <= HostedIdentity.MaxProfileImageLengthBytes) && (eraseImage || ImageManager.ValidateImageFormat(image));
          if (!imageValid)
          {
            log.Debug("Invalid image.");
            details = "image";
          }
        }


        if ((details == null) && UpdateProfileRequest.SetLocation)
        {
          GpsLocation locLat = new GpsLocation(UpdateProfileRequest.Latitude, 0);
          GpsLocation locLong = new GpsLocation(0, UpdateProfileRequest.Longitude);
          if (!locLat.IsValid())
          {
            log.Debug("Latitude '{0}' is not a valid GPS latitude value.", UpdateProfileRequest.Latitude);
            details = "latitude";
          }
          else if (!locLong.IsValid())
          {
            log.Debug("Longitude '{0}' is not a valid GPS longitude value.", UpdateProfileRequest.Longitude);
            details = "longitude";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetExtraData)
        {
          string extraData = UpdateProfileRequest.ExtraData;
          if (extraData == null) extraData = "";

          // Extra data is semicolon separated 'key=value' list, max Identity.MaxProfileExtraDataLengthBytes bytes long.
          if (Encoding.UTF8.GetByteCount(extraData) > IdentityBase.MaxProfileExtraDataLengthBytes)
          {
            log.Debug("Extra data too large ({0} bytes, limit is {1}).", Encoding.UTF8.GetByteCount(extraData), IdentityBase.MaxProfileExtraDataLengthBytes);
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
    /// Processes CancelHostingAgreementRequest message from client.
    /// <para>Cancels a hosting agreement with an identity.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    /// <remarks>Cancelling hosting agreement causes identity's image files to be deleted. 
    /// The profile itself is not immediately deleted, but its expiration date is set, 
    /// which will lead to its deletion. If the hosting server redirection is installed, 
    /// the expiration date is set to a later time.</remarks>
    public async Task<Message> ProcessMessageCancelHostingAgreementRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      CancelHostingAgreementRequest cancelHostingAgreementRequest = RequestMessage.Request.ConversationRequest.CancelHostingAgreement;

      if (!cancelHostingAgreementRequest.RedirectToNewProfileServer || (cancelHostingAgreementRequest.NewProfileServerNetworkId.Length == IdentityBase.IdentifierLength))
      {
        byte[] profileImageToDelete = null;
        byte[] thumbnailImageToDelete = null;

        bool success = false;
        res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          bool signalNeighborhoodAction = false;

          DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.HostedIdentityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
          using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
          {
            try
            {
              HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
              if (identity != null)
              {
                // We artificially initialize the profile when we cancel it in order to allow queries towards this profile.
                if (!identity.IsProfileInitialized())
                  identity.Version = SemVer.V100.ToByteArray();

                // We are going to delete the images, so we have to make sure, the identity in database does not reference it anymore.
                profileImageToDelete = identity.ProfileImage;
                thumbnailImageToDelete = identity.ThumbnailImage;

                identity.ProfileImage = null;
                identity.ThumbnailImage = null;

                if (cancelHostingAgreementRequest.RedirectToNewProfileServer)
                {
                  // The customer cancelled the contract, but left a redirect, which we will maintain for 14 days.
                  identity.ExpirationDate = DateTime.UtcNow.AddDays(14);
                  identity.HostingServerId = cancelHostingAgreementRequest.NewProfileServerNetworkId.ToByteArray();
                }
                else
                {
                  // The customer cancelled the contract, no redirect is being maintained, we can delete the record at any time.
                  identity.ExpirationDate = DateTime.UtcNow;
                }

                unitOfWork.HostedIdentityRepository.Update(identity);

                // The profile change has to be propagated to all our followers
                // we create database actions that will be processed by dedicated thread.
                signalNeighborhoodAction = await AddIdentityProfileFollowerActions(unitOfWork, NeighborhoodActionType.RemoveProfile, identity.IdentityId);

                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
                success = true;
              }
              else
              {
                log.Debug("Identity '{0}' is not a client of this profile server.", Client.IdentityId.ToHex());
                res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
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

          if (success)
          {
            if (cancelHostingAgreementRequest.RedirectToNewProfileServer) log.Debug("Identity '{0}' hosting agreement cancelled and redirection set to profile server ID '{1}'.", Client.IdentityId.ToHex(), cancelHostingAgreementRequest.NewProfileServerNetworkId.ToByteArray().ToHex());
            else log.Debug("Identity '{0}' hosting agreement cancelled and no redirection set.", Client.IdentityId.ToHex());

            res = messageBuilder.CreateCancelHostingAgreementResponse(RequestMessage);

            // Send signal to neighborhood action processor to process the new series of actions.
            if (signalNeighborhoodAction)
            {
              NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary["Network.NeighborhoodActionProcessor"];
              neighborhoodActionProcessor.Signal();
            }

            // Delete old files, if there are any.
            ImageManager imageManager = (ImageManager)Base.ComponentDictionary["Data.ImageManager"];
            if (profileImageToDelete != null) imageManager.RemoveImageReference(profileImageToDelete);
            if (thumbnailImageToDelete != null) imageManager.RemoveImageReference(thumbnailImageToDelete);
          }
        }
      }
      else
      {
        log.Debug("Invalid profile server identifier '{0}'.", cancelHostingAgreementRequest.NewProfileServerNetworkId.ToByteArray().ToHex());
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "newProfileServerNetworkId");
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
    public Message ProcessMessageApplicationServiceAddRequest(IncomingClient Client, Message RequestMessage)
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
        if (string.IsNullOrEmpty(serviceName) || (Encoding.UTF8.GetByteCount(serviceName) > IncomingClient.MaxApplicationServiceNameLengthBytes))
        {
          log.Warn("Invalid service name '{0}'.", serviceName);
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, string.Format("serviceNames[{0}]", i));
          break;
        }
      }

      if (res == null)
      {
        if (Client.ApplicationServices.AddServices(applicationServiceAddRequest.ServiceNames))
        {
          log.Debug("Service names added to identity '{0}': {1}", Client.IdentityId.ToHex(), string.Join(", ", applicationServiceAddRequest.ServiceNames));
          res = messageBuilder.CreateApplicationServiceAddResponse(RequestMessage);
        }
        else
        {
          log.Debug("Identity '{0}' application services list not changed, number of services would exceed the limit {1}.", Client.IdentityId.ToHex(), IncomingClient.MaxClientApplicationServices);
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
    public Message ProcessMessageApplicationServiceRemoveRequest(IncomingClient Client, Message RequestMessage)
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
      if (Client.ApplicationServices.RemoveService(serviceName))
      {
        res = messageBuilder.CreateApplicationServiceRemoveResponse(RequestMessage);
        log.Debug("Service name '{0}' removed from identity '{1}'.", serviceName, Client.IdentityId.ToHex());
      }
      else
      {
        log.Warn("Service name '{0}' not found on the list of supported services of identity '{1}'.", serviceName, Client.IdentityId.ToHex());
        res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }




    /// <summary>
    /// Processes CallIdentityApplicationServiceRequest message from client.
    /// <para>This is a request from the caller to contact the callee and inform it about incoming call via one of its application services.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Error response message to be sent to the client, or null if everything goes OK and the callee is going to be informed 
    /// about an incoming call.</returns>
    public async Task<Message> ProcessMessageCallIdentityApplicationServiceRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      CallIdentityApplicationServiceRequest callIdentityApplicationServiceRequest = RequestMessage.Request.ConversationRequest.CallIdentityApplicationService;

      byte[] calleeIdentityId = callIdentityApplicationServiceRequest.IdentityNetworkId.ToByteArray();
      string serviceName = callIdentityApplicationServiceRequest.ServiceName;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        try
        {
          HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == calleeIdentityId))).FirstOrDefault();
          if (identity != null)
          {
            if (!identity.IsProfileInitialized())
            {
              log.Debug("Identity ID '{0}' not initialized and can not be called.", calleeIdentityId.ToHex());
              res = messageBuilder.CreateErrorUninitializedResponse(RequestMessage);
            }
          }
          else
          {
            log.Warn("Identity ID '{0}' not found.", calleeIdentityId.ToHex());
            res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "identityNetworkId");
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
          res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        }
      }


      if (res == null)
      {
        IncomingClient callee = clientList.GetCheckedInClient(calleeIdentityId);
        if (callee != null)
        {
          // The callee is hosted on this profile server, it is online and its profile is initialized.
          if (callee.ApplicationServices.ContainsService(serviceName))
          {
            // All OK, create network relay and inform callee.
            RelayConnection relay = clientList.CreateNetworkRelay(Client, callee, serviceName, RequestMessage);
            if (relay != null)
            {
              bool error = false;
              Message notificationMessage = callee.MessageBuilder.CreateIncomingCallNotificationRequest(Client.PublicKey, serviceName, relay.GetCalleeToken().ToByteArray());
              if (await callee.SendMessageAndSaveUnfinishedRequestAsync(notificationMessage, relay))
              {
                // res remains null, which is fine!
                // At this moment, the caller is put on hold and we contact it again once one of the following happens:
                // 1) We lose connection to the callee, in which case we send ERROR_NOT_AVAILABLE to the caller and destroy the relay.
                // 2) We do not receive response from the callee within a reasonable time, in which case we send ERROR_NOT_AVAILABLE to the caller and destroy the relay.
                // 3) We receive a rejection from the callee, in which case we send ERROR_REJECTED to the caller and destroy the relay.
                // 4) We receive an acceptance from the callee, in which case we send CallIdentityApplicationServiceResponse to the caller and continue.
                log.Debug("Incoming call notification request sent to the callee '{0}'.", calleeIdentityId.ToHex());
              }
              else
              {
                log.Debug("Unable to send incoming call notification to the callee '{0}'.", calleeIdentityId.ToHex());
                res = messageBuilder.CreateErrorNotAvailableResponse(RequestMessage);
                error = true;
              }

              if (error) await clientList.DestroyNetworkRelay(relay);
            }
            else
            {
              log.Debug("Token issueing failed, callee '{0}' is probably not available anymore.", calleeIdentityId.ToHex());
              res = messageBuilder.CreateErrorNotAvailableResponse(RequestMessage);
            }
          }
          else
          {
            log.Debug("Callee's identity '{0}' does not have service name '{1}' enabled.", calleeIdentityId.ToHex(), serviceName);
            res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "serviceName");
          }
        }
        else
        {
          log.Debug("Callee's identity '{0}' not found among online clients.", calleeIdentityId.ToHex());
          res = messageBuilder.CreateErrorNotAvailableResponse(RequestMessage);
        }
      }

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      else log.Trace("(-):null");
      return res;
    }



    /// <summary>
    /// Processes IncomingCallNotificationResponse message from client.
    /// <para>This is a callee's reply to the notification about an incoming call that it accepts the call.</para>
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="Request">Unfinished request message that corresponds to the response message.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageIncomingCallNotificationResponseAsync(IncomingClient Client, Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;

      RelayConnection relay = (RelayConnection)Request.Context;
      // Both OK and error responses are handled in CalleeRepliedToIncomingCallNotification.
      res = await relay.CalleeRepliedToIncomingCallNotification(ResponseMessage, Request);

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Processes ApplicationServiceSendMessageRequest message from a client.
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageApplicationServiceSendMessageRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientAppService, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      ApplicationServiceSendMessageRequest applicationServiceSendMessageRequest = RequestMessage.Request.SingleRequest.ApplicationServiceSendMessage;

      byte[] tokenBytes = applicationServiceSendMessageRequest.Token.ToByteArray();
      if (tokenBytes.Length == 16)
      {
        Guid token = new Guid(tokenBytes);
        RelayConnection relay = clientList.GetRelayByGuid(token);
        if (relay != null)
        {
          res = await relay.ProcessIncomingMessage(Client, RequestMessage, token);
        }
        else
        {
          log.Trace("No relay found for token '{0}', closing connection to client.", token);
          res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          Client.ForceDisconnect = true;
        }
      }
      else
      {
        log.Warn("Invalid length of token - {0} bytes, need 16 byte GUID, closing connection to client.", tokenBytes.Length);
        res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
        Client.ForceDisconnect = true;
      }

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Processes ApplicationServiceReceiveMessageNotificationResponse message from client.
    /// <para>This is a recipient's reply to the notification about an incoming message over an open relay.</para>
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="Request">Unfinished request message that corresponds to the response message.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageApplicationServiceReceiveMessageNotificationResponseAsync(IncomingClient Client, Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;

      RelayMessageContext context = (RelayMessageContext)Request.Context;
      // Both OK and error responses are handled in RecipientConfirmedMessage.
      res = await context.Relay.RecipientConfirmedMessage(Client, ResponseMessage, context.SenderRequest);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes ProfileStatsRequest message from client.
    /// <para>Obtains identity profiles statistics from the profile server.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageProfileStatsRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer | ServerRole.ClientCustomer, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      ProfileStatsRequest profileStatsRequest = RequestMessage.Request.SingleRequest.ProfileStats;

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        try
        {
          List<ProfileStatsItem> stats = await unitOfWork.HostedIdentityRepository.GetProfileStatsAsync();
          res = messageBuilder.CreateProfileStatsResponse(RequestMessage, stats);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }




    /// <summary>
    /// Minimal number of results we ask for from database. This limit prevents doing small database queries 
    /// when there are many identities to be explored but very few of them actually match the client's criteria. 
    /// Since not all filtering is done in the SQL query, we need to prevent ourselves from putting to much 
    /// pressure on the database in those edge cases.
    /// </summary>
    public const int ProfileSearchBatchSizeMin = 1000;

    /// <summary>
    /// Multiplication factor to count maximal size of the batch from the number of required results.
    /// Not all filtering is done in the SQL query. This means that in order to get a certain amount of results 
    /// that the client asks for, it is likely that we need to load more records from the database.
    /// This value provides information about how many times more do we load.
    /// </summary>
    public const decimal ProfileSearchBatchSizeMaxFactor = 10.0m;

    /// <summary>Maximum amount of time in milliseconds that a single search query can take.</summary>
    public const int ProfileSearchMaxTimeMs = 15000;

    /// <summary>Maximum amount of time in milliseconds that we want to spend on regular expression matching of extraData.</summary>
    public const int ProfileSearchMaxExtraDataMatchingTimeTotalMs = 1000;

    /// <summary>Maximum amount of time in milliseconds that we want to spend on regular expression matching of extraData of a single profile.</summary>
    public const int ProfileSearchMaxExtraDataMatchingTimeSingleMs = 25;

    /// <summary>Maximum number of results the profile server can send in the response if images are included.</summary>
    public const int ProfileSearchMaxResponseRecordsWithImage = 100;

    /// <summary>Maximum number of results the profile server can send in the response if images are not included.</summary>
    public const int ProfileSearchMaxResponseRecordsWithoutImage = 1000;


    /// <summary>
    /// Processes ProfileSearchRequest message from client.
    /// <para>Performs a search operation to find all matching identities that this profile server hosts, 
    /// possibly including identities hosted in the profile server's neighborhood.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageProfileSearchRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationAny, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      ProfileSearchRequest profileSearchRequest = RequestMessage.Request.ConversationRequest.ProfileSearch;

      Message errorResponse;
      if (ValidateProfileSearchRequest(profileSearchRequest, messageBuilder, RequestMessage, out errorResponse))
      {
        res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          Stopwatch watch = new Stopwatch();
          try
          {
            uint maxResults = profileSearchRequest.MaxTotalRecordCount;
            uint maxResponseResults = profileSearchRequest.MaxResponseRecordCount;
            string typeFilter = profileSearchRequest.Type;
            string nameFilter = profileSearchRequest.Name;
            GpsLocation locationFilter = null;
            if (profileSearchRequest.Latitude != GpsLocation.NoLocationLocationType)
              locationFilter = new GpsLocation(profileSearchRequest.Latitude, profileSearchRequest.Longitude);
            uint radius = profileSearchRequest.Radius;
            string extraDataFilter = profileSearchRequest.ExtraData;
            bool includeImages = profileSearchRequest.IncludeThumbnailImages;

            watch.Start();

            // First, we try to find enough results among identities hosted on this profile server.
            List<IdentityNetworkProfileInformation> searchResultsNeighborhood = new List<IdentityNetworkProfileInformation>();
            List<IdentityNetworkProfileInformation> searchResultsLocal = await ProfileSearch(unitOfWork.HostedIdentityRepository, maxResults, typeFilter, nameFilter, locationFilter, radius, extraDataFilter, includeImages, watch);
            if (searchResultsLocal != null)
            {
              bool localServerOnly = true;
              bool error = false;
              // If possible and needed we try to find more results among identities hosted in this profile server's neighborhood.
              if (!profileSearchRequest.IncludeHostedOnly && (searchResultsLocal.Count < maxResults))
              {
                localServerOnly = false;
                maxResults -= (uint)searchResultsLocal.Count;
                searchResultsNeighborhood = await ProfileSearch(unitOfWork.NeighborIdentityRepository, maxResults, typeFilter, nameFilter, locationFilter, radius, extraDataFilter, includeImages, watch);
                if (searchResultsNeighborhood == null)
                {
                  log.Error("Profile search among neighborhood identities failed.");
                  error = true;
                }
              }

              if (!error)
              {
                // Now we have all required results in searchResultsLocal and searchResultsNeighborhood.
                // If the number of results is small enough to fit into a single response, we send them all.
                // Otherwise, we save them to the session context and only send first part of them.
                List<IdentityNetworkProfileInformation> allResults = searchResultsLocal;
                allResults.AddRange(searchResultsNeighborhood);
                List<IdentityNetworkProfileInformation> responseResults = allResults;
                log.Debug("Total number of matching profiles is {0}, from which {1} are local, {2} are from neighbors.", allResults.Count, searchResultsLocal.Count, searchResultsNeighborhood.Count);
                if (maxResponseResults < allResults.Count)
                {
                  log.Trace("All results can not fit into a single response (max {0} results).", maxResponseResults);
                  // We can not send all results, save them to session.
                  Client.SaveProfileSearchResults(allResults, includeImages);

                  // And send the maximum we can in the response.
                  responseResults = new List<IdentityNetworkProfileInformation>();
                  responseResults.AddRange(allResults.GetRange(0, (int)maxResponseResults));
                }

                List<byte[]> coveredServers = await ProfileSearchGetCoveredServers(unitOfWork, localServerOnly);
                res = messageBuilder.CreateProfileSearchResponse(RequestMessage, (uint)allResults.Count, maxResponseResults, coveredServers, responseResults);
              }
            }
            else log.Error("Profile search among hosted identities failed.");
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }

          watch.Stop();
        }
      } else res = errorResponse;

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Obtains list of covered profile servers for a profile search query.
    /// <para>If the search used the local profile server database only, the result is simply ID of the local profile server. 
    /// Otherwise, it is its ID and a list of all its neighbors' IDs.</para>
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="LocalServerOnly">true if the search query only used the local profile server, false otherwise.</param>
    /// <returns>List of network IDs of profile servers whose database could be used to create the result.</returns>
    /// <remarks>Note that the covered profile server list is not guaranteed to be accurate. The search query processing is not atomic 
    /// and during the process it may happen that a neighbor server can be added or removed from the list of neighbors.</remarks>
    private async Task<List<byte[]>> ProfileSearchGetCoveredServers(UnitOfWork UnitOfWork, bool LocalServerOnly)
    {
      log.Trace("()");

      List<byte[]> res = new List<byte[]>();
      res.Add(serverComponent.ServerId);
      if (!LocalServerOnly)
      {
        List<byte[]> neighborIds = (await UnitOfWork.NeighborRepository.GetAsync(null, null, true)).Select(n => n.NeighborId).ToList();
        res.AddRange(neighborIds);
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }

    /// <summary>
    /// Checks whether the search profile request is valid.
    /// </summary>
    /// <param name="ProfileSearchRequest">Profile search request part of the client's request message.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the profile update request can be applied, false otherwise.</returns>
    private bool ValidateProfileSearchRequest(ProfileSearchRequest ProfileSearchRequest, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      bool includeImages = ProfileSearchRequest.IncludeThumbnailImages;
      int responseResultLimit = includeImages ? 100 : 1000;
      int totalResultLimit = includeImages ? 1000 : 10000;

      bool maxResponseRecordCountValid = (1 <= ProfileSearchRequest.MaxResponseRecordCount)
        && (ProfileSearchRequest.MaxResponseRecordCount <= responseResultLimit)
        && (ProfileSearchRequest.MaxResponseRecordCount <= ProfileSearchRequest.MaxTotalRecordCount);
      if (!maxResponseRecordCountValid)
      {
        log.Debug("Invalid maxResponseRecordCount value '{0}'.", ProfileSearchRequest.MaxResponseRecordCount);
        details = "maxResponseRecordCount";
      }

      if (details == null)
      {
        bool maxTotalRecordCountValid = (1 <= ProfileSearchRequest.MaxTotalRecordCount) && (ProfileSearchRequest.MaxTotalRecordCount <= totalResultLimit);
        if (!maxTotalRecordCountValid)
        {
          log.Debug("Invalid maxTotalRecordCount value '{0}'.", ProfileSearchRequest.MaxTotalRecordCount);
          details = "maxTotalRecordCount";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Type != null))
      {
        bool typeValid = Encoding.UTF8.GetByteCount(ProfileSearchRequest.Type) <= ProtocolHelper.MaxProfileSearchTypeLengthBytes;
        if (!typeValid)
        {
          log.Debug("Invalid type value length '{0}'.", ProfileSearchRequest.Type.Length);
          details = "type";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Name != null))
      {
        bool nameValid = Encoding.UTF8.GetByteCount(ProfileSearchRequest.Name) <= ProtocolHelper.MaxProfileSearchNameLengthBytes;
        if (!nameValid)
        {
          log.Debug("Invalid name value length '{0}'.", ProfileSearchRequest.Name.Length);
          details = "name";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Latitude != GpsLocation.NoLocationLocationType))
      {
        GpsLocation locLat = new GpsLocation(ProfileSearchRequest.Latitude, 0);
        GpsLocation locLong = new GpsLocation(0, ProfileSearchRequest.Longitude);
        if (!locLat.IsValid())
        {
          log.Debug("Latitude '{0}' is not a valid GPS latitude value.", ProfileSearchRequest.Latitude);
          details = "latitude";
        }
        else if (!locLong.IsValid())
        {
          log.Debug("Longitude '{0}' is not a valid GPS longitude value.", ProfileSearchRequest.Longitude);
          details = "longitude";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Latitude != GpsLocation.NoLocationLocationType))
      {
        bool radiusValid = ProfileSearchRequest.Radius > 0;
        if (!radiusValid)
        {
          log.Debug("Invalid radius value '{0}'.", ProfileSearchRequest.Radius);
          details = "radius";
        }
      }

      if ((details == null) && (ProfileSearchRequest.ExtraData != null))
      {
        bool extraDataValid = RegexTypeValidator.ValidateProfileSearchRegex(ProfileSearchRequest.ExtraData);
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData regular expression filter.");
          details = "extraData";
        }
      }

      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Performs a search request on a repository to retrieve the list of profiles that match match specific criteria.
    /// </summary>
    /// <param name="Repository">Home or neighborhood identity repository, which is queried.</param>
    /// <param name="MaxResults">Maximum number of results to retrieve.</param>
    /// <param name="TypeFilter">Wildcard filter for identity type, or empty string if identity type filtering is not required.</param>
    /// <param name="NameFilter">Wildcard filter for profile name, or empty string if profile name filtering is not required.</param>
    /// <param name="LocationFilter">If not null, this value together with <paramref name="Radius"/> provide specification of target area, in which the identities has to have their location set. If null, GPS location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the target area radius with the centre in <paramref name="LocationFilter"/>.</param>
    /// <param name="ExtraDataFilter">Regular expression filter for identity's extraData information, or empty string if extraData filtering is not required.</param>
    /// <param name="IncludeImages">If true, the results will include profiles' thumbnail images.</param>
    /// <param name="TimeoutWatch">Stopwatch instance that is used to terminate the search query in case the execution takes too long. The stopwatch has to be started by the caller before calling this method.</param>
    /// <returns>List of network profile informations of identities that match the specific criteria.</returns>
    /// <remarks>In order to prevent DoS attacks, we require the search to complete within small period of time. 
    /// One the allowed time is up, the search is terminated even if we do not have enough results yet and there 
    /// is still a possibility to get more.</remarks>
    private async Task<List<IdentityNetworkProfileInformation>> ProfileSearch<T>(IdentityRepository<T> Repository, uint MaxResults, string TypeFilter, string NameFilter, GpsLocation LocationFilter, uint Radius, string ExtraDataFilter, bool IncludeImages, Stopwatch TimeoutWatch) where T : IdentityBase
    {
      log.Trace("(Repository:{0},MaxResults:{1},TypeFilter:'{2}',NameFilter:'{3}',LocationFilter:'{4}',Radius:{5},ExtraDataFilter:'{6}',IncludeImages:{7})",
        Repository, MaxResults, TypeFilter, NameFilter, LocationFilter, Radius, ExtraDataFilter, IncludeImages);

      List<IdentityNetworkProfileInformation> res = new List<IdentityNetworkProfileInformation>();

      uint batchSize = Math.Max(ProfileSearchBatchSizeMin, (uint)(MaxResults * ProfileSearchBatchSizeMaxFactor));
      uint offset = 0;

      RegexEval extraDataEval = !string.IsNullOrEmpty(ExtraDataFilter) ? new RegexEval(ExtraDataFilter, ProfileSearchMaxExtraDataMatchingTimeSingleMs, ProfileSearchMaxExtraDataMatchingTimeTotalMs) : null;

      long totalTimeMs = ProfileSearchMaxTimeMs;

      bool done = false;
      while (!done)
      {
        // Load result candidates from the database.
        bool noMoreResults = false;
        List<T> identities = null;
        try
        {
          identities = await Repository.ProfileSearch(offset, batchSize, TypeFilter, NameFilter, LocationFilter, Radius);
          noMoreResults = (identities == null) || (identities.Count < batchSize);
          if (noMoreResults)
            log.Debug("Received {0}/{1} results from repository, no more results available.", identities != null ? identities.Count : 0, batchSize);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
          done = true;
        }

        if (!done)
        {
          if (identities != null)
          {
            int accepted = 0;
            int filteredOutLocation = 0;
            int filteredOutExtraData = 0;
            foreach (T identity in identities)
            {
              // Terminate search if the time is up.
              if (totalTimeMs - TimeoutWatch.ElapsedMilliseconds < 0)
              {
                log.Debug("Time for search query ({0} ms) is up, terminating query.", totalTimeMs);
                done = true;
                break;
              }

              // Filter out profiles that do not match exact location filter and extraData filter
              GpsLocation identityLocation = new GpsLocation(identity.InitialLocationLatitude, identity.InitialLocationLongitude);
              if (LocationFilter != null)
              {
                double distance = GpsLocation.DistanceBetween(LocationFilter, identityLocation);
                bool withinArea = distance <= (double)Radius;
                if (!withinArea)
                {
                  filteredOutLocation++;
                  continue;
                }
              }

              if (!string.IsNullOrEmpty(ExtraDataFilter))
              {
                bool match = extraDataEval.Matches(identity.ExtraData);
                if (!match)
                {
                  filteredOutExtraData++;
                  continue;
                }
              }

              accepted++;

              // Convert identity to search result format.
              IdentityNetworkProfileInformation inpi = new IdentityNetworkProfileInformation();
              if (Repository is HostedIdentityRepository)
              {
                inpi.IsHosted = true;
                inpi.IsOnline = clientList.IsIdentityOnline(identity.IdentityId);
              }
              else
              {
                inpi.IsHosted = false;
                inpi.HostingServerNetworkId = ProtocolHelper.ByteArrayToByteString(identity.HostingServerId);
              }

              inpi.Version = ProtocolHelper.ByteArrayToByteString(identity.Version);
              inpi.IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(identity.PublicKey);
              inpi.Type = identity.Type != null ? identity.Type : "";
              inpi.Name = identity.Name != null ? identity.Name : "";
              inpi.Latitude = identityLocation.GetLocationTypeLatitude();
              inpi.Longitude = identityLocation.GetLocationTypeLongitude();
              inpi.ExtraData = identity.ExtraData;
              if (IncludeImages)
              {
                byte[] image = await identity.GetThumbnailImageDataAsync();
                if (image != null) inpi.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(image);
                else inpi.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[0]);
              }

              res.Add(inpi);
              if (res.Count >= MaxResults)
              {
                log.Debug("Target number of results {0} has been reached.", MaxResults);
                break;
              }
            }

            log.Info("Total number of examined records is {0}, {1} of them have been accepted, {2} filtered out by location, {3} filtered out by extra data filter.",
              accepted + filteredOutLocation + filteredOutExtraData, accepted, filteredOutLocation, filteredOutExtraData);
          }

          bool timedOut = totalTimeMs - TimeoutWatch.ElapsedMilliseconds < 0;
          if (timedOut) log.Debug("Time for search query ({0} ms) is up, terminating query.", totalTimeMs);
          done = noMoreResults || (res.Count >= MaxResults) || timedOut;
          offset += batchSize;
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Processes ProfileSearchPartRequest message from client.
    /// <para>Loads cached search results and sends them to the client.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public Message ProcessMessageProfileSearchPartRequest(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationAny, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      ProfileSearchPartRequest profileSearchPartRequest = RequestMessage.Request.ConversationRequest.ProfileSearchPart;

      int cacheResultsCount;
      bool cacheIncludeImages;
      if (Client.GetProfileSearchResultsInfo(out cacheResultsCount, out cacheIncludeImages))
      {
        int maxRecordCount = cacheIncludeImages ? ProfileSearchMaxResponseRecordsWithImage : ProfileSearchMaxResponseRecordsWithoutImage;
        bool recordIndexValid = (0 <= profileSearchPartRequest.RecordIndex) && (profileSearchPartRequest.RecordIndex < cacheResultsCount);
        bool recordCountValid = (0 <= profileSearchPartRequest.RecordCount) && (profileSearchPartRequest.RecordCount <= maxRecordCount)
          && (profileSearchPartRequest.RecordIndex + profileSearchPartRequest.RecordCount <= cacheResultsCount);

        if (recordIndexValid && recordCountValid)
        {
          List<IdentityNetworkProfileInformation> cachedResults = Client.GetProfileSearchResults((int)profileSearchPartRequest.RecordIndex, (int)profileSearchPartRequest.RecordCount);
          if (cachedResults != null)
          {
            res = messageBuilder.CreateProfileSearchPartResponse(RequestMessage, profileSearchPartRequest.RecordIndex, profileSearchPartRequest.RecordCount, cachedResults);
          }
          else
          {
            log.Trace("Cached results are no longer available for client ID {0}.", Client.Id.ToHex());
            res = messageBuilder.CreateErrorNotAvailableResponse(RequestMessage);
          }
        }
        else
        {
          log.Trace("Required record index is {0}, required record count is {1}.", recordIndexValid ? "valid" : "invalid", recordCountValid ? "valid" : "invalid");
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, !recordIndexValid ? "recordIndex" : "recordCount");
        }
      }
      else
      {
        log.Trace("No cached results are available for client ID {0}.", Client.Id.ToHex());
        res = messageBuilder.CreateErrorNotAvailableResponse(RequestMessage);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes AddRelatedIdentityRequest message from client.
    /// <para>Adds a proven relationship between an identity and the client to the list of client's related identities.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageAddRelatedIdentityRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      AddRelatedIdentityRequest addRelatedIdentityRequest = RequestMessage.Request.ConversationRequest.AddRelatedIdentity;

      Message errorResponse;
      if (ValidateAddRelatedIdentityRequest(Client, addRelatedIdentityRequest, messageBuilder, RequestMessage, out errorResponse))
      {
        CardApplicationInformation application = addRelatedIdentityRequest.CardApplication;
        SignedRelationshipCard signedCard = addRelatedIdentityRequest.SignedCard;
        RelationshipCard card = signedCard.Card;
        byte[] issuerSignature = signedCard.IssuerSignature.ToByteArray();
        byte[] recipientSignature = RequestMessage.Request.ConversationRequest.Signature.ToByteArray();
        byte[] cardId = card.CardId.ToByteArray();
        byte[] cardVersion = card.Version.ToByteArray();
        byte[] applicationId = application.ApplicationId.ToByteArray();
        string cardType = card.Type;
        DateTime validFrom = ProtocolHelper.UnixTimestampMsToDateTime(card.ValidFrom);
        DateTime validTo = ProtocolHelper.UnixTimestampMsToDateTime(card.ValidTo);
        byte[] issuerPublicKey = card.IssuerPublicKey.ToByteArray();
        byte[] recipientPublicKey = Client.PublicKey;
        byte[] issuerIdentityId = Crypto.Sha256(issuerPublicKey);

        RelatedIdentity newRelation = new RelatedIdentity()
        {
          ApplicationId = applicationId,
          CardId = cardId,
          CardVersion = cardVersion,
          IdentityId = Client.IdentityId,
          IssuerPublicKey = issuerPublicKey,
          IssuerSignature = issuerSignature,
          RecipientPublicKey = recipientPublicKey,
          RecipientSignature = recipientSignature,
          RelatedToIdentityId = issuerIdentityId,
          Type = cardType,
          ValidFrom = validFrom,
          ValidTo = validTo
        };

        res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          bool success = false;
          DatabaseLock lockObject = UnitOfWork.RelatedIdentityLock;
          using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObject))
          {
            try
            {
              int count = await unitOfWork.RelatedIdentityRepository.CountAsync(ri => ri.IdentityId == Client.IdentityId);
              if (count < Base.Configuration.MaxIdenityRelations)
              {
                RelatedIdentity existingRelation = (await unitOfWork.RelatedIdentityRepository.GetAsync(ri => (ri.IdentityId == Client.IdentityId) && (ri.ApplicationId == applicationId))).FirstOrDefault();
                if (existingRelation == null)
                {
                  await unitOfWork.RelatedIdentityRepository.InsertAsync(newRelation);
                  await unitOfWork.SaveThrowAsync();
                  transaction.Commit();
                  success = true;
                }
                else
                {
                  log.Warn("Client identity ID '{0}' already has relation application ID '{1}'.", Client.IdentityId.ToHex(), applicationId.ToHex());
                  res = messageBuilder.CreateErrorAlreadyExistsResponse(RequestMessage);
                }
              }
              else
              {
                log.Warn("Client identity '{0}' has too many ({1}) relations already.", Client.IdentityId.ToHex(), count);
                res = messageBuilder.CreateErrorQuotaExceededResponse(RequestMessage);
              }
            }
            catch (Exception e)
            {
              log.Error("Exception occurred: {0}", e.ToString());
            }

            if (success)
            {
              res = messageBuilder.CreateAddRelatedIdentityResponse(RequestMessage);
            }
            else
            {
              log.Warn("Rolling back transaction.");
              unitOfWork.SafeTransactionRollback(transaction);
            }

            unitOfWork.ReleaseLock(lockObject);
          }
        }
      }
      else res = errorResponse;

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Checks whether AddRelatedIdentityRequest request is valid.
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="AddRelatedIdentityRequest">Client's request message to validate.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the profile update request can be applied, false otherwise.</returns>
    private bool ValidateAddRelatedIdentityRequest(IncomingClient Client, AddRelatedIdentityRequest AddRelatedIdentityRequest, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      CardApplicationInformation cardApplication = AddRelatedIdentityRequest.CardApplication;
      SignedRelationshipCard signedCard = AddRelatedIdentityRequest.SignedCard;
      RelationshipCard card = signedCard.Card;

      byte[] applicationId = cardApplication.ApplicationId.ToByteArray();
      byte[] cardId = card.CardId.ToByteArray();

      if ((applicationId.Length == 0) || (applicationId.Length > RelatedIdentity.CardIdentifierLength))
      {
        log.Debug("Card application ID is invalid.");
        details = "cardApplication.applicationId";
      }

      if (details == null)
      {
        byte[] appCardId = cardApplication.CardId.ToByteArray();
        if (!StructuralEqualityComparer<byte[]>.Default.Equals(cardId, appCardId))
        {
          log.Debug("Card IDs in application card and relationship card do not match.");
          details = "cardApplication.cardId";
        }
      }

      if (details == null)
      {
        if (card.ValidFrom > card.ValidTo)
        {
          log.Debug("Card validFrom field is greater than validTo field.");
          details = "signedCard.card.validFrom";
        }
      }

      if (details == null)
      {
        byte[] issuerPublicKey = card.IssuerPublicKey.ToByteArray();
        bool pubKeyValid = (0 < issuerPublicKey.Length) && (issuerPublicKey.Length <= IdentityBase.MaxPublicKeyLengthBytes);
        if (!pubKeyValid)
        {
          log.Debug("Issuer public key has invalid length {0} bytes.", issuerPublicKey.Length);
          details = "signedCard.card.issuerPublicKey";
        }
      }

      if (details == null)
      {
        byte[] recipientPublicKey = card.RecipientPublicKey.ToByteArray();
        if (!StructuralEqualityComparer<byte[]>.Default.Equals(recipientPublicKey, Client.PublicKey))
        {
          log.Debug("Caller is not recipient of the card.");
          details = "signedCard.card.recipientPublicKey";
        }
      }

      if (details == null)
      {
        if (!Client.MessageBuilder.VerifySignedConversationRequestBodyPart(RequestMessage, cardApplication.ToByteArray(), Client.PublicKey))
        {
          log.Debug("Caller is not recipient of the card.");
          ErrorResponse = Client.MessageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
          details = "";
        }
      }

      if (details == null)
      {
        SemVer cardVersion = new SemVer(card.Version);
        if (!cardVersion.Equals(SemVer.V100))
        {
          log.Debug("Card version is invalid or not supported.");
          details = "signedCard.card.version";
        }
      }

      if (details == null)
      {
        if (Encoding.UTF8.GetByteCount(card.Type) > ProtocolHelper.MaxRelationshipCardTypeLengthBytes)
        {
          log.Debug("Card type is too long.");
          details = "signedCard.card.type";
        }
      }

      if (details == null)
      {
        RelationshipCard emptyIdCard = new RelationshipCard()
        {
          CardId = ProtocolHelper.ByteArrayToByteString(new byte[RelatedIdentity.CardIdentifierLength]),
          Version = card.Version,
          IssuerPublicKey = card.IssuerPublicKey,
          RecipientPublicKey = card.RecipientPublicKey,
          Type = card.Type,
          ValidFrom = card.ValidFrom,
          ValidTo = card.ValidTo
        };

        byte[] hash = Crypto.Sha256(emptyIdCard.ToByteArray());
        if (!StructuralEqualityComparer<byte[]>.Default.Equals(hash, cardId))
        {
          log.Debug("Card ID '{0}' does not match its hash '{1}'.", cardId.ToHex(64), hash.ToHex());
          details = "signedCard.card.cardId";
        }
      }

      if (details == null)
      {
        byte[] issuerSignature = signedCard.IssuerSignature.ToByteArray();
        byte[] issuerPublicKey = card.IssuerPublicKey.ToByteArray();
        if (!Ed25519.Verify(issuerSignature, cardId, issuerPublicKey))
        {
          log.Debug("Issuer signature is invalid.");
          details = "signedCard.issuerSignature";
        }
      }

      if (details == null)
      {
        res = true;
      }
      else
      {
        if (ErrorResponse == null)
          ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Processes RemoveRelatedIdentityRequest message from client.
    /// <para>Remove related identity from the list of client's related identities.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageRemoveRelatedIdentityRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      RemoveRelatedIdentityRequest removeRelatedIdentityRequest = RequestMessage.Request.ConversationRequest.RemoveRelatedIdentity;
      byte[] applicationId = removeRelatedIdentityRequest.ApplicationId.ToByteArray();

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.RelatedIdentityLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          RelatedIdentity existingRelation = (await unitOfWork.RelatedIdentityRepository.GetAsync(ri => (ri.IdentityId == Client.IdentityId) && (ri.ApplicationId == applicationId))).FirstOrDefault();
          if (existingRelation != null)
          {
            unitOfWork.RelatedIdentityRepository.Delete(existingRelation);
            if (await unitOfWork.SaveAsync())
            {
              res = messageBuilder.CreateRemoveRelatedIdentityResponse(RequestMessage);
            }
            else log.Error("Unable to delete client ID '{0}' relation application ID '{1}' from the database.", Client.IdentityId.ToHex(), applicationId.ToHex());
          }
          else
          {
            log.Warn("Client identity '{0}' relation application ID '{1}' does not exist.", Client.IdentityId.ToHex(), applicationId.ToHex());
            res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes GetIdentityRelationshipsInformationRequest message from client.
    /// <para>Obtains a list of related identities of that match given criteria.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageGetIdentityRelationshipsInformationRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      GetIdentityRelationshipsInformationRequest getIdentityRelationshipsInformationRequest = RequestMessage.Request.SingleRequest.GetIdentityRelationshipsInformation;
      byte[] identityId = getIdentityRelationshipsInformationRequest.IdentityNetworkId.ToByteArray();
      bool includeInvalid = getIdentityRelationshipsInformationRequest.IncludeInvalid;
      string type = getIdentityRelationshipsInformationRequest.Type;
      bool specificIssuer = getIdentityRelationshipsInformationRequest.SpecificIssuer;
      byte[] issuerId = specificIssuer ? getIdentityRelationshipsInformationRequest.IssuerNetworkId.ToByteArray() : null;

      if (Encoding.UTF8.GetByteCount(type) <= ProtocolHelper.MaxGetIdentityRelationshipsTypeLengthBytes)
      {
        res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          try
          {
            List<RelatedIdentity> relations = await unitOfWork.RelatedIdentityRepository.GetRelationsAsync(identityId, type, includeInvalid, issuerId);

            List<IdentityRelationship> identityRelationships = new List<IdentityRelationship>();
            foreach (RelatedIdentity relatedIdentity in relations)
            {
              CardApplicationInformation cardApplication = new CardApplicationInformation()
              {
                ApplicationId = ProtocolHelper.ByteArrayToByteString(relatedIdentity.ApplicationId),
                CardId = ProtocolHelper.ByteArrayToByteString(relatedIdentity.CardId),
              };

              RelationshipCard card = new RelationshipCard()
              {
                CardId = ProtocolHelper.ByteArrayToByteString(relatedIdentity.CardId),
                Version = ProtocolHelper.ByteArrayToByteString(relatedIdentity.CardVersion),
                IssuerPublicKey = ProtocolHelper.ByteArrayToByteString(relatedIdentity.IssuerPublicKey),
                RecipientPublicKey = ProtocolHelper.ByteArrayToByteString(relatedIdentity.RecipientPublicKey),
                Type = relatedIdentity.Type,
                ValidFrom = ProtocolHelper.DateTimeToUnixTimestampMs(relatedIdentity.ValidFrom),
                ValidTo = ProtocolHelper.DateTimeToUnixTimestampMs(relatedIdentity.ValidTo)
              };

              SignedRelationshipCard signedCard = new SignedRelationshipCard()
              {
                Card = card,
                IssuerSignature = ProtocolHelper.ByteArrayToByteString(relatedIdentity.IssuerSignature)
              };

              IdentityRelationship relationship = new IdentityRelationship()
              {
                Card = signedCard,
                CardApplication = cardApplication,
                CardApplicationSignature = ProtocolHelper.ByteArrayToByteString(relatedIdentity.RecipientSignature)
              };

              identityRelationships.Add(relationship);
            }

            res = messageBuilder.CreateGetIdentityRelationshipsInformationResponse(RequestMessage, identityRelationships);
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }
        }
      }
      else
      {
        log.Warn("Type filter is too long.");
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "type");
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Adds neighborhood actions that will announce a change in a specific identity profile to all followers of the profile server.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="ActionType">Type of action on the identity profile.</param>
    /// <param name="IdentityId">Identifier of the identity which caused the action.</param>
    /// <param name="AdditionalData">Additional data to store with the action.</param>
    /// <returns>
    /// true if at least one new action was added to the database, false otherwise.
    /// <para>
    /// This function can throw database exception and the caller is expected to call it within try/catch block.
    /// </para>
    /// </returns>
    /// <remarks>The caller of this function is responsible starting a database transaction with FollowerLock and NeighborhoodActionLock locks.</remarks>
    public async Task<bool> AddIdentityProfileFollowerActions(UnitOfWork UnitOfWork, NeighborhoodActionType ActionType, byte[] IdentityId, string AdditionalData = null)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());

      bool res = false;
      List<Follower> followers = (await UnitOfWork.FollowerRepository.GetAsync()).ToList();
      if (followers.Count > 0)
      {
        // Disable change tracking for faster multiple inserts.
        UnitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        DateTime now = DateTime.UtcNow;
        foreach (Follower follower in followers)
        {
          NeighborhoodAction neighborhoodAction = new NeighborhoodAction()
          {
            ServerId = follower.FollowerId,
            ExecuteAfter = null,
            TargetIdentityId = IdentityId,
            Timestamp = now,
            Type = ActionType,
            AdditionalData = AdditionalData
          };
          await UnitOfWork.NeighborhoodActionRepository.InsertAsync(neighborhoodAction);

          res = true;
          log.Trace("Add profile action with identity ID '{0}' added for follower ID '{1}'.", IdentityId.ToHex(), follower.FollowerId.ToHex());
        }
      }
      else log.Trace("No followers found to propagate identity profile change to.");

      log.Trace("(-):{0}", res);
      return res;
    }




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
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

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
    /// Installs InitializationProcessInProgress neighborhood action that will prevent 
    /// the profile server to sending updates to a new follower.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="FollowerId">Identifier of the follower to block updates to.</param>
    /// <returns>Action ID of the newly installed action, or -1 if the function fails.</returns>
    public async Task<int> InstallInitializationProcessInProgress(UnitOfWork UnitOfWork, byte[] FollowerId)
    {
      log.Trace("(FollowerId:'{0}')", FollowerId.ToHex());

      int res = -1;

      DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
      await UnitOfWork.AcquireLockAsync(lockObject);

      try
      {
        // This action will make sure the profile server will not send updates to the new follower
        // until the neighborhood initialization process is complete.
        NeighborhoodAction action = new NeighborhoodAction()
        {
          ServerId = FollowerId,
          Type = NeighborhoodActionType.InitializationProcessInProgress,
          TargetIdentityId = null,
          Timestamp = DateTime.UtcNow,
          AdditionalData = null,

          // This will cause other actions to this follower to be postponed for 20 minutes from now.
          ExecuteAfter = DateTime.UtcNow.AddMinutes(20)
        };
        await UnitOfWork.NeighborhoodActionRepository.InsertAsync(action);
        await UnitOfWork.SaveThrowAsync();
        res = action.Id;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      UnitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Uninstalls InitializationProcessInProgress neighborhood action that was installed by InstallInitializationProcessInProgress.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="FollowerId">Identifier of the follower.</param>
    /// <returns>true if the function suceeds, false otherwise.</returns>
    public async Task<bool> UninstallInitializationProcessInProgress(UnitOfWork UnitOfWork, int ActionId)
    {
      log.Trace("(ActionId:'{0}')", ActionId);

      bool res = false;

      DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
      await UnitOfWork.AcquireLockAsync(lockObject);

      try
      {
        NeighborhoodAction action = (await UnitOfWork.NeighborhoodActionRepository.GetAsync(a => a.Id == ActionId)).FirstOrDefault();
        if (action != null)
        {
          UnitOfWork.NeighborhoodActionRepository.Delete(action);
          await UnitOfWork.SaveThrowAsync();
          res = true;
        }
        else log.Error("Action ID {0} not found.", ActionId);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      UnitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Builds an update message for neighborhood initialization process.
    /// <para>An update message is built in a way that as many as possible consecutive profiles from the list 
    /// are being put into the message.</para>
    /// </summary>
    /// <param name="Client">Client for which the message is to be prepared.</param>
    /// <param name="Context">Context describing the status of the initialization process.</param>
    /// <returns>Upadate request message that is ready to be sent to the client.</returns>
    public async Task<Message> BuildNeighborhoodSharedProfileUpdateRequest(IncomingClient Client, NeighborhoodInitializationProcessContext Context)
    {
      log.Trace("()");

      Message res = Client.MessageBuilder.CreateNeighborhoodSharedProfileUpdateRequest();

      // We want to send as many items as possible in one message in order to minimize the number of messages 
      // there is some overhead when the message put into the final MessageWithHeader structure, 
      // so to be safe we just use 32 bytes less then the maximum.
      int messageSizeLimit = ProtocolHelper.MaxMessageSize - 32;

      int index = Context.IdentitiesDone;
      List<HostedIdentity> identities = Context.HostedIdentities;
      log.Trace("Starting with identity index {0}, total identities number is {1}.", index, identities.Count);
      while (index < identities.Count)
      {
        HostedIdentity identity = identities[index];
        byte[] thumbnailImage = await identity.GetThumbnailImageDataAsync();
        GpsLocation location = identity.GetInitialLocation();
        SharedProfileUpdateItem updateItem = new SharedProfileUpdateItem()
        {
          Add = new SharedProfileAddItem()
          {
            Version = ProtocolHelper.ByteArrayToByteString(identity.Version),
            IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(identity.PublicKey),
            Name = identity.Name,
            Type = identity.Type,
            SetThumbnailImage = thumbnailImage != null,
            Latitude = location.GetLocationTypeLatitude(),
            Longitude = location.GetLocationTypeLongitude(),
            ExtraData = identity.ExtraData
          }
        };

        if (updateItem.Add.SetThumbnailImage)
          updateItem.Add.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(thumbnailImage);


        res.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Add(updateItem);
        int newSize = res.CalculateSize();

        log.Trace("Index {0}, message size is {1} bytes, limit is {2} bytes.", index, newSize, messageSizeLimit);
        if (newSize > messageSizeLimit)
        {
          // We have reached the limit, remove the last item and send the message.
          res.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.RemoveAt(res.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Count - 1);
          break;
        }

        index++;
      }

      Context.IdentitiesDone += res.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Count;
      log.Debug("{0} update items inserted to the message. Already processed {1}/{2} profiles.", 
        res.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Count, Context.IdentitiesDone, Context.HostedIdentities.Count);

      log.Trace("(-)");
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
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes NeighborhoodSharedProfileUpdateRequest message from client.
    /// <para>Processes a shared profile update from a neighbor.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      NeighborhoodSharedProfileUpdateRequest neighborhoodSharedProfileUpdateRequest = RequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate;

      bool error = false;
      byte[] neighborId = Client.IdentityId;

      int sharedProfilesCount = 0;
      // First, we verify that the client is our neighbor and how many profiles it shares with us.
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == neighborId)).FirstOrDefault();
        if (neighbor == null)
        {
          log.Warn("Share profile update request came from client ID '{0}', who is not our neighbor.", neighborId.ToHex());
          res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
          error = true;
        }
        else if (neighbor.LastRefreshTime == null)
        {
          log.Warn("Share profile update request came from client ID '{0}', who is our neighbor, but we have not finished the initialization process with it yet.", neighborId.ToHex());
          res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
          error = true;
        } else
        {
          sharedProfilesCount = neighbor.SharedProfiles;
          log.Trace("Neighbor ID {0} currently shares {1} profiles with the profile server.", sharedProfilesCount, neighborId.ToHex());
        }
      }

      if (error)
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }


      // Second, we do a validation of all items without touching a database.

      // itemsImageHashes is a mapping of indexes of update items to hashes of images that has been successfully 
      // stored to the images folder.
      Dictionary<int, byte[]> itemsImageHashes = new Dictionary<int, byte[]>();

      // itemIndex will hold the index of the first item that is invalid.
      // If it reaches the number of items, all items are valid.
      int itemIndex = 0;

      // doRefresh is true, if at least one of the update items is of type Refresh.
      bool doRefresh = false;

      // List of network IDs of profiles that were validated in this batch already.
      // It is used to find multiple updates within the batch that work with the same profile, which is not allowed.
      HashSet<byte[]> usedProfileIdsInBatch = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);

      while (itemIndex < neighborhoodSharedProfileUpdateRequest.Items.Count)
      {
        SharedProfileUpdateItem updateItem = neighborhoodSharedProfileUpdateRequest.Items[itemIndex];
        Message errorResponse;
        if (ValidateSharedProfileUpdateItem(updateItem, itemIndex, sharedProfilesCount, usedProfileIdsInBatch, Client.MessageBuilder, RequestMessage, out errorResponse))
        {
          // Modify sharedProfilesCount to reflect the item we just validated.
          // In case of delete operation, we have not checked the existence yet, 
          // but it will be checked prior any problem could be caused by that.
          if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add) sharedProfilesCount++;
          else if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Delete) sharedProfilesCount--;

          // Is new image being transferred?
          byte[] newImageData = null;
          if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add && updateItem.Add.SetThumbnailImage) newImageData = updateItem.Add.ThumbnailImage.ToByteArray();
          else if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Change && updateItem.Change.SetThumbnailImage) newImageData = updateItem.Change.ThumbnailImage.ToByteArray();

          if ((newImageData != null) && (newImageData.Length != 0))
          {
            byte[] imageHash = Crypto.Sha256(newImageData);
            if (!await ImageManager.SaveImageDataAsync(imageHash, newImageData))
            {
              log.Error("Unable to save image data from item index {0} to file.", itemIndex);
              res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
              break;
            }

            itemsImageHashes.Add(itemIndex, imageHash);
          }

          if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Refresh)
            doRefresh = true;
        }
        else
        {
          res = errorResponse;
          break;
        }

        itemIndex++;
      }

      log.Debug("{0}/{1} update items passed validation, doRefresh is {2}.", itemIndex, neighborhoodSharedProfileUpdateRequest.Items.Count, doRefresh);


      // If there was a refresh request, we process it first as it does no harm and we do not need to care about it later.
      if (doRefresh)
        await UpdateNeighborLastRefreshTime(neighborId);


      // Now we save all valid items up to the first invalid (or all if all are valid).
      // But if we detect duplicity of identity with Add operation, or we can not find identity 
      // with Change or Delete action, we end earlier.
      // We will process the data in batches of max 100 items, not to occupy the database locks for too long.
      log.Trace("Saving {0} valid profiles changes.", itemIndex);

      // imagesToDelete is a list of image hashes that were replaced and the corresponding image files should be deleted.
      List<byte[]> imagesToDelete = new List<byte[]>();

      // Index of the update item currently being processed.
      int index = 0;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        // Batch number just for logging purposes.
        int batchNumber = 1;
        while (index < itemIndex)
        {
          log.Trace("Processing batch number {0}, which starts with item index {1}.", batchNumber, index);
          batchNumber++;

          // List of update item indexes with images that this batch used.
          // If the batch is saved to the database successfully, all images of these items are safe.
          List<int> batchUsedImageItemIndexes = new List<int>();

          // List of item image hashes of images that were removed during this batch.
          List<byte[]> batchDeletedImageHashes = new List<byte[]>();

          DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborIdentityLock, UnitOfWork.NeighborLock };
          using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
          {
            bool success = false;
            bool saveDb = false;
            try
            {
              Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == neighborId)).FirstOrDefault();
              if (neighbor != null)
              {
                int oldSharedProfilesCount = neighbor.SharedProfiles;
                for (int loopIndex = 0; loopIndex < 100; loopIndex++)
                {
                  SharedProfileUpdateItem updateItem = neighborhoodSharedProfileUpdateRequest.Items[index];

                  byte[] itemImageHash = null;
                  if (!itemsImageHashes.TryGetValue(index, out itemImageHash))
                    itemImageHash = null;

                  StoreSharedProfileUpdateResult storeResult = await StoreSharedProfileUpdateToDatabase(unitOfWork, updateItem, index, neighbor, itemImageHash, messageBuilder, RequestMessage);
                  if (storeResult.SaveDb) saveDb = true;
                  if (storeResult.Error) error = true;
                  if (storeResult.ErrorResponse != null) res = storeResult.ErrorResponse;
                  if (storeResult.ImageToDelete != null) batchDeletedImageHashes.Add(storeResult.ImageToDelete);
                  if (storeResult.ItemImageUsed) batchUsedImageItemIndexes.Add(index);

                  // Error here means that we want to save all already processed items to the database
                  // and quite the loop right after that, the response is filled with error response already.
                  if (error) break;

                  index++;
                  if (index >= itemIndex) break;
                }

                if (oldSharedProfilesCount != neighbor.SharedProfiles)
                {
                  unitOfWork.NeighborRepository.Update(neighbor);
                  saveDb = true;
                }
              }
              else
              {
                log.Error("Unable to find neighbor ID '{0}', sending ERROR_REJECTED response.", neighborId.ToHex());
                res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
              }

              if (saveDb)
              {
                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
              }
              success = true;
            }
            catch (Exception e)
            {
              log.Error("Exception occurred: {0}", e.ToString());
            }

            if (success)
            {
              // Data were saved to the database successfully.
              // All image hashes from this batch are safe in DB.
              // We remove the index from itemsImageHashes, which will leave 
              // unused images in itemsImageHashes.
              foreach (int iIndex in batchUsedImageItemIndexes)
              {
                if (itemsImageHashes.ContainsKey(iIndex))
                  itemsImageHashes.Remove(iIndex);
              }

              // Similarly, all deleted images should be processed as well.
              foreach (byte[] hash in batchDeletedImageHashes)
                imagesToDelete.Add(hash);
            }
            else
            {
              log.Warn("Rolling back transaction.");
              unitOfWork.SafeTransactionRollback(transaction);
            }

            unitOfWork.ReleaseLock(lockObjects);
          }

          if (error) break;
        }
      }


      // We now extend the list of images to delete with images of all profiles that were not saved to the database.
      // And then we delete all the image files that are not referenced from DB.
      foreach (byte[] hash in itemsImageHashes.Values)
          imagesToDelete.Add(hash);

      ImageManager imageManager = (ImageManager)Base.ComponentDictionary["Data.ImageManager"];
      foreach (byte[] hash in imagesToDelete)
        imageManager.RemoveImageReference(hash);


      if (res == null) res = messageBuilder.CreateNeighborhoodSharedProfileUpdateResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Description of result of StoreSharedProfileUpdateToDatabase function.
    /// </summary>
    private class StoreSharedProfileUpdateResult
    {
      /// <summary>True if there was a change to the database that should be saved.</summary>
      public bool SaveDb;
      
      /// <summary>True if there was an error and no more update items should be processed.</summary>
      public bool Error;

      /// <summary>If there was an error, this is the error response message to be delivered to the neighbor.</summary>
      public Message ErrorResponse;

      /// <summary>If any image was replaced and the file on disk should be deleted, this is set to its hash.</summary>
      public byte[] ImageToDelete;

      /// <summary>True if the ItemImage was used, false otherwise.</summary>
      public bool ItemImageUsed;
    }


    /// <summary>
    /// Updates a database according to the update item that is already partially validated.
    /// </summary>
    /// <param name="UnitOfWork">Instance of unit of work.</param>
    /// <param name="UpdateItem">Update item that is to be processed.</param>
    /// <param name="UpdateItemIndex">Index of the item within the request.</param>
    /// <param name="Neighbor">Identifier of the neighbor that sent the request.</param>
    /// <param name="ItemImageHash">Hash of the image related to this item, or null if this item does not have a related image.</param>
    /// <param name="MessageBuilder">Neighbor client's message builder.</param>
    /// <param name="RequestMessage">Original request message sent by the neighbor.</param>
    /// <returns>Result described by StoreSharedProfileUpdateResult class.</returns>
    /// <remarks>The caller of this function is responsible to call this function within a database transaction with acquired NeighborIdentityLock.</remarks>
    private async Task<StoreSharedProfileUpdateResult> StoreSharedProfileUpdateToDatabase(UnitOfWork UnitOfWork, SharedProfileUpdateItem UpdateItem, int UpdateItemIndex, Neighbor Neighbor, byte[] ItemImageHash, MessageBuilder MessageBuilder, Message RequestMessage)
    {
      log.Trace("(UpdateItemIndex:{0},Neighbor.SharedProfiles:{1})", UpdateItemIndex, Neighbor.SharedProfiles);

      StoreSharedProfileUpdateResult res = new StoreSharedProfileUpdateResult()
      {
        SaveDb = false,
        Error = false,
        ErrorResponse = null,
        ImageToDelete = null,
      };
    
      switch (UpdateItem.ActionTypeCase)
      {
        case SharedProfileUpdateItem.ActionTypeOneofCase.Add:
          {
            if (Neighbor.SharedProfiles >= IdentityBase.MaxHostedIdentities)
            {
              log.Error("Neighbor ID '{0}' already shares the maximum number of profiles.", Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".add");
              res.Error = true;
              break;
            }

            SharedProfileAddItem addItem = UpdateItem.Add;
            byte[] pubKey = addItem.IdentityPublicKey.ToByteArray();
            byte[] identityId = Crypto.Sha256(pubKey);

            // Identity already exists if there exists a NeighborIdentity with same identity ID and the same hosting server ID.
            NeighborIdentity existingIdentity = (await UnitOfWork.NeighborIdentityRepository.GetAsync(i => (i.IdentityId == identityId) && (i.HostingServerId == Neighbor.NeighborId))).FirstOrDefault();
            if (existingIdentity == null)
            {
              GpsLocation location = new GpsLocation(addItem.Latitude, addItem.Longitude);
              NeighborIdentity newIdentity = new NeighborIdentity()
              {
                IdentityId = identityId,
                HostingServerId = Neighbor.NeighborId,
                PublicKey = pubKey,
                Version = addItem.Version.ToByteArray(),
                Name = addItem.Name,
                Type = addItem.Type,
                InitialLocationLatitude = location.Latitude,
                InitialLocationLongitude = location.Longitude,
                ExtraData = addItem.ExtraData,
                ThumbnailImage = ItemImageHash,
                ExpirationDate = null
              };

              res.ItemImageUsed = ItemImageHash != null;

              await UnitOfWork.NeighborIdentityRepository.InsertAsync(newIdentity);
              Neighbor.SharedProfiles++;
              res.SaveDb = true;
            }
            else
            {
              log.Error("Identity ID '{0}' already exists with hosting server ID '{1}'.", identityId.ToHex(), Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".add.identityPublicKey");
              res.Error = true;
            }

            break;
          }

        case SharedProfileUpdateItem.ActionTypeOneofCase.Change:
          {
            SharedProfileChangeItem changeItem = UpdateItem.Change;
            byte[] identityId = changeItem.IdentityNetworkId.ToByteArray();

            // Identity already exists if there exists a NeighborIdentity with same identity ID and the same hosting server ID.
            NeighborIdentity existingIdentity = (await UnitOfWork.NeighborIdentityRepository.GetAsync(i => (i.IdentityId == identityId) && (i.HostingServerId == Neighbor.NeighborId))).FirstOrDefault();
            if (existingIdentity != null)
            {
              if (changeItem.SetVersion) existingIdentity.Version = changeItem.Version.ToByteArray();
              if (changeItem.SetName) existingIdentity.Name = changeItem.Name;

              if (changeItem.SetThumbnailImage)
              {
                res.ImageToDelete = existingIdentity.ThumbnailImage;

                existingIdentity.ThumbnailImage = ItemImageHash;
                res.ItemImageUsed = ItemImageHash != null;
              }

              if (changeItem.SetLocation)
              {
                existingIdentity.InitialLocationLatitude = changeItem.Latitude;
                existingIdentity.InitialLocationLongitude = changeItem.Longitude;
              }

              if (changeItem.SetExtraData) existingIdentity.ExtraData = changeItem.ExtraData;

              UnitOfWork.NeighborIdentityRepository.Update(existingIdentity);
              res.SaveDb = true;
            }
            else
            {
              log.Error("Identity ID '{0}' does exists with hosting server ID '{1}'.", identityId.ToHex(), Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".change.identityNetworkId");
              res.Error = true;
            }

            break;
          }

        case SharedProfileUpdateItem.ActionTypeOneofCase.Delete:
          {
            SharedProfileDeleteItem deleteItem = UpdateItem.Delete;
            byte[] identityId = deleteItem.IdentityNetworkId.ToByteArray();

            // Identity already exists if there exists a NeighborIdentity with same identity ID and the same hosting server ID.
            NeighborIdentity existingIdentity = (await UnitOfWork.NeighborIdentityRepository.GetAsync(i => (i.IdentityId == identityId) && (i.HostingServerId == Neighbor.NeighborId))).FirstOrDefault();
            if (existingIdentity != null)
            {
              res.ImageToDelete = existingIdentity.ThumbnailImage;

              UnitOfWork.NeighborIdentityRepository.Delete(existingIdentity);
              Neighbor.SharedProfiles--;
              res.SaveDb = true;
            }
            else
            {
              log.Error("Identity ID '{0}' does exists with hosting server ID '{1}'.", identityId.ToHex(), Neighbor.NeighborId.ToHex());
              res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".delete.identityNetworkId");
              res.Error = true;
            }
            break;
          }
      }

      log.Trace("(-):*.Error={0},*.SaveDb={1},*.ItemImageUsed={2},*.ImageToDelete='{3}'", res.Error, res.SaveDb, res.ItemImageUsed, res.ImageToDelete != null ? res.ImageToDelete.ToHex() : "null");
      return res;
    }



    /// <summary>
    /// Updates LastRefreshTime of a neighbor server.
    /// </summary>
    /// <param name="NeighborId">Identifier of the neighbor server to update.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> UpdateNeighborLastRefreshTime(byte[] NeighborId)
    {
      log.Trace("(NeighborId:'{0}')", NeighborId.ToHex());

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.NeighborLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            neighbor.LastRefreshTime = DateTime.UtcNow;
            unitOfWork.NeighborRepository.Update(neighbor);
            await unitOfWork.SaveThrowAsync();
          }
          else
          {
            // Between the check couple of lines above and here, the requesting server stop being our neighbor
            // we can ignore it now and proceed as this does no harm and the requesting server will be informed later.
            log.Error("Client ID '{0}' is no longer our neighbor.", NeighborId.ToHex());
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred while trying to update LastRefreshTime of neighbor ID '{0}': {1}", NeighborId.ToHex(), e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Validates incoming SharedProfileUpdateItem update item.
    /// </summary>
    /// <param name="UpdateItem">Update item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="SharedProfilesCount">Number of profiles the neighbor already shares with the profile server.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public bool ValidateSharedProfileUpdateItem(SharedProfileUpdateItem UpdateItem, int Index, int SharedProfilesCount, HashSet<byte[]> UsedProfileIdsInBatch, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("(Index:{0},SharedProfilesCount:{1})", Index, SharedProfilesCount);

      bool res = false;
      ErrorResponse = null;

      switch (UpdateItem.ActionTypeCase)
      {
        case SharedProfileUpdateItem.ActionTypeOneofCase.Add:
          res = ValidateSharedProfileAddItem(UpdateItem.Add, Index, SharedProfilesCount, UsedProfileIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedProfileUpdateItem.ActionTypeOneofCase.Change:
          res = ValidateSharedProfileChangeItem(UpdateItem.Change, Index, UsedProfileIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedProfileUpdateItem.ActionTypeOneofCase.Delete:
          res = ValidateSharedProfileDeleteItem(UpdateItem.Delete, Index, UsedProfileIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedProfileUpdateItem.ActionTypeOneofCase.Refresh:
          res = true;
          break;

        default:
          ErrorResponse = MessageBuilder.CreateErrorProtocolViolationResponse(RequestMessage);
          res = false;
          break;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedProfileAddItem update item.
    /// </summary>
    /// <param name="AddItem">Add item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="SharedProfilesCount">Number of profiles the neighbor already shares with the profile server.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public bool ValidateSharedProfileAddItem(SharedProfileAddItem AddItem, int Index, int SharedProfilesCount, HashSet<byte[]> UsedProfileIdsInBatch, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("(Index:{0},SharedProfilesCount:{1})", Index, SharedProfilesCount);

      bool res = false;
      ErrorResponse = null;

      string details = null;

      if (SharedProfilesCount >= IdentityBase.MaxHostedIdentities)
      {
        log.Debug("Target server already sent too many profiles.");
        details = "add";
      }

      if (details == null)
      {
        SemVer version = new SemVer(AddItem.Version);
        // Currently only supported version is 1.0.0.
        if (!version.Equals(SemVer.V100))
        {
          log.Debug("Unsupported version '{0}'.", version);
          details = "add.version";
        }
      }

      if (details == null)
      {
        // We do not verify identity duplicity here, that is being done in ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
        byte[] pubKey = AddItem.IdentityPublicKey.ToByteArray();
        bool pubKeyValid = (0 < pubKey.Length) && (pubKey.Length <= IdentityBase.MaxPublicKeyLengthBytes);
        if (pubKeyValid)
        {
          byte[] identityId = Crypto.Sha256(pubKey);
          if (!UsedProfileIdsInBatch.Contains(identityId))
          {
            UsedProfileIdsInBatch.Add(identityId);
          }
          else
          {
            log.Debug("ID '{0}' (public key '{1}') already processed in this request.", identityId.ToHex(), pubKey.ToHex());
            details = "add.identityPublicKey";
          }
        }
        else 
        {
          log.Debug("Invalid public key length '{0}'.", pubKey.Length);
          details = "add.identityPublicKey";
        }
      }

      if (details == null)
      {
        int nameSize = Encoding.UTF8.GetByteCount(AddItem.Name);
        bool nameValid = nameSize <= IdentityBase.MaxProfileNameLengthBytes;
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "add.name";
        }
      }

      if (details == null)
      {
        int typeSize = Encoding.UTF8.GetByteCount(AddItem.Type);
        bool typeValid = (0 < typeSize) && (typeSize <= IdentityBase.MaxProfileTypeLengthBytes);
        if (!typeValid)
        {
          log.Debug("Invalid type size in bytes {0}.", typeSize);
          details = "add.type";
        }
      }

      if ((details == null) && AddItem.SetThumbnailImage)
      {
        byte[] thumbnailImage = AddItem.ThumbnailImage.ToByteArray();

        bool imageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && ImageManager.ValidateImageFormat(thumbnailImage);
        if (!imageValid)
        {
          log.Debug("Invalid thumbnail image.");
          details = "add.thumbnailImage";
        }
      }

      if (details == null)
      {
        GpsLocation locLat = new GpsLocation(AddItem.Latitude, 0);
        if (!locLat.IsValid())
        {
          log.Debug("Invalid latitude {0}.", AddItem.Latitude);
          details = "add.latitude";
        }
      }

      if (details == null)
      {
        GpsLocation locLon = new GpsLocation(0, AddItem.Longitude);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", AddItem.Longitude);
          details = "add.longitude";
        }
      }

      if (details == null)
      {
        int extraDataSize = Encoding.UTF8.GetByteCount(AddItem.ExtraData);
        bool extraDataValid = extraDataSize <= IdentityBase.MaxProfileExtraDataLengthBytes;
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData size in bytes {0}.", extraDataSize);
          details = "add.extraData";
        }
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedProfileChangeItem update item.
    /// </summary>
    /// <param name="ChangeItem">Change item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public bool ValidateSharedProfileChangeItem(SharedProfileChangeItem ChangeItem, int Index, HashSet<byte[]> UsedProfileIdsInBatch, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      string details = null;

      byte[] identityId = ChangeItem.IdentityNetworkId.ToByteArray();
      // We do not verify identity existence here, that is being done in ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
      bool identityIdValid = identityId.Length == IdentityBase.IdentifierLength;
      if (identityIdValid)
      {
        if (!UsedProfileIdsInBatch.Contains(identityId))
        {
          UsedProfileIdsInBatch.Add(identityId);
        }
        else
        {
          log.Debug("ID '{0}' already processed in this request.", identityId.ToHex());
          details = "change.identityNetworkId";
        }
      }
      else 
      {
        log.Debug("Invalid identity ID length '{0}'.", identityId.Length);
        details = "change.identityNetworkId";
      }

      if (details == null)
      {
        if (!ChangeItem.SetVersion
          && !ChangeItem.SetName
          && !ChangeItem.SetThumbnailImage
          && !ChangeItem.SetLocation
          && !ChangeItem.SetExtraData)
        {
          log.Debug("Nothing is going to change.");
          details = "change.set*";
        }
      }

      if ((details == null) && ChangeItem.SetVersion)
      {
        SemVer version = new SemVer(ChangeItem.Version);
        // Currently only supported version is 1.0.0.
        if (!version.Equals(SemVer.V100))
        {
          log.Debug("Unsupported version '{0}'.", version);
          details = "change.version";
        }
      }


      if ((details == null) && ChangeItem.SetName)
      {
        int nameSize = Encoding.UTF8.GetByteCount(ChangeItem.Name);
        bool nameValid = nameSize <= IdentityBase.MaxProfileNameLengthBytes;
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "change.name";
        }
      }

      if ((details == null) && ChangeItem.SetThumbnailImage)
      {
        byte[] thumbnailImage = ChangeItem.ThumbnailImage.ToByteArray();

        bool deleteImage = thumbnailImage.Length == 0;
        bool imageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) 
          && (deleteImage || ImageManager.ValidateImageFormat(thumbnailImage));
        if (!imageValid)
        {
          log.Debug("Invalid thumbnail image.");
          details = "change.thumbnailImage";
        }
      }

      if ((details == null) && ChangeItem.SetLocation)
      {
        GpsLocation locLat = new GpsLocation(ChangeItem.Latitude, 0);
        if (!locLat.IsValid())
        {
          log.Debug("Invalid latitude {0}.", ChangeItem.Latitude);
          details = "change.latitude";
        }
      }

      if ((details == null) && ChangeItem.SetLocation)
      {
        GpsLocation locLon = new GpsLocation(0, ChangeItem.Longitude);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", ChangeItem.Longitude);
          details = "change.longitude";
        }
      }

      if ((details == null) && ChangeItem.SetExtraData)
      {
        int extraDataSize = Encoding.UTF8.GetByteCount(ChangeItem.ExtraData);
        bool extraDataValid = extraDataSize <= IdentityBase.MaxProfileExtraDataLengthBytes;
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData size in bytes {0}.", extraDataSize);
          details = "change.extraData";
        }
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Validates incoming SharedProfileDeleteItem update item.
    /// </summary>
    /// <param name="DeleteItem">Delete item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public bool ValidateSharedProfileDeleteItem(SharedProfileDeleteItem DeleteItem, int Index, HashSet<byte[]> UsedProfileIdsInBatch, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      string details = null;

      byte[] identityId = DeleteItem.IdentityNetworkId.ToByteArray();
      // We do not verify identity existence here, that is being done in ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
      bool identityIdValid = identityId.Length == IdentityBase.IdentifierLength;
      if (identityIdValid)
      {
        if (!UsedProfileIdsInBatch.Contains(identityId))
        {
          UsedProfileIdsInBatch.Add(identityId);
        }
        else
        {
          log.Debug("ID '{0}' already processed in this request.", identityId.ToHex());
          details = "delete.identityNetworkId";
        }
      }
      else
      { 
        log.Debug("Invalid identity ID length '{0}'.", identityId.Length);
        details = "delete.identityNetworkId";
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes StopNeighborhoodUpdatesRequest message from client.
    /// <para>Removes follower server from the database and also removes all pending actions to the follower.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageStopNeighborhoodUpdatesRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

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
    }


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
    }


    /// <summary>
    /// Processes FinishNeighborhoodInitializationResponse message from client.
    /// <para>
    /// This response is received when the neighborhood initialization process is finished and the follower server confirms that.
    /// The profile server marks the follower as fully synchronized in the database. It also unblocks the neighborhood action queue 
    /// for this follower.
    /// </para>
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="Request">Unfinished request message that corresponds to the response message.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageFinishNeighborhoodInitializationResponseAsync(IncomingClient Client, Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;

      if (Client.NeighborhoodInitializationProcessInProgress)
      {
        if (ResponseMessage.Response.Status == Status.Ok)
        {
          using (UnitOfWork unitOfWork = new UnitOfWork())
          {
            byte[] followerId = Client.IdentityId;

            bool success = false;
            bool signalNeighborhoodAction = false;
            DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
            using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
            {
              try
              {
                bool saveDb = false;

                // Update the follower, so it is considered as fully initialized.
                Follower follower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == followerId)).FirstOrDefault();
                if (follower != null)
                {
                  follower.LastRefreshTime = DateTime.UtcNow;
                  unitOfWork.FollowerRepository.Update(follower);
                  saveDb = true;
                }
                else log.Error("Follower ID '{0}' not found.", followerId.ToHex());

                // Update the blocking neighbhorhood action, so that new updates are sent to the follower.
                NeighborhoodAction action = (await unitOfWork.NeighborhoodActionRepository.GetAsync(a => (a.ServerId == followerId) && (a.Type == NeighborhoodActionType.InitializationProcessInProgress))).FirstOrDefault();
                if (action != null)
                {
                  action.ExecuteAfter = DateTime.UtcNow;
                  unitOfWork.NeighborhoodActionRepository.Update(action);
                  signalNeighborhoodAction = true;
                  saveDb = true;
                }
                else log.Error("Initialization process in progress neighborhood action for follower ID '{0}' not found.", followerId.ToHex());


                if (saveDb)
                {
                  await unitOfWork.SaveThrowAsync();
                  transaction.Commit();
                }

                Client.NeighborhoodInitializationProcessInProgress = false;
                success = true;
                res = true;
              }
              catch (Exception e)
              {
                log.Error("Exception occurred: {0}", e.ToString());
              }

              if (success)
              {
                if (signalNeighborhoodAction)
                {
                  NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary["Network.NeighborhoodActionProcessor"];
                  neighborhoodActionProcessor.Signal();
                }
              }
              else
              {
                log.Warn("Rolling back transaction.");
                unitOfWork.SafeTransactionRollback(transaction);
              }
            }
            unitOfWork.ReleaseLock(lockObjects);
          }
        }
        else
        {
          // Client is a follower in the middle of the initialization process and failed to accept the finish request.
          // Follower will be deleted automatically as the connection terminates in IncomingClient.HandleDisconnect.
          log.Error("Client ID '{0}' is a follower and failed to accept finish request to neighborhood initialization process (error code {1}), it will be disconnected and deleted from our database.", Client.IdentityId.ToHex(), ResponseMessage.Response.Status);
        }
      }
      else log.Error("Client ID '{0}' does not have neighborhood initialization process in progress.", Client.IdentityId.ToHex());

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes CanStoreDataRequest message from client.
    /// <para>Uploads client's data object to CAN and returns CAN hash of it.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageCanStoreDataRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      CanStoreDataRequest canStoreDataRequest = RequestMessage.Request.ConversationRequest.CanStoreData;

      // First check whether the new object is valid.
      CanIdentityData identityData = canStoreDataRequest.Data;
      bool uploadNew = identityData != null;
      if (uploadNew)
      {
        byte[] claimedServerId = identityData.HostingServerId.ToByteArray();
        byte[] realServerId = serverComponent.ServerId;

        if (!StructuralEqualityComparer<byte[]>.Default.Equals(claimedServerId, realServerId))
        {
          log.Debug("Identity data object from client contains invalid hostingServerId.");
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "data.hostingServerId");
        }
      }

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        CanApi canApi = (CanApi)Base.ComponentDictionary["Network.ContentAddressNetwork.CanApi"];
        
        // Then delete old object if there is any.
        if (res == null)
        {
          res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

          byte[] canOldObjectHash = await unitOfWork.HostedIdentityRepository.GetCanObjectHashAsync(Client.IdentityId);
          bool deleteOldObjectFromDb = false;
          if (canOldObjectHash != null)
          {
            string objectPath = CanApi.CreateIpfsPathFromHash(canOldObjectHash);

            CanDeleteResult cres = await canApi.CanDeleteObject(objectPath);
            if (cres.Success)
            {
              log.Debug("Old CAN object hash '{0}' of client identity ID '{1}' deleted.", canOldObjectHash.ToBase58(), Client.IdentityId.ToHex());
              deleteOldObjectFromDb = true;
            }
            else
            {
              log.Warn("Failed to delete old CAN object hash '{0}', error message '{1}'.", canOldObjectHash, cres.Message);
              res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, cres.Message);
            }
          }
          else res = null;

          if (deleteOldObjectFromDb)
          {
            // Object was successfully deleted from CAN, remove it from DB.
            if (await unitOfWork.HostedIdentityRepository.SetCanObjectHashAsync(unitOfWork, Client.IdentityId, null))
            {
              log.Debug("Old CAN object of client identity ID '{0}' has been deleted from database.", Client.IdentityId.ToHex());
              res = null;
            }
          }
        }

        if (res == null)
        {
          res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

          // Now upload the new object if any.
          if (uploadNew)
          {
            byte[] canObject = identityData.ToByteArray();
            CanUploadResult cres = await canApi.CanUploadObject(canObject);
            if (cres.Success)
            {
              byte[] canHash = cres.Hash;
              log.Info("New CAN object hash '{0}' added for client identity ID '{1}'.", canHash.ToBase58(), Client.IdentityId.ToHex());

              if (await unitOfWork.HostedIdentityRepository.SetCanObjectHashAsync(unitOfWork, Client.IdentityId, canHash))
              {
                res = messageBuilder.CreateCanStoreDataResponse(RequestMessage, canHash);
              }
              else
              {
                // Unable to save the new can hash to DB, so delete the object from CAN as well.
                string objectPath = CanApi.CreateIpfsPathFromHash(canHash);
                CanDeleteResult delRes = await canApi.CanDeleteObject(objectPath);
                if (delRes.Success) log.Debug("CAN object hash '{0}' deleted.", canHash.ToBase58());
                else log.Debug("Failed to delete CAN object hash '{0}'.", canHash.ToBase58());
              }
            }
            else
            {
              res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, cres.Message);
            }
          }
          else
          {
            log.Debug("No new object to upload.");
            res = messageBuilder.CreateCanStoreDataResponse(RequestMessage, null);
          }
        }
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes CanPublishIpnsRecordRequest message from client.
    /// <para>Uploads client's IPNS record to CAN.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageCanPublishIpnsRecordRequestAsync(IncomingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      Message res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Response.Status);
        return res;
      }

      MessageBuilder messageBuilder = Client.MessageBuilder;
      CanPublishIpnsRecordRequest canPublishIpnsRecordRequest = RequestMessage.Request.ConversationRequest.CanPublishIpnsRecord;

      if (canPublishIpnsRecordRequest.Record == null)
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "record");

      if (res == null)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          HostedIdentity identity = await unitOfWork.HostedIdentityRepository.GetHostedIdentityByIdAsync(Client.IdentityId);
          if (identity != null)
          {
            byte[] canObjectHash = identity.CanObjectHash;
            if (canObjectHash != null)
            {
              string objectPath = CanApi.CreateIpfsPathFromHash(canObjectHash);
              try
              {
                string value = Encoding.UTF8.GetString(canPublishIpnsRecordRequest.Record.Value.ToByteArray());
                if (value != objectPath)
                {
                  log.Trace("IPNS record value does not point to the path of the identity client ID '{0}' CAN object.", Client.IdentityId.ToHex());
                  res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "record.value");
                }
              }
              catch
              {
                log.Trace("IPNS record value does not contain a valid CAN object path.");
                res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "record.value");
              }
            }
            else
            {
              log.Trace("Identity client ID '{0}' has no CAN object.", Client.IdentityId.ToHex());
              res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
            }

#warning TODO: Check that canPublishIpnsRecordRequest.Record.Validity does not exceed the identity's hosting plan expiration time. This will be possible once we implement hosting contracts.
          }
          else
          {
            log.Error("Identity ID '{0}' not found.", Client.IdentityId.ToHex());
            res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
          }
        }
      }

      if (res == null)
      {
        CanApi canApi = (CanApi)Base.ComponentDictionary["Network.ContentAddressNetwork.CanApi"];
        CanRefreshIpnsResult cres = await canApi.RefreshIpnsRecord(canPublishIpnsRecordRequest.Record, Client.PublicKey);
        if (cres.Success) res = messageBuilder.CreateCanPublishIpnsRecordResponse(RequestMessage);
        else res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, cres.Message);
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }
  }
}
