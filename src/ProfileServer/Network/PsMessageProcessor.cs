using Google.Protobuf;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using ProfileServer.Data.Repositories;
using ProfileServer.Kernel;
using IopCrypto;
using IopProtocol;
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
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;
using IopServerCore.Network;
using IopServerCore.Network.CAN;
using System.Net;
using Iop.Shared;
using System.Runtime.CompilerServices;

namespace ProfileServer.Network
{
  /// <summary>
  /// Implements the logic behind processing incoming messages to the profile server.
  /// </summary>
  public class PsMessageProcessor: IMessageProcessor<Message>
  {
    /// <summary>Instance logger.</summary>
    private Logger log;

    /// <summary>Prefix used</summary>
    private string logPrefix;

    /// <summary>Parent role server.</summary>
    private TcpRoleServer<IncomingClient, Message> roleServer;

    /// <summary>Pointer to the Network.Server component.</summary>
    private Server serverComponent;

    /// <summary>List of server's network peers and clients owned by Network.Server component.</summary>
    public IncomingClientList<Message> clientList;

    /// <summary>List of server's network peers and clients owned by Network.Server component.</summary>
    public RelayList relayList;

    /// <summary>
    /// Creates a new instance connected to the parent role server.
    /// </summary>
    /// <param name="RoleServer">Parent role server.</param>
    /// <param name="LogPrefix">Log prefix of the parent role server.</param>
    public PsMessageProcessor(TcpRoleServer<IncomingClient, Message> RoleServer, string LogPrefix = "")
    {
      roleServer = RoleServer;
      logPrefix = LogPrefix;
      log = new Logger("ProfileServer.Network.PsMessageProcessor", logPrefix);
      serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
      clientList = serverComponent.Clients;
      relayList = serverComponent.RelayList;
    }



    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client who send the message.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(ClientBase<Message> Client, IProtocolMessage<Message> IncomingMessage)
    {
      IncomingClient client = (IncomingClient)Client;
      var incomingMessage = IncomingMessage;
      PsMessageBuilder messageBuilder = client.MessageBuilder;

      bool res = false;
      log.Debug("()");
      try
      {
        // Update time until this client's connection is considered inactive.
        client.NextKeepAliveTime = DateTime.UtcNow.AddMilliseconds(client.KeepAliveIntervalMs);
        log.Trace("Client ID {0} NextKeepAliveTime updated to {1}.", client.Id.ToHex(), client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));

        log.Trace("Received message type is {0}, message ID is {1}.", incomingMessage.Message.MessageTypeCase, incomingMessage.Id);
        switch (incomingMessage.Message.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              var responseMessage = messageBuilder.CreateErrorProtocolViolationResponse(incomingMessage);
              Request request = incomingMessage.Message.Request;
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
                      responseMessage.Message.Response.Details = "version";
                      break;
                    }

                    switch (singleRequest.RequestTypeCase)
                    {
                      case SingleRequest.RequestTypeOneofCase.Ping:
                        responseMessage = ProcessMessagePingRequest(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ListRoles:
                        responseMessage = ProcessMessageListRolesRequest(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.GetProfileInformation:
                        responseMessage = await ProcessMessageGetProfileInformationRequestAsync(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ProfileSearch:
                        responseMessage = await ProcessMessageProfileSearchRequestAsync(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ProfileSearchPart:
                        responseMessage = ProcessMessageProfileSearchPartRequest(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ApplicationServiceSendMessage:
                        responseMessage = await ProcessMessageApplicationServiceSendMessageRequestAsync(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.ProfileStats:
                        responseMessage = await ProcessMessageProfileStatsRequestAsync(client, incomingMessage);
                        break;

                      case SingleRequest.RequestTypeOneofCase.GetIdentityRelationshipsInformation:
                        responseMessage = await ProcessMessageGetIdentityRelationshipsInformationRequestAsync(client, incomingMessage);
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
                        responseMessage = ProcessMessageStartConversationRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.RegisterHosting:
                        responseMessage = await ProcessMessageRegisterHostingRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CheckIn:
                        responseMessage = await ProcessMessageCheckInRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.VerifyIdentity:
                        responseMessage = ProcessMessageVerifyIdentityRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.UpdateProfile:
                        responseMessage = await ProcessMessageUpdateProfileRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CancelHostingAgreement:
                        responseMessage = await ProcessMessageCancelHostingAgreementRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ApplicationServiceAdd:
                        responseMessage = ProcessMessageApplicationServiceAddRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.ApplicationServiceRemove:
                        responseMessage = ProcessMessageApplicationServiceRemoveRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CallIdentityApplicationService:
                        responseMessage = await ProcessMessageCallIdentityApplicationServiceRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.AddRelatedIdentity:
                        responseMessage = await ProcessMessageAddRelatedIdentityRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.RemoveRelatedIdentity:
                        responseMessage = await ProcessMessageRemoveRelatedIdentityRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization:
                        responseMessage = await ProcessMessageStartNeighborhoodInitializationRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                        responseMessage = ProcessMessageFinishNeighborhoodInitializationRequest(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate:
                        responseMessage = await ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.StopNeighborhoodUpdates:
                        responseMessage = await ProcessMessageStopNeighborhoodUpdatesRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CanStoreData:
                        responseMessage = await ProcessMessageCanStoreDataRequestAsync(client, incomingMessage);
                        break;

                      case ConversationRequest.RequestTypeOneofCase.CanPublishIpnsRecord:
                        responseMessage = await ProcessMessageCanPublishIpnsRecordRequestAsync(client, incomingMessage);
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
                res = await client.SendMessageAsync(responseMessage);

                if (res)
                {
                  // If the message was sent successfully to the target, we close the connection in case it was a protocol violation error response.
                  if (responseMessage.Message.MessageTypeCase == Message.MessageTypeOneofCase.Response)
                    res = responseMessage.Message.Response.Status != Status.ErrorProtocolViolation;
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
              Response response = incomingMessage.Message.Response;
              log.Trace("Response status is {0}, details are '{1}', conversation type is {2}.", response.Status, response.Details, response.ConversationTypeCase);

              // Find associated request. If it does not exist, disconnect the client as it 
              // send a response without receiving a request. This is protocol violation, 
              // but as this is a reponse, we have no how to inform the client about it, 
              // so we just disconnect it.
              UnfinishedRequest<Message> unfinishedRequest = client.GetAndRemoveUnfinishedRequest(incomingMessage.Id);
              if ((unfinishedRequest != null) && (unfinishedRequest.RequestMessage != null))
              {
                var requestMessage = unfinishedRequest.RequestMessage;
                Request request = requestMessage.Message.Request;
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
                            res = await ProcessMessageApplicationServiceReceiveMessageNotificationResponseAsync(client, incomingMessage, unfinishedRequest);
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
                            res = await ProcessMessageIncomingCallNotificationResponseAsync(client, incomingMessage, unfinishedRequest);
                            break;

                          case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate:
                            res = await ProcessMessageNeighborhoodSharedProfileUpdateResponseAsync(client, incomingMessage, unfinishedRequest);
                            break;

                          case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                            res = await ProcessMessageFinishNeighborhoodInitializationResponseAsync(client, incomingMessage, unfinishedRequest);
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
                  log.Warn("Message type of the response ID {0} does not match the message type of the request ID {1}, the connection will be closed.", incomingMessage.Id, unfinishedRequest.RequestMessage.Id);
                  // Connection will be closed in ReceiveMessageLoop.
                }
              }
              else
              {
                log.Warn("No unfinished request found for incoming response ID {0}, the connection will be closed.", incomingMessage.Id);
                // Connection will be closed in ReceiveMessageLoop.
              }

              break;
            }

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", incomingMessage.Message.MessageTypeCase);
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

      if (res && client.ForceDisconnect)
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
    public async Task SendProtocolViolation(ClientBase<Message> Client)
    {
      PsMessageBuilder mb = new PsMessageBuilder(0, new List<SemVer>() { SemVer.V100 }, null);
      var response = mb.CreateErrorProtocolViolationResponse();

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
    public bool CheckSessionConditions(IncomingClient Client, IProtocolMessage<Message> RequestMessage, ServerRole? RequiredRole, ClientConversationStatus? RequiredConversationStatus, out IProtocolMessage<Message> ResponseMessage)
    {
      log.Trace("(RequiredRole:{0},RequiredConversationStatus:{1})", RequiredRole != null ? RequiredRole.ToString() : "null", RequiredConversationStatus != null ? RequiredConversationStatus.Value.ToString() : "null");

      bool res = false;
      ResponseMessage = null;

      string requestName = RequestMessage.Message.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest ? "single request " + RequestMessage.Message.Request.SingleRequest.RequestTypeCase.ToString() : "conversation request " + RequestMessage.Message.Request.ConversationRequest.RequestTypeCase.ToString();

      // RequiredRole contains one or more roles and the current server has to have at least one of them.
      if ((RequiredRole == null) || ((roleServer.Roles & (uint)RequiredRole.Value) != 0))
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
    public IProtocolMessage<Message> ProcessMessagePingRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      PingRequest pingRequest = RequestMessage.Message.Request.SingleRequest.Ping;

      var res = messageBuilder.CreatePingResponse(RequestMessage, pingRequest.Payload.ToByteArray(), DateTime.UtcNow);

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes ListRolesRequest message from client.
    /// <para>Obtains a list of role servers and returns it in the response.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public IProtocolMessage<Message> ProcessMessageListRolesRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.Primary, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      ListRolesRequest listRolesRequest = RequestMessage.Message.Request.SingleRequest.ListRoles;

      List<Iop.Profileserver.ServerRole> roles = GetRolesFromServerComponent();
      res = messageBuilder.CreateListRolesResponse(RequestMessage, roles);

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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

      var res = new List<Iop.Profileserver.ServerRole>();

      foreach (var roleServer in serverComponent.Servers)
      {
        foreach (ServerRole role in Enum.GetValues(typeof(ServerRole)))
        {
          if ((roleServer.Roles & (uint)role) != 0)
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
    /// Processes GetProfileInformationRequest message from client.
    /// <para>Obtains information about identity that is hosted by the profile server.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageGetProfileInformationRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      GetProfileInformationRequest getProfileInformationRequest = RequestMessage.Message.Request.SingleRequest.GetProfileInformation;

      byte[] identityId = getProfileInformationRequest.IdentityNetworkId.ToByteArray();
      if (identityId.Length == ProtocolHelper.NetworkIdentifierLength)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.IdentityId == identityId)).FirstOrDefault();
          if (identity != null)
          {
            bool isHosted = !identity.Cancelled;
            if (isHosted)
            {
              if (identity.Initialized)
              {
                IncomingClient targetClient = (IncomingClient)clientList.GetAuthenticatedOnlineClient(identityId);
                bool isOnline = targetClient != null;

                SignedProfileInformation signedProfile = identity.ToSignedProfileInformation();

                byte[] profileImage = null;
                byte[] thumbnailImage = null;
                HashSet<string> applicationServices = null;

                if (getProfileInformationRequest.IncludeProfileImage)
                  profileImage = await identity.GetProfileImageDataAsync();

                if (getProfileInformationRequest.IncludeThumbnailImage)
                  thumbnailImage = await identity.GetThumbnailImageDataAsync();

                if (getProfileInformationRequest.IncludeApplicationServices)
                  applicationServices = targetClient != null ? targetClient.ApplicationServices.GetServices() : null;

                res = messageBuilder.CreateGetProfileInformationResponse(RequestMessage, isHosted, null, isOnline, signedProfile, profileImage, thumbnailImage, applicationServices);
              }
              else
              {
                log.Trace("Identity ID '{0}' profile not initialized.", identityId.ToHex());
                res = messageBuilder.CreateErrorUninitializedResponse(RequestMessage);
              }
            }
            else
            {
              byte[] targetHostingServer = identity.HostingServerId;
              res = messageBuilder.CreateGetProfileInformationResponse(RequestMessage, isHosted, targetHostingServer);
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }


    /// <summary>
    /// Processes StartConversationRequest message from client.
    /// <para>Initiates a conversation with the client provided that there is a common version of the protocol supported by both sides.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public IProtocolMessage<Message> ProcessMessageStartConversationRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, null, ClientConversationStatus.NoConversation, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }


      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      StartConversationRequest startConversationRequest = RequestMessage.Message.Request.ConversationRequest.Start;
      byte[] clientChallenge = startConversationRequest.ClientChallenge.ToByteArray();
      byte[] pubKey = startConversationRequest.PublicKey.ToByteArray();

      if (clientChallenge.Length == PsMessageBuilder.ChallengeDataSize)
      {
        if ((0 < pubKey.Length) && (pubKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes))
        {
          SemVer version;
          if (GetCommonSupportedVersion(startConversationRequest.SupportedVersions, out version))
          {
            Client.PublicKey = pubKey;
            Client.IdentityId = Crypto.Sha256(Client.PublicKey);

            if (clientList.AddNetworkPeerWithIdentity(Client))
            {
              Client.MessageBuilder.SetProtocolVersion(version);

              byte[] challenge = new byte[PsMessageBuilder.ChallengeDataSize];
              Crypto.Rng.GetBytes(challenge);
              Client.AuthenticationChallenge = challenge;
              Client.ConversationStatus = ClientConversationStatus.ConversationStarted;

              log.Debug("Client {0} conversation status updated to {1}, selected version is '{2}', client public key set to '{3}', client identity ID set to '{4}', challenge set to '{5}'.",
                Client.RemoteEndPoint, Client.ConversationStatus, version, Client.PublicKey.ToHex(), Client.IdentityId.ToHex(), Client.AuthenticationChallenge.ToHex());

              res = messageBuilder.CreateStartConversationResponse(RequestMessage, version, Config.Configuration.Keys.PublicKey, Client.AuthenticationChallenge, clientChallenge);
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
        log.Warn("Client send clientChallenge, which is {0} bytes long, but it should be {1} bytes long.", clientChallenge.Length, PsMessageBuilder.ChallengeDataSize);
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "clientChallenge");
      }

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<IProtocolMessage<Message>> ProcessMessageRegisterHostingRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
#warning TODO: This function is currently implemented to support arbitrary contracts as the server does not have any list of contracts.
      // TODO: CHECK CONTRACT:
      // * planId is valid
      // * identityType is valid (per existing contract)
      log.Trace("()");
      log.Fatal("TODO UNIMPLEMENTED");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      RegisterHostingRequest registerHostingRequest = RequestMessage.Message.Request.ConversationRequest.RegisterHosting;
      if (registerHostingRequest == null) registerHostingRequest = new RegisterHostingRequest();
      if (registerHostingRequest.Contract == null) registerHostingRequest.Contract = new HostingPlanContract();

      HostingPlanContract contract = registerHostingRequest.Contract;

      bool success = false;
      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

      IProtocolMessage<Message> errorResponse;
      if (InputValidators.ValidateRegisterHostingRequest(Client.PublicKey, contract, messageBuilder, RequestMessage, out errorResponse))
      {
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
              if (hostedIdentities < Config.Configuration.MaxHostedIdentities)
              {
                HostedIdentity existingIdentity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.IdentityId == Client.IdentityId)).FirstOrDefault();
                // Identity does not exist at all, or it has been cancelled so that ExpirationDate was set.
                if ((existingIdentity == null) || (existingIdentity.Cancelled))
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
                  identity.Type = contract.IdentityType;
                  // Existing cancelled identity profile does not have images, no need to delete anything at this point.
                  identity.Signature = null;
                  identity.ProfileImage = null;
                  identity.ThumbnailImage = null;
                  identity.InitialLocationLatitude = GpsLocation.NoLocation.Latitude;
                  identity.InitialLocationLongitude = GpsLocation.NoLocation.Longitude;
                  identity.ExtraData = "";
#warning TODO: When we implement hosting plans, this should be set to the period of hosting contract + some reserve.
                  identity.ExpirationDate = DateTime.UtcNow.AddYears(10);
                  identity.Initialized = false;
                  identity.Cancelled = false;

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
                log.Debug("MaxHostedIdentities {0} has been reached.", Config.Configuration.MaxHostedIdentities);
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
      }
      else res = errorResponse;


      if (success)
      {
        log.Debug("Identity '{0}' added to database.", Client.IdentityId.ToHex());
        res = messageBuilder.CreateRegisterHostingResponse(RequestMessage, contract);
      }

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<IProtocolMessage<Message>> ProcessMessageCheckInRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }


      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      CheckInRequest checkInRequest = RequestMessage.Message.Request.ConversationRequest.CheckIn;

      byte[] challenge = checkInRequest.Challenge.ToByteArray();
      if (ByteArrayComparer.Equals(challenge, Client.AuthenticationChallenge))
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
              HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.Cancelled == false))).FirstOrDefault();
              if (identity != null)
              {
                if (await clientList.AddAuthenticatedOnlineClient(Client))
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public IProtocolMessage<Message> ProcessMessageVerifyIdentityRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer | ServerRole.ServerNeighbor, ClientConversationStatus.ConversationStarted, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }


      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      VerifyIdentityRequest verifyIdentityRequest = RequestMessage.Message.Request.ConversationRequest.VerifyIdentity;

      byte[] challenge = verifyIdentityRequest.Challenge.ToByteArray();
      if (ByteArrayComparer.Equals(challenge, Client.AuthenticationChallenge))
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<IProtocolMessage<Message>> ProcessMessageUpdateProfileRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      UpdateProfileRequest updateProfileRequest = RequestMessage.Message.Request.ConversationRequest.UpdateProfile;
      if (updateProfileRequest == null) updateProfileRequest = new UpdateProfileRequest();
      if (updateProfileRequest.Profile == null) updateProfileRequest.Profile = new ProfileInformation();

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        try
        {
          HostedIdentity identityForValidation = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.Cancelled == false), null, true)).FirstOrDefault();
          if (identityForValidation != null)
          {
            IProtocolMessage<Message> errorResponse;
            if (InputValidators.ValidateUpdateProfileRequest(identityForValidation, updateProfileRequest, messageBuilder, RequestMessage, out errorResponse))
            {
              bool error = false;
              ProfileInformation profile = updateProfileRequest.Profile;

              // If an identity has a profile image and a thumbnail image, they are saved on the disk.
              // If we are replacing those images, we have to create new files and delete the old files.
              // First, we create the new files and then in DB transaction, we get information about 
              // whether to delete existing files and which ones.
              List<byte[]> imagesToDelete = new List<byte[]>();
              List<byte[]> newlyCreatedImages = new List<byte[]>();

              byte[] newProfileImageHash = profile.ProfileImageHash.Length != 0 ? profile.ProfileImageHash.ToByteArray() : null;
              bool profileImageChanged = !ByteArrayComparer.Equals(identityForValidation.ProfileImage, newProfileImageHash);
              if (profileImageChanged)
              {
                if (newProfileImageHash != null)
                {
                  byte[] profileImage = updateProfileRequest.ProfileImage.ToByteArray();

                  identityForValidation.ProfileImage = newProfileImageHash;
                  if (await identityForValidation.SaveProfileImageDataAsync(profileImage))
                  {
                    // In case we of an error, we have to delete this newly saved image.
                    newlyCreatedImages.Add(newProfileImageHash);
                  }
                  else 
                  {
                    error = true;
                    log.Error("Failed to save profile image data to disk.");
                  }
                }
                // else Profile image will be deleted.
              }

              byte[] newThumbnailImageHash = profile.ThumbnailImageHash.Length != 0 ? profile.ThumbnailImageHash.ToByteArray() : null;
              bool thumbnailImageChanged = !ByteArrayComparer.Equals(identityForValidation.ThumbnailImage, newThumbnailImageHash);
              if (!error && thumbnailImageChanged)
              {
                if (newThumbnailImageHash != null)
                {
                  byte[] thumbnailImage = updateProfileRequest.ThumbnailImage.ToByteArray();

                  identityForValidation.ThumbnailImage = newThumbnailImageHash;
                  if (await identityForValidation.SaveThumbnailImageDataAsync(thumbnailImage))
                  {
                    // In case we of an error, we have to delete this newly saved image.
                    newlyCreatedImages.Add(newThumbnailImageHash);
                  }
                  else 
                  {
                    error = true;
                    log.Error("Failed to save thumbnail image data to disk.");
                  }
                }
                // else Thumbnail image will be deleted.
              }

              if (!error)
              {
                StrongBox<bool> notFound = new StrongBox<bool>(false);
                SignedProfileInformation signedProfile = new SignedProfileInformation()
                {
                  Profile = profile,
                  Signature = RequestMessage.Message.Request.ConversationRequest.Signature
                };
                if (await unitOfWork.HostedIdentityRepository.UpdateProfileAndPropagateAsync(Client.IdentityId, signedProfile, profileImageChanged, thumbnailImageChanged, updateProfileRequest.NoPropagation, notFound, imagesToDelete))
                {
                  log.Debug("Identity '{0}' updated its profile in the database.", Client.IdentityId.ToHex());
                  res = messageBuilder.CreateUpdateProfileResponse(RequestMessage);
                }
                else if (notFound.Value == true)
                {
                  log.Debug("Identity '{0}' is not a client of this profile server.", Client.IdentityId.ToHex());
                  res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
                }
              }

              if (res.Message.Response.Status != Status.Ok)
                imagesToDelete.AddRange(newlyCreatedImages);

              // Delete old/unused image files, if there are any.
              ImageManager imageManager = (ImageManager)Base.ComponentDictionary[ImageManager.ComponentName];
              foreach (byte[] imageHash in imagesToDelete)
                imageManager.RemoveImageReference(imageHash);
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<IProtocolMessage<Message>> ProcessMessageCancelHostingAgreementRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      CancelHostingAgreementRequest cancelHostingAgreementRequest = RequestMessage.Message.Request.ConversationRequest.CancelHostingAgreement;

      if (!cancelHostingAgreementRequest.RedirectToNewProfileServer || (cancelHostingAgreementRequest.NewProfileServerNetworkId.Length == ProtocolHelper.NetworkIdentifierLength))
      {
        res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          bool signalNeighborhoodAction = false;

          StrongBox<bool> notFound = new StrongBox<bool>(false);
          List<byte[]> imagesToDelete = new List<byte[]>();
          if (await unitOfWork.HostedIdentityRepository.CancelProfileAndPropagateAsync(Client.IdentityId, cancelHostingAgreementRequest, notFound, imagesToDelete))
          {
            res = messageBuilder.CreateCancelHostingAgreementResponse(RequestMessage);

            // Send signal to neighborhood action processor to process the new series of actions.
            if (signalNeighborhoodAction)
            {
              NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary[NeighborhoodActionProcessor.ComponentName];
              neighborhoodActionProcessor.Signal();
            }

            // Delete old files, if there are any.
            ImageManager imageManager = (ImageManager)Base.ComponentDictionary[ImageManager.ComponentName];
            foreach (byte[] imageHash in imagesToDelete)
              imageManager.RemoveImageReference(imageHash);
          }
          else if (notFound.Value == true)
          {
            log.Debug("Identity '{0}' is not a client of this profile server.", Client.IdentityId.ToHex());
            res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          }

        }
      }
      else
      {
        log.Debug("Invalid profile server identifier '{0}'.", cancelHostingAgreementRequest.NewProfileServerNetworkId.ToByteArray().ToHex());
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "newProfileServerNetworkId");
      }

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes ApplicationServiceAddRequest message from client.
    /// <para>Adds one or more application services to the list of available services of a customer client.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public IProtocolMessage<Message> ProcessMessageApplicationServiceAddRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      ApplicationServiceAddRequest applicationServiceAddRequest = RequestMessage.Message.Request.ConversationRequest.ApplicationServiceAdd;

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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }





    /// <summary>
    /// Processes ApplicationServiceRemoveRequest message from client.
    /// <para>Removes an application service from the list of available services of a customer client.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public IProtocolMessage<Message> ProcessMessageApplicationServiceRemoveRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      ApplicationServiceRemoveRequest applicationServiceRemoveRequest = RequestMessage.Message.Request.ConversationRequest.ApplicationServiceRemove;

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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<IProtocolMessage<Message>> ProcessMessageCallIdentityApplicationServiceRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      CallIdentityApplicationServiceRequest callIdentityApplicationServiceRequest = RequestMessage.Message.Request.ConversationRequest.CallIdentityApplicationService;

      byte[] calleeIdentityId = callIdentityApplicationServiceRequest.IdentityNetworkId.ToByteArray();
      string serviceName = callIdentityApplicationServiceRequest.ServiceName;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        try
        {
          HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.IdentityId == calleeIdentityId))).FirstOrDefault();
          if (identity != null)
          {
            if (!identity.Initialized)
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
        IncomingClient callee = (IncomingClient)clientList.GetAuthenticatedOnlineClient(calleeIdentityId);
        if (callee != null)
        {
          // The callee is hosted on this profile server, it is online and its profile is initialized.
          if (callee.ApplicationServices.ContainsService(serviceName))
          {
            // All OK, create network relay and inform callee.
            RelayConnection relay = relayList.CreateNetworkRelay(Client, callee, serviceName, RequestMessage);
            if (relay != null)
            {
              bool error = false;
              var notificationMessage = callee.MessageBuilder.CreateIncomingCallNotificationRequest(Client.PublicKey, serviceName, relay.GetCalleeToken().ToByteArray());
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

              if (error) await relayList.DestroyNetworkRelay(relay);
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

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<bool> ProcessMessageIncomingCallNotificationResponseAsync(IncomingClient Client, IProtocolMessage<Message> ResponseMessage, UnfinishedRequest<Message> Request)
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
    public async Task<IProtocolMessage<Message>> ProcessMessageApplicationServiceSendMessageRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientAppService, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      ApplicationServiceSendMessageRequest applicationServiceSendMessageRequest = RequestMessage.Message.Request.SingleRequest.ApplicationServiceSendMessage;

      byte[] tokenBytes = applicationServiceSendMessageRequest.Token.ToByteArray();
      if (tokenBytes.Length == 16)
      {
        Guid token = new Guid(tokenBytes);
        RelayConnection relay = relayList.GetRelayByGuid(token);
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

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<bool> ProcessMessageApplicationServiceReceiveMessageNotificationResponseAsync(IncomingClient Client, IProtocolMessage<Message> ResponseMessage, UnfinishedRequest<Message> Request)
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
    public async Task<IProtocolMessage<Message>> ProcessMessageProfileStatsRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientNonCustomer | ServerRole.ClientCustomer, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      ProfileStatsRequest profileStatsRequest = RequestMessage.Message.Request.SingleRequest.ProfileStats;

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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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

    /// <summary>Maximum number of results the profile server can store in total for a single client if images are included.</summary>
    public const int ProfileSearchMaxTotalRecordsWithImage = 1000;

    /// <summary>Maximum number of results the profile server can store in total for a single client if images are not included.</summary>
    public const int ProfileSearchMaxTotalRecordsWithoutImage = 10000;


    /// <summary>
    /// Processes ProfileSearchRequest message from client.
    /// <para>Performs a search operation to find all matching identities that this profile server hosts, 
    /// possibly including identities hosted in the profile server's neighborhood.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageProfileSearchRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationAny, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      ProfileSearchRequest profileSearchRequest = RequestMessage.Message.Request.SingleRequest.ProfileSearch;
      if (profileSearchRequest == null) profileSearchRequest = new ProfileSearchRequest();

      IProtocolMessage<Message> errorResponse;
      if (InputValidators.ValidateProfileSearchRequest(profileSearchRequest, messageBuilder, RequestMessage, out errorResponse))
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
            GpsLocation locationFilter = profileSearchRequest.Latitude != GpsLocation.NoLocationLocationType ? new GpsLocation(profileSearchRequest.Latitude, profileSearchRequest.Longitude) : null;
            uint radiusFilter = profileSearchRequest.Radius;
            string extraDataFilter = profileSearchRequest.ExtraData;
            bool includeImages = profileSearchRequest.IncludeThumbnailImages;

            watch.Start();

            // First, we try to find enough results among identities hosted on this profile server.
            List<ProfileQueryInformation> searchResultsNeighborhood = new List<ProfileQueryInformation>();
            List<ProfileQueryInformation> searchResultsLocal = await ProfileSearchAsync(unitOfWork.HostedIdentityRepository, maxResults, typeFilter, nameFilter, locationFilter, radiusFilter, extraDataFilter, includeImages, watch);
            if (searchResultsLocal != null)
            {
              bool localServerOnly = true;
              bool error = false;
              // If possible and needed we try to find more results among identities hosted in this profile server's neighborhood.
              if (!profileSearchRequest.IncludeHostedOnly && (searchResultsLocal.Count < maxResults))
              {
                localServerOnly = false;
                maxResults -= (uint)searchResultsLocal.Count;
                searchResultsNeighborhood = await ProfileSearchAsync(unitOfWork.NeighborIdentityRepository, maxResults, typeFilter, nameFilter, locationFilter, radiusFilter, extraDataFilter, includeImages, watch);
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
                List<ProfileQueryInformation> allResults = searchResultsLocal;
                allResults.AddRange(searchResultsNeighborhood);
                List<ProfileQueryInformation> responseResults = allResults;
                log.Debug("Total number of matching profiles is {0}, from which {1} are local, {2} are from neighbors.", allResults.Count, searchResultsLocal.Count, searchResultsNeighborhood.Count);
                if (maxResponseResults < allResults.Count)
                {
                  log.Trace("All results can not fit into a single response (max {0} results).", maxResponseResults);
                  // We can not send all results, save them to session.
                  Client.SaveProfileSearchResults(allResults, includeImages);

                  // And send the maximum we can in the response.
                  responseResults = new List<ProfileQueryInformation>();
                  responseResults.AddRange(allResults.GetRange(0, (int)maxResponseResults));
                }

                List<byte[]> coveredServers = await ProfileSearchGetCoveredServersAsync(unitOfWork, localServerOnly);
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
      }
      else res = errorResponse;

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    private async Task<List<byte[]>> ProfileSearchGetCoveredServersAsync(UnitOfWork UnitOfWork, bool LocalServerOnly)
    {
      log.Trace("()");

      List<byte[]> res = new List<byte[]>();
      res.Add(serverComponent.ServerId);
      if (!LocalServerOnly)
      {
        List<byte[]> neighborIds = (await UnitOfWork.NeighborRepository.GetAsync(null, null, true)).Select(n => n.NetworkId).ToList();
        res.AddRange(neighborIds);
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Performs a search request on a repository to retrieve the list of profiles that match specific criteria.
    /// </summary>
    /// <param name="Repository">Home or neighborhood identity repository, which is queried.</param>
    /// <param name="MaxResults">Maximum number of results to retrieve.</param>
    /// <param name="TypeFilter">Wildcard filter for identity type, or empty string if identity type filtering is not required.</param>
    /// <param name="NameFilter">Wildcard filter for profile name, or empty string if profile name filtering is not required.</param>
    /// <param name="LocationFilter">If not null, this value together with <paramref name="RadiusFilter"/> provide specification of target area, in which the identities has to have their location set. If null, GPS location filtering is not required.</param>
    /// <param name="RadiusFilter">If <paramref name="LocationFilter"/> is not null, this is the target area radius with the centre in <paramref name="LocationFilter"/>.</param>
    /// <param name="ExtraDataFilter">Regular expression filter for identity's extraData information, or empty string if extraData filtering is not required.</param>
    /// <param name="IncludeImages">If true, the results will include profiles' thumbnail images.</param>
    /// <param name="TimeoutWatch">Stopwatch instance that is used to terminate the search query in case the execution takes too long. The stopwatch has to be started by the caller before calling this method.</param>
    /// <returns>List of network profile informations of identities that match the specific criteria.</returns>
    /// <remarks>In order to prevent DoS attacks, we require the search to complete within small period of time. 
    /// One the allowed time is up, the search is terminated even if we do not have enough results yet and there 
    /// is still a possibility to get more.</remarks>
    private async Task<List<ProfileQueryInformation>> ProfileSearchAsync<T>(IdentityRepositoryBase<T> Repository, uint MaxResults, string TypeFilter, string NameFilter, GpsLocation LocationFilter, uint RadiusFilter, string ExtraDataFilter, bool IncludeImages, Stopwatch TimeoutWatch) where T : IdentityBase
    {
      log.Trace("(Repository:{0},MaxResults:{1},TypeFilter:'{2}',NameFilter:'{3}',LocationFilter:[{4}],RadiusFilter:{5},ExtraDataFilter:'{6}',IncludeImages:{7})",
        Repository, MaxResults, TypeFilter, NameFilter, LocationFilter, RadiusFilter, ExtraDataFilter, IncludeImages);

      List<ProfileQueryInformation> res = new List<ProfileQueryInformation>();

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
          identities = await Repository.ProfileSearchAsync(offset, batchSize, TypeFilter, NameFilter, LocationFilter, RadiusFilter);
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

              // Filter out profiles that do not match exact location filter and extraData filter.
              GpsLocation identityLocation = new GpsLocation(identity.InitialLocationLatitude, identity.InitialLocationLongitude);
              if (LocationFilter != null)
              {
                double distance = GpsLocation.DistanceBetween(LocationFilter, identityLocation);
                bool withinArea = distance <= (double)RadiusFilter;
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
              ProfileQueryInformation profileQueryInformation = new ProfileQueryInformation();
              if (Repository is HostedIdentityRepository)
              {
                profileQueryInformation.IsHosted = true;
                profileQueryInformation.IsOnline = clientList.IsIdentityOnlineAuthenticated(identity.IdentityId);
              }
              else
              {
                profileQueryInformation.IsHosted = false;
                profileQueryInformation.HostingServerNetworkId = ProtocolHelper.ByteArrayToByteString(identity.HostingServerId);
              }

              
              profileQueryInformation.SignedProfile = identity.ToSignedProfileInformation();
              if (IncludeImages)
              {
                profileQueryInformation.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[0]);
                if (identity.ThumbnailImage != null)
                {
                  // Profile has thumbnail image.
                  // If we fail to load it, we still can return the result 
                  // and the client will know that the profile does have a thumbnail image.
                  byte[] image = await identity.GetThumbnailImageDataAsync();
                  if (image != null) profileQueryInformation.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(image);
                  else log.Error("Unable to load thumbnail image for identity ID '{0}'.", identity.IdentityId.ToHex());
                }
              }

              res.Add(profileQueryInformation);
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
    public IProtocolMessage<Message> ProcessMessageProfileSearchPartRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, ClientConversationStatus.ConversationAny, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      ProfileSearchPartRequest profileSearchPartRequest = RequestMessage.Message.Request.SingleRequest.ProfileSearchPart;

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
          List<ProfileQueryInformation> cachedResults = Client.GetProfileSearchResults((int)profileSearchPartRequest.RecordIndex, (int)profileSearchPartRequest.RecordCount);
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes AddRelatedIdentityRequest message from client.
    /// <para>Adds a proven relationship between an identity and the client to the list of client's related identities.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageAddRelatedIdentityRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      AddRelatedIdentityRequest addRelatedIdentityRequest = RequestMessage.Message.Request.ConversationRequest.AddRelatedIdentity;

      IProtocolMessage<Message> errorResponse;
      if (InputValidators.ValidateAddRelatedIdentityRequest(Client, addRelatedIdentityRequest, messageBuilder, RequestMessage, out errorResponse))
      {
        CardApplicationInformation application = addRelatedIdentityRequest.CardApplication;
        SignedRelationshipCard signedCard = addRelatedIdentityRequest.SignedCard;
        RelationshipCard card = signedCard.Card;
        byte[] issuerSignature = signedCard.IssuerSignature.ToByteArray();
        byte[] recipientSignature = RequestMessage.Message.Request.ConversationRequest.Signature.ToByteArray();
        byte[] cardId = card.CardId.ToByteArray();
        byte[] cardVersion = card.Version.ToByteArray();
        byte[] applicationId = application.ApplicationId.ToByteArray();
        string cardType = card.Type;
        DateTime? validFrom = ProtocolHelper.UnixTimestampMsToDateTime(card.ValidFrom);
        DateTime? validTo = ProtocolHelper.UnixTimestampMsToDateTime(card.ValidTo);
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
          ValidFrom = validFrom.Value,
          ValidTo = validTo.Value
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
              if (count < Config.Configuration.MaxIdenityRelations)
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }




    /// <summary>
    /// Processes RemoveRelatedIdentityRequest message from client.
    /// <para>Remove related identity from the list of client's related identities.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageRemoveRelatedIdentityRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      RemoveRelatedIdentityRequest removeRelatedIdentityRequest = RequestMessage.Message.Request.ConversationRequest.RemoveRelatedIdentity;
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes GetIdentityRelationshipsInformationRequest message from client.
    /// <para>Obtains a list of related identities of that match given criteria.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageGetIdentityRelationshipsInformationRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer | ServerRole.ClientNonCustomer, null, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      GetIdentityRelationshipsInformationRequest getIdentityRelationshipsInformationRequest = RequestMessage.Message.Request.SingleRequest.GetIdentityRelationshipsInformation;
      byte[] identityId = getIdentityRelationshipsInformationRequest.IdentityNetworkId.ToByteArray();
      bool includeInvalid = getIdentityRelationshipsInformationRequest.IncludeInvalid;
      string type = getIdentityRelationshipsInformationRequest.Type;
      bool specificIssuer = getIdentityRelationshipsInformationRequest.SpecificIssuer;
      byte[] issuerId = specificIssuer ? getIdentityRelationshipsInformationRequest.IssuerNetworkId.ToByteArray() : null;

      if (Encoding.UTF8.GetByteCount(type) <= PsMessageBuilder.MaxGetIdentityRelationshipsTypeLengthBytes)
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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<IProtocolMessage<Message>> ProcessMessageStartNeighborhoodInitializationRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }


      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      StartNeighborhoodInitializationRequest startNeighborhoodInitializationRequest = RequestMessage.Message.Request.ConversationRequest.StartNeighborhoodInitialization;
      if (startNeighborhoodInitializationRequest == null) startNeighborhoodInitializationRequest = new StartNeighborhoodInitializationRequest();

      int primaryPort = (int)startNeighborhoodInitializationRequest.PrimaryPort;
      int srNeighborPort = (int)startNeighborhoodInitializationRequest.SrNeighborPort;
      byte[] ipAddressBytes = startNeighborhoodInitializationRequest.IpAddress.ToByteArray();
      byte[] followerId = Client.IdentityId;

      IPAddress followerIpAddress = IPAddressExtensions.IpFromBytes(ipAddressBytes);

      res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      bool success = false;

      NeighborhoodInitializationProcessContext nipContext = null;

      Config config = (Config)Base.ComponentDictionary[ConfigBase.ComponentName];
      bool primaryPortValid = (0 < primaryPort) && (primaryPort <= 65535);
      bool srNeighborPortValid = (0 < srNeighborPort) && (srNeighborPort <= 65535);
      
      bool ipAddressValid = (followerIpAddress != null) && (config.TestModeEnabled || !followerIpAddress.IsReservedOrLocal());

      if (primaryPortValid && srNeighborPortValid && ipAddressValid)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          int blockActionId = await unitOfWork.NeighborhoodActionRepository.InstallInitializationProcessInProgressAsync(followerId);
          if (blockActionId != -1)
          {
            DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.HostedIdentityLock, UnitOfWork.FollowerLock };
            using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
            {
              try
              {
                int followerCount = await unitOfWork.FollowerRepository.CountAsync();
                if (followerCount < Config.Configuration.MaxFollowerServersCount)
                {
                  int neighborhoodInitializationsInProgress = await unitOfWork.FollowerRepository.CountAsync(f => f.Initialized == false);
                  if (neighborhoodInitializationsInProgress < Config.Configuration.NeighborhoodInitializationParallelism)
                  {
                    Follower existingFollower = (await unitOfWork.FollowerRepository.GetAsync(f => f.NetworkId == followerId)).FirstOrDefault();
                    if (existingFollower == null)
                    {
                      // Take snapshot of all our identities that have valid contracts.
                      List<HostedIdentity> allHostedIdentities = (await unitOfWork.HostedIdentityRepository.GetAsync(i => (i.Initialized == true) && (i.Cancelled == false), null, true)).ToList();

                      // Create new follower.
                      Follower follower = new Follower()
                      {
                        NetworkId = followerId,
                        IpAddress = followerIpAddress.GetAddressBytes(),
                        PrimaryPort = primaryPort,
                        SrNeighborPort = srNeighborPort,
                        LastRefreshTime = DateTime.UtcNow,
                        Initialized = false
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
                    log.Warn("Maximal number of neighborhood initialization processes {0} in progress has been reached.", Config.Configuration.NeighborhoodInitializationParallelism);
                    res = messageBuilder.CreateErrorBusyResponse(RequestMessage);
                  }
                }
                else
                {
                  log.Warn("Maximal number of follower servers {0} has been reached already. Will not accept another follower.", Config.Configuration.MaxFollowerServersCount);
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
              if (!await unitOfWork.NeighborhoodActionRepository.UninstallInitializationProcessInProgressAsync(blockActionId))
                log.Error("Unable to uninstall blocking neighborhood action ID {0} for follower ID '{1}'.", blockActionId, followerId.ToHex());
            }
          }
          else log.Error("Unable to install blocking neighborhood action for follower ID '{0}'.", followerId.ToHex());
        }
      }
      else
      {
        if (!primaryPortValid) res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "primaryPort");
        else if (!srNeighborPortValid) res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "srNeighborPort");
        else res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "ipAddress");
      }

      if (success)
      {
        log.Info("New follower ID '{0}' added to the database.", followerId.ToHex());

        var responseMessage = messageBuilder.CreateStartNeighborhoodInitializationResponse(RequestMessage);
        if (await Client.SendMessageAsync(responseMessage))
        {
          if (nipContext.HostedIdentities.Count > 0)
          {
            log.Trace("Sending first batch of our {0} hosted identities.", nipContext.HostedIdentities.Count);
            var updateMessage = await BuildNeighborhoodSharedProfileUpdateRequestAsync(Client, nipContext);
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
            var finishMessage = messageBuilder.CreateFinishNeighborhoodInitializationRequest();
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

      if (res != null) log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      else log.Trace("(-):null");
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
    public async Task<IProtocolMessage<Message>> BuildNeighborhoodSharedProfileUpdateRequestAsync(IncomingClient Client, NeighborhoodInitializationProcessContext Context)
    {
      log.Trace("()");

      var res = Client.MessageBuilder.CreateNeighborhoodSharedProfileUpdateRequest();

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
            SignedProfile = identity.ToSignedProfileInformation(),
            ThumbnailImage = thumbnailImage != null ? ProtocolHelper.ByteArrayToByteString(thumbnailImage) : ProtocolHelper.ByteArrayToByteString(new byte[0])
          }
        };

        res.Message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Add(updateItem);
        int newSize = res.Message.CalculateSize();

        log.Trace("Index {0}, message size is {1} bytes, limit is {2} bytes.", index, newSize, messageSizeLimit);
        if (newSize > messageSizeLimit)
        {
          // We have reached the limit, remove the last item and send the message.
          res.Message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.RemoveAt(res.Message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Count - 1);
          break;
        }

        index++;
      }

      Context.IdentitiesDone += res.Message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Count;
      log.Debug("{0} update items inserted to the message. Already processed {1}/{2} profiles.", 
        res.Message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items.Count, Context.IdentitiesDone, Context.HostedIdentities.Count);

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
    public IProtocolMessage<Message> ProcessMessageFinishNeighborhoodInitializationRequest(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes NeighborhoodSharedProfileUpdateRequest message from client.
    /// <para>Processes a shared profile update from a neighbor.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }


      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      NeighborhoodSharedProfileUpdateRequest neighborhoodSharedProfileUpdateRequest = RequestMessage.Message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate;
      if (neighborhoodSharedProfileUpdateRequest == null) neighborhoodSharedProfileUpdateRequest = new NeighborhoodSharedProfileUpdateRequest();

      bool error = false;
      byte[] neighborId = Client.IdentityId;

      int sharedProfilesCount = 0;
      // First, we verify that the client is our neighbor and how many profiles it shares with us.
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NetworkId == neighborId)).FirstOrDefault();
        if ((neighbor != null) && neighbor.Initialized)
        {
          sharedProfilesCount = neighbor.SharedProfiles;
          log.Trace("Neighbor ID '{0}' currently shares {1} profiles with the profile server.", neighborId.ToHex(), sharedProfilesCount);
        }
        else if (neighbor == null)
        {
          log.Warn("Share profile update request came from client ID '{0}', who is not our neighbor.", neighborId.ToHex());
          res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
          error = true;
        }
        else 
        {
          log.Warn("Share profile update request came from client ID '{0}', who is our neighbor, but we have not finished the initialization process with it yet.", neighborId.ToHex());
          res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
          error = true;
        }
      }

      if (error)
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
        IProtocolMessage<Message> errorResponse;
        if (InputValidators.ValidateSharedProfileUpdateItem(updateItem, itemIndex, sharedProfilesCount, usedProfileIdsInBatch, Client.MessageBuilder, RequestMessage, out errorResponse))
        {
          // Modify sharedProfilesCount to reflect the item we just validated.
          // In case of delete operation, we have not checked the existence yet, 
          // but it will be checked prior any problem could be caused by that.
          if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add) sharedProfilesCount++;
          else if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Delete) sharedProfilesCount--;

          // Is new image being transferred?
          byte[] newImageData = null;
          if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add) newImageData = updateItem.Add.ThumbnailImage.ToByteArray();
          else if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Change) newImageData = updateItem.Change.ThumbnailImage.ToByteArray();
          if ((newImageData != null) && (newImageData.Length == 0)) newImageData = null;

          if (newImageData != null)
          {
            byte[] imageHash = Crypto.Sha256(newImageData);
            if (!await ImageManager.SaveImageDataAsync(imageHash, newImageData))
            {
              log.Error("Unable to save image data from item index {0} to file.", itemIndex);
              res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
              error = true;
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


      // imagesToDelete is a list of image hashes that were replaced and the corresponding image files should be deleted
      // or image hashes of the new images that were not saved to the database.
      List<byte[]> imagesToDelete = new List<byte[]>();

      if (!error)
      {
        log.Debug("{0}/{1} update items passed validation, doRefresh is {2}.", itemIndex, neighborhoodSharedProfileUpdateRequest.Items.Count, doRefresh);


        // Now we save all valid items up to the first invalid (or all if all are valid).
        // But if we detect duplicity of identity with Add operation, or we can not find identity 
        // with Change or Delete action, we end earlier.
        // We will process the data in batches of max 100 items, not to occupy the database locks for too long.
        log.Trace("Saving {0} valid profiles changes.", itemIndex);

        // Index of the update item currently being processed.
        int index = 0;

        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          // If there was a refresh request, we process it first as it does no harm and we do not need to care about it later.
          if (doRefresh)
            await unitOfWork.NeighborRepository.UpdateLastRefreshTimeAsync(neighborId);


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
              bool dbSuccess = false;
              bool saveDb = false;
              try
              {
                Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NetworkId == neighborId)).FirstOrDefault();
                if (neighbor != null)
                {
                  int oldSharedProfilesCount = neighbor.SharedProfiles;
                  for (int loopIndex = 0; loopIndex < 100; loopIndex++)
                  {
                    SharedProfileUpdateItem updateItem = neighborhoodSharedProfileUpdateRequest.Items[index];

                    StoreSharedProfileUpdateResult storeResult = await StoreSharedProfileUpdateToDatabaseAsync(unitOfWork, updateItem, index, neighbor, messageBuilder, RequestMessage);
                    if (storeResult.Error)
                    {
                      // Error here means that we want to save all already processed items to the database
                      // and quit the loop right after that, the response is filled with error response already.
                      res = storeResult.ErrorResponse;
                      error = true;
                      break;
                    }

                    if (storeResult.SaveDb) saveDb = true;
                    if (storeResult.ImageToDelete != null) batchDeletedImageHashes.Add(storeResult.ImageToDelete);
                    if (storeResult.ItemImageUsed) batchUsedImageItemIndexes.Add(index);

                    index++;
                    if (index >= itemIndex) break;
                  }

                  if (oldSharedProfilesCount != neighbor.SharedProfiles)
                  {
                    unitOfWork.NeighborRepository.Update(neighbor);
                    saveDb = true;
                  }

                  if (saveDb)
                  {
                    await unitOfWork.SaveThrowAsync();
                    transaction.Commit();
                  }
                  dbSuccess = true;
                }
                else
                {
                  log.Error("Unable to find neighbor ID '{0}', sending ERROR_REJECTED response.", neighborId.ToHex());
                  res = messageBuilder.CreateErrorRejectedResponse(RequestMessage);
                  error = true;
                }
              }
              catch (Exception e)
              {
                log.Error("Exception occurred: {0}", e.ToString());
                res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
                error = true;
              }

              if (dbSuccess)
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
      }

      // We now extend the list of images to delete with images of all profiles that were not saved to the database.
      // And then we delete all the image files that are not referenced from DB.
      foreach (byte[] hash in itemsImageHashes.Values)
          imagesToDelete.Add(hash);

      ImageManager imageManager = (ImageManager)Base.ComponentDictionary[ImageManager.ComponentName];
      foreach (byte[] hash in imagesToDelete)
        imageManager.RemoveImageReference(hash);


      if (res == null) res = messageBuilder.CreateNeighborhoodSharedProfileUpdateResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
      public IProtocolMessage<Message> ErrorResponse;

      /// <summary>If any image was replaced and the file on disk should be deleted, this is set to its hash.</summary>
      public byte[] ImageToDelete;

      /// <summary>True if the image of the item was used, false otherwise.</summary>
      public bool ItemImageUsed;
    }


    /// <summary>
    /// Updates a database according to the update item that is already partially validated.
    /// </summary>
    /// <param name="UnitOfWork">Instance of unit of work.</param>
    /// <param name="UpdateItem">Update item that is to be processed.</param>
    /// <param name="UpdateItemIndex">Index of the item within the request.</param>
    /// <param name="Neighbor">Identifier of the neighbor that sent the request.</param>
    /// <param name="MessageBuilder">Neighbor client's message builder.</param>
    /// <param name="RequestMessage">Original request message sent by the neighbor.</param>
    /// <returns>Result described by StoreSharedProfileUpdateResult class.</returns>
    /// <remarks>The caller of this function is responsible to call this function within a database transaction with acquired NeighborIdentityLock.</remarks>
    private async Task<StoreSharedProfileUpdateResult> StoreSharedProfileUpdateToDatabaseAsync(UnitOfWork UnitOfWork, SharedProfileUpdateItem UpdateItem, int UpdateItemIndex, Neighbor Neighbor, PsMessageBuilder MessageBuilder, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("(UpdateItemIndex:{0},Neighbor.SharedProfiles:{1})", UpdateItemIndex, Neighbor.SharedProfiles);

      StoreSharedProfileUpdateResult res = new StoreSharedProfileUpdateResult()
      {
        SaveDb = false,
        Error = false,
        ErrorResponse = null,
        ImageToDelete = null,
        ItemImageUsed = false,
      };

      try
      {
        switch (UpdateItem.ActionTypeCase)
        {
          case SharedProfileUpdateItem.ActionTypeOneofCase.Add:
            {
              if (Neighbor.SharedProfiles >= IdentityBase.MaxHostedIdentities)
              {
                log.Warn("Neighbor ID '{0}' already shares the maximum number of profiles.", Neighbor.NetworkId.ToHex());
                res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".add");
                res.Error = true;
                break;
              }

              SharedProfileAddItem addItem = UpdateItem.Add;
              byte[] pubKey = addItem.SignedProfile.Profile.PublicKey.ToByteArray();
              byte[] identityId = Crypto.Sha256(pubKey);

              // Identity already exists if there exists a NeighborIdentity with same identity ID and the same hosting server ID.
              NeighborIdentity existingIdentity = (await UnitOfWork.NeighborIdentityRepository.GetAsync(i => (i.IdentityId == identityId) && (i.HostingServerId == Neighbor.NetworkId))).FirstOrDefault();
              if (existingIdentity == null)
              {
                res.ItemImageUsed = addItem.SignedProfile.Profile.ThumbnailImageHash.Length != 0;

                NeighborIdentity newIdentity = NeighborIdentity.FromSignedProfileInformation(addItem.SignedProfile, Neighbor.NetworkId);
                await UnitOfWork.NeighborIdentityRepository.InsertAsync(newIdentity);

                Neighbor.SharedProfiles++;

                res.SaveDb = true;
              }
              else
              {
                log.Warn("Identity ID '{0}' already exists with hosting server ID '{1}'.", identityId.ToHex(), Neighbor.NetworkId.ToHex());
                res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".add.signedProfile.profile.publicKey");
                res.Error = true;
              }

              break;
            }

          case SharedProfileUpdateItem.ActionTypeOneofCase.Change:
            {
              SharedProfileChangeItem changeItem = UpdateItem.Change;
              byte[] pubKey = changeItem.SignedProfile.Profile.PublicKey.ToByteArray();
              byte[] identityId = Crypto.Sha256(pubKey);

              // Identity already exists if there exists a NeighborIdentity with same identity ID and the same hosting server ID.
              NeighborIdentity existingIdentity = (await UnitOfWork.NeighborIdentityRepository.GetAsync(i => (i.IdentityId == identityId) && (i.HostingServerId == Neighbor.NetworkId))).FirstOrDefault();
              if (existingIdentity != null)
              {
                // Changing type is not allowed.
                if (existingIdentity.Type != changeItem.SignedProfile.Profile.Type)
                {
                  log.Debug("Attempt to change profile type.");
                  log.Warn("Neighbor ID '{0}' already shares the maximum number of profiles.", Neighbor.NetworkId.ToHex());
                  res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".change.signedProfile.profile.type");
                  res.Error = true;
                  break;
                }

                res.ImageToDelete = existingIdentity.ThumbnailImage;
                res.ItemImageUsed = changeItem.SignedProfile.Profile.ThumbnailImageHash.Length != 0;

                existingIdentity.CopyFromSignedProfileInformation(changeItem.SignedProfile, Neighbor.NetworkId);

                UnitOfWork.NeighborIdentityRepository.Update(existingIdentity);
                res.SaveDb = true;
              }
              else
              {
                log.Warn("Identity ID '{0}' does not exist with hosting server ID '{1}'.", identityId.ToHex(), Neighbor.NetworkId.ToHex());
                res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".change.signedProfile.profile.publicKey");
                res.Error = true;
              }

              break;
            }

          case SharedProfileUpdateItem.ActionTypeOneofCase.Delete:
            {
              SharedProfileDeleteItem deleteItem = UpdateItem.Delete;
              byte[] identityId = deleteItem.IdentityNetworkId.ToByteArray();

              // Identity already exists if there exists a NeighborIdentity with same identity ID and the same hosting server ID.
              NeighborIdentity existingIdentity = (await UnitOfWork.NeighborIdentityRepository.GetAsync(i => (i.IdentityId == identityId) && (i.HostingServerId == Neighbor.NetworkId))).FirstOrDefault();
              if (existingIdentity != null)
              {
                res.ImageToDelete = existingIdentity.ThumbnailImage;

                UnitOfWork.NeighborIdentityRepository.Delete(existingIdentity);
                Neighbor.SharedProfiles--;
                res.SaveDb = true;
              }
              else
              {
                log.Warn("Identity ID '{0}' does exists with hosting server ID '{1}'.", identityId.ToHex(), Neighbor.NetworkId.ToHex());
                res.ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, UpdateItemIndex + ".delete.identityNetworkId");
                res.Error = true;
              }
              break;
            }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
        res.ErrorResponse = MessageBuilder.CreateErrorInternalResponse(RequestMessage);
        res.Error = true;
      }

      log.Trace("(-):*.Error={0},*.SaveDb={1},*.ImageToDelete='{2}',ItemImageUsed={3}", res.Error, res.SaveDb, res.ImageToDelete != null ? res.ImageToDelete.ToHex() : "null", res.ItemImageUsed);
      return res;
    }




    /// <summary>
    /// Processes StopNeighborhoodUpdatesRequest message from client.
    /// <para>Removes follower server from the database and also removes all pending actions to the follower.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageStopNeighborhoodUpdatesRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ServerNeighbor, ClientConversationStatus.Verified, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      StopNeighborhoodUpdatesRequest stopNeighborhoodUpdatesRequest = RequestMessage.Message.Request.ConversationRequest.StopNeighborhoodUpdates;
      if (stopNeighborhoodUpdatesRequest == null) stopNeighborhoodUpdatesRequest = new StopNeighborhoodUpdatesRequest();

      byte[] followerId = Client.IdentityId;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        Status status = await unitOfWork.FollowerRepository.DeleteFollowerAsync(followerId);

        if (status == Status.Ok) res = messageBuilder.CreateStopNeighborhoodUpdatesResponse(RequestMessage);
        else if (status == Status.ErrorNotFound) res = messageBuilder.CreateErrorNotFoundResponse(RequestMessage);
        else res = messageBuilder.CreateErrorInternalResponse(RequestMessage);
      }


      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
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
    public async Task<bool> ProcessMessageNeighborhoodSharedProfileUpdateResponseAsync(IncomingClient Client, IProtocolMessage<Message> ResponseMessage, UnfinishedRequest<Message> Request)
    {
      log.Trace("()");

      bool res = false;

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      if (Client.NeighborhoodInitializationProcessInProgress)
      {
        if (ResponseMessage.Message.Response.Status == Status.Ok)
        {
          NeighborhoodInitializationProcessContext nipContext = (NeighborhoodInitializationProcessContext)Request.Context;
          if (nipContext.IdentitiesDone < nipContext.HostedIdentities.Count)
          {
            var updateMessage = await BuildNeighborhoodSharedProfileUpdateRequestAsync(Client, nipContext);
            if (await Client.SendMessageAndSaveUnfinishedRequestAsync(updateMessage, nipContext))
            {
              res = true;
            }
            else log.Warn("Unable to send update message to the client.");
          }
          else
          {
            // If all hosted identities were sent, finish initialization process.
            var finishMessage = messageBuilder.CreateFinishNeighborhoodInitializationRequest();
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
          log.Warn("Client ID '{0}' is follower in the middle of the neighborhood initialization process, but it did not accept our profiles (error code {1}), so we will disconnect it and delete it from our database.", Client.IdentityId.ToHex(), ResponseMessage.Message.Response.Status);
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
    public async Task<bool> ProcessMessageFinishNeighborhoodInitializationResponseAsync(IncomingClient Client, IProtocolMessage<Message> ResponseMessage, UnfinishedRequest<Message> Request)
    {
      log.Trace("()");

      bool res = false;

      if (Client.NeighborhoodInitializationProcessInProgress)
      {
        if (ResponseMessage.Message.Response.Status == Status.Ok)
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
                Follower follower = (await unitOfWork.FollowerRepository.GetAsync(f => f.NetworkId == followerId)).FirstOrDefault();
                if (follower != null)
                {
                  follower.LastRefreshTime = DateTime.UtcNow;
                  follower.Initialized = true;
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
                  NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary[NeighborhoodActionProcessor.ComponentName];
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
          log.Error("Client ID '{0}' is a follower and failed to accept finish request to neighborhood initialization process (error code {1}), it will be disconnected and deleted from our database.", Client.IdentityId.ToHex(), ResponseMessage.Message.Response.Status);
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
    public async Task<IProtocolMessage<Message>> ProcessMessageCanStoreDataRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      CanStoreDataRequest canStoreDataRequest = RequestMessage.Message.Request.ConversationRequest.CanStoreData;

      // First check whether the new object is valid.
      CanIdentityData identityData = canStoreDataRequest.Data;
      bool uploadNew = identityData != null;
      if (uploadNew)
      {
        byte[] claimedServerId = identityData.HostingServerId.ToByteArray();
        byte[] realServerId = serverComponent.ServerId;

        if (!ByteArrayComparer.Equals(claimedServerId, realServerId))
        {
          log.Debug("Identity data object from client contains invalid hostingServerId.");
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "data.hostingServerId");
        }
      }

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        ContentAddressNetwork can = (ContentAddressNetwork)Base.ComponentDictionary[ContentAddressNetwork.ComponentName];
        CanApi canApi = can.Api;
        
        // Then delete old object if there is any.
        if (res == null)
        {
          res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

          byte[] canOldObjectHash = await unitOfWork.HostedIdentityRepository.GetCanObjectHashAsync(Client.IdentityId);
          bool deleteOldObjectFromDb = false;
          if (canOldObjectHash != null)
          {
            CanDeleteResult cres = await canApi.CanDeleteObjectByHash(canOldObjectHash);
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
            if (await unitOfWork.HostedIdentityRepository.SetCanObjectHashAsync(Client.IdentityId, null))
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

              if (await unitOfWork.HostedIdentityRepository.SetCanObjectHashAsync(Client.IdentityId, canHash))
              {
                res = messageBuilder.CreateCanStoreDataResponse(RequestMessage, canHash);
              }
              else
              {
                // Unable to save the new can hash to DB, so delete the object from CAN as well.
                CanDeleteResult delRes = await canApi.CanDeleteObjectByHash(canHash);

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

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }



    /// <summary>
    /// Processes CanPublishIpnsRecordRequest message from client.
    /// <para>Uploads client's IPNS record to CAN.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessMessageCanPublishIpnsRecordRequestAsync(IncomingClient Client, IProtocolMessage<Message> RequestMessage)
    {
      log.Trace("()");

      IProtocolMessage<Message> res = null;
      if (!CheckSessionConditions(Client, RequestMessage, ServerRole.ClientCustomer, ClientConversationStatus.Authenticated, out res))
      {
        log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
        return res;
      }

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      CanPublishIpnsRecordRequest canPublishIpnsRecordRequest = RequestMessage.Message.Request.ConversationRequest.CanPublishIpnsRecord;

      if (canPublishIpnsRecordRequest.Record != null)
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
      else
      {
        log.Debug("Null record provided.");
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "record");
      }

      if (res == null)
      {
        ContentAddressNetwork can = (ContentAddressNetwork)Base.ComponentDictionary[ContentAddressNetwork.ComponentName];
        CanRefreshIpnsResult cres = await can.Api.RefreshIpnsRecord(canPublishIpnsRecordRequest.Record, Client.PublicKey);
        if (cres.Success) res = messageBuilder.CreateCanPublishIpnsRecordResponse(RequestMessage);
        else res = messageBuilder.CreateErrorRejectedResponse(RequestMessage, cres.Message);
      }

      log.Trace("(-):*.Response.Status={0}", res.Message.Response.Status);
      return res;
    }
  }
}
