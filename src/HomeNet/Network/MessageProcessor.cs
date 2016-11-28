using Google.Protobuf;
using HomeNet.Data;
using HomeNet.Data.Models;
using HomeNet.Data.Repositories;
using HomeNet.Kernel;
using HomeNet.Utils;
using HomeNetCrypto;
using HomeNetProtocol;
using Iop.Homenode;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HomeNet.Network
{
  /// <summary>
  /// Implements the logic behind processing incoming messages to the node.
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
    public ClientList clientList;

    /// <summary>
    /// Creates a new instance connected to the parent role server.
    /// </summary>
    /// <param name="RoleServer">Parent role server.</param>
    /// <param name="LogPrefix">Log prefix of the parent role server.</param>
    public MessageProcessor(TcpRoleServer RoleServer, string LogPrefix)
    {
      roleServer = RoleServer;
      logPrefix = LogPrefix;
      log = new PrefixLogger("HomeNet.Network.MessageProcessor", logPrefix);
      serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
      clientList = serverComponent.GetClientList();
    }



    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client who send the message.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(Client Client, Message IncomingMessage)
    {
      string prefix = string.Format("{0}[{1}] ", logPrefix, Client.RemoteEndPoint);
      PrefixLogger log = new PrefixLogger("HomeNet.Network.MessageProcessor", prefix);

      MessageBuilder messageBuilder = Client.MessageBuilder;

      bool res = false;
      log.Debug("()");
      try
      {
        // Update time until this client's connection is considered inactive.
        Client.NextKeepAliveTime = DateTime.UtcNow.AddSeconds(Client.KeepAliveIntervalSeconds);
        log.Trace("Client ID {0} NextKeepAliveTime updated to {1}.", Client.Id.ToHex(), Client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));

        string msgStr = IncomingMessage.ToString();
        log.Trace("Received message type is {0}, message ID is {1}:\n{2}", IncomingMessage.MessageTypeCase, IncomingMessage.Id, msgStr.SubstrMax(512));
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
                    log.Trace("Single request type is {0}, version is {1}.", singleRequest.RequestTypeCase, ProtocolHelper.VersionBytesToString(singleRequest.Version.ToByteArray()));

                    if (!ProtocolHelper.IsValidVersion(singleRequest.Version.ToByteArray()))
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

                      case ConversationRequest.RequestTypeOneofCase.HomeNodeRequest:
                        responseMessage = await ProcessMessageHomeNodeRequestRequestAsync(Client, IncomingMessage);
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

                      case ConversationRequest.RequestTypeOneofCase.CancelHomeNodeAgreement:
                        responseMessage = await ProcessMessageCancelHomeNodeAgreementRequestAsync(Client, IncomingMessage);
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
              log.Trace("Response status is {0}, details are '{1}', conversation type is {0}.", response.Status, response.Details, response.ConversationTypeCase);

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

      if (res &&  Client.ForceDisconnect)
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
    public async Task SendProtocolViolation(Client Client)
    {
      MessageBuilder mb = new MessageBuilder(0, new List<byte[]>() { new byte[] { 1, 0, 0 } }, null);
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
    public bool CheckSessionConditions(Client Client, Message RequestMessage, ServerRole? RequiredRole, ClientConversationStatus? RequiredConversationStatus, out Message ResponseMessage)
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
    /// Creates a list of role servers to be sent to the requesting client.
    /// The information about role servers can be obtained from Network.Server component.
    /// </summary>
    /// <returns>List of role server descriptions.</returns>
    private List<Iop.Homenode.ServerRole> GetRolesFromServerComponent()
    {
      log.Trace("()");

      List<Iop.Homenode.ServerRole> res = new List<Iop.Homenode.ServerRole>();

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
      if (identityId.Length == BaseIdentity.IdentifierLength)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          HomeIdentity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => i.IdentityId == identityId)).FirstOrDefault();
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

                res = messageBuilder.CreateGetIdentityInformationResponse(RequestMessage, isHosted, null, isOnline, publicKey, name, type, location, extraData, profileImage, thumbnailImage, applicationServices);
              }
              else
              {
                byte[] targetHomeNode = identity.HomeNodeId;
                res = messageBuilder.CreateGetIdentityInformationResponse(RequestMessage, isHosted, targetHomeNode);
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
            log.Trace("Identity ID '{0}' is not hosted by this node.", identityId.ToHex());
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
      byte[] clientChallenge = startConversationRequest.ClientChallenge.ToByteArray();

      if (clientChallenge.Length == ProtocolHelper.ChallengeDataSize)
      {
        byte[] version;
        if (GetCommonSupportedVersion(startConversationRequest.SupportedVersions, out version))
        {
          Client.PublicKey = startConversationRequest.PublicKey.ToByteArray();
          Client.IdentityId = Crypto.Sha256(Client.PublicKey);

          if (clientList.AddNetworkPeerWithIdentity(Client))
          {
            Client.MessageBuilder.SetProtocolVersion(version);

            byte[] challenge = new byte[ProtocolHelper.ChallengeDataSize];
            Crypto.Rng.GetBytes(challenge);
            Client.AuthenticationChallenge = challenge;
            Client.ConversationStatus = ClientConversationStatus.ConversationStarted;

            log.Debug("Client {0} conversation status updated to {1}, selected version is '{2}', client public key set to '{3}', client identity ID set to '{4}', challenge set to '{5}'.",
              Client.RemoteEndPoint, Client.ConversationStatus, ProtocolHelper.VersionBytesToString(version), Client.PublicKey.ToHex(), Client.IdentityId.ToHex(), Client.AuthenticationChallenge.ToHex());

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
        log.Warn("Client send clientChallenge, which is {0} bytes, but it should be {1} bytes.", clientChallenge.Length, ProtocolHelper.ChallengeDataSize);
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "clientChallenge");
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
#warning TODO: This function is currently implemented to mostly contracts, they can only be used for specifying identity type.
      // TODO: CHECK CONTRACT:
      // * signature is valid 
      // * planId is valid
      // * startTime is per specification
      // * identityPublicKey is client's key 
      // * identityType is valid
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
      string identityType = contract != null ? contract.IdentityType : "<new>";


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
              HomeIdentity existingIdentity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => i.IdentityId == Client.IdentityId)).FirstOrDefault();
              // Identity does not exist at all, or it has been cancelled so that ExpirationDate was set.
              if ((existingIdentity == null) || (existingIdentity.ExpirationDate != null))
              {
                // We do not have the identity in our client's database,
                // OR we do have the identity in our client's database, but it's contract has been cancelled.
                if (existingIdentity != null)
                  log.Debug("Identity ID '{0}' is already a client of this node, but its contract has been cancelled.", Client.IdentityId.ToHex());

                HomeIdentity identity = existingIdentity == null ? new HomeIdentity() : existingIdentity;

                // We can't change primary identifier in existing entity.
                if (existingIdentity == null) identity.IdentityId = Client.IdentityId;

                identity.HomeNodeId = new byte[0];
                identity.PublicKey = Client.PublicKey;
                identity.Version = new byte[] { 0, 0, 0 };
                identity.Name = "";
                identity.Type = identityType;
                // Existing cancelled identity profile does not have images, no need to delete anything at this point.
                identity.ProfileImage = null;
                identity.ThumbnailImage = null;
                identity.InitialLocationLatitude = GpsLocation.NoLocationDecimal;
                identity.InitialLocationLongitude = GpsLocation.NoLocationDecimal;
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
                log.Debug("Identity ID '{0}' is already a client of this node.", Client.IdentityId.ToHex());
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
              HomeIdentity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
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
                log.Debug("Identity '{0}' is not a client of this node.", Client.IdentityId.ToHex());
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
          HomeIdentity identityForValidation = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null), null, true)).FirstOrDefault();
          if (identityForValidation != null)
          {
            Message errorResponse;
            if (ValidateUpdateProfileRequest(identityForValidation, updateProfileRequest, messageBuilder, RequestMessage, out errorResponse))
            {
              // If an identity has a profile image and a thumbnail image, they are saved on the disk.
              // If we are replacing those images, we have to create new files and delete the old files.
              // First, we create the new files and then in DB transaction, we get information about 
              // whether to delete existing files and which ones.
              Guid? profileImageToDelete = null;
              Guid? thumbnailImageToDelete = null;

              if (updateProfileRequest.SetImage)
              {
                identityForValidation.ProfileImage = Guid.NewGuid();
                identityForValidation.ThumbnailImage = Guid.NewGuid();

                byte[] profileImage = updateProfileRequest.Image.ToByteArray();
                byte[] thumbnailImage;
                ImageHelper.ProfileImageToThumbnailImage(profileImage, out thumbnailImage);

                await identityForValidation.SaveProfileImageDataAsync(profileImage);
                await identityForValidation.SaveThumbnailImageDataAsync(thumbnailImage);
              }


              // Update database record.
              DatabaseLock lockObject = UnitOfWork.HomeIdentityLock;
              await unitOfWork.AcquireLockAsync(lockObject);
              try
              {
                HomeIdentity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();

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
                  {
                    GpsLocation gpsLocation = new GpsLocation(updateProfileRequest.Latitude, updateProfileRequest.Longitude);
                    identity.SetInitialLocation(gpsLocation);
                  }

                  if (updateProfileRequest.SetExtraData)
                    identity.ExtraData = updateProfileRequest.ExtraData;

                  unitOfWork.HomeIdentityRepository.Update(identity);
                  success = await unitOfWork.SaveAsync();
                }
                else
                {
                  log.Debug("Identity '{0}' is not a client of this node.", Client.IdentityId.ToHex());
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
                log.Debug("Identity '{0}' updated its profile in the database.", Client.IdentityId.ToHex());
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
            log.Debug("Identity '{0}' is not a client of this node.", Client.IdentityId.ToHex());
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
    private bool ValidateUpdateProfileRequest(HomeIdentity Identity, UpdateProfileRequest UpdateProfileRequest, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
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
          byte[] version = UpdateProfileRequest.Version.ToByteArray();

          // Currently only supported version is 1,0,0.
          if (!StructuralEqualityComparer<byte[]>.Default.Equals(version, new byte[] { 1, 0, 0 }))
          {
            log.Debug("Unsupported version '{0}'.", ProtocolHelper.VersionBytesToString(version));
            details = "version";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetName)
        {
          string name = UpdateProfileRequest.Name;

          // Name is non-empty string, max Identity.MaxProfileNameLengthBytes bytes long.
          if (string.IsNullOrEmpty(name) || (Encoding.UTF8.GetByteCount(name) > BaseIdentity.MaxProfileNameLengthBytes))
          {
            log.Debug("Invalid name '{0}'.", name);
            details = "name";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetImage)
        {
          byte[] image = UpdateProfileRequest.Image.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxProfileImageLengthBytes.
          if ((image.Length == 0) || (image.Length > BaseIdentity.MaxProfileImageLengthBytes) || !ImageHelper.ValidateImageFormat(image))
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
          if (Encoding.UTF8.GetByteCount(extraData) > BaseIdentity.MaxProfileExtraDataLengthBytes)
          {
            log.Debug("Extra data too large ({0} bytes, limit is {1}).", Encoding.UTF8.GetByteCount(extraData), BaseIdentity.MaxProfileExtraDataLengthBytes);
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

      if (!cancelHomeNodeAgreementRequest.RedirectToNewHomeNode || (cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId.Length == BaseIdentity.IdentifierLength))
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
            HomeIdentity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == Client.IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
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
              log.Debug("Identity '{0}' is not a client of this node.", Client.IdentityId.ToHex());
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
            if (cancelHomeNodeAgreementRequest.RedirectToNewHomeNode) log.Debug("Identity '{0}' home node agreement cancelled and redirection set to node '{1}'.", Client.IdentityId.ToHex(), cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId.ToByteArray().ToHex());
            else log.Debug("Identity '{0}' home node agreement cancelled and no redirection set.", Client.IdentityId.ToHex());

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
        log.Debug("Invalid home node identifier '{0}'.", cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId.ToByteArray().ToHex());
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
        if (Client.ApplicationServices.AddServices(applicationServiceAddRequest.ServiceNames))
        {
          log.Debug("Service names added to identity '{0}': {1}", Client.IdentityId.ToHex(), string.Join(", ", applicationServiceAddRequest.ServiceNames));
          res = messageBuilder.CreateApplicationServiceAddResponse(RequestMessage);
        }
        else
        {
          log.Debug("Identity '{0}' application services list not changed, number of services would exceed the limit {1}.", Client.IdentityId.ToHex(), Client.MaxClientApplicationServices);
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
    public async Task<Message> ProcessMessageCallIdentityApplicationServiceRequestAsync(Client Client, Message RequestMessage)
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
          HomeIdentity identity = (await unitOfWork.HomeIdentityRepository.GetAsync(i => (i.IdentityId == calleeIdentityId))).FirstOrDefault();
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
        Client callee = clientList.GetCheckedInClient(calleeIdentityId);
        if (callee != null)
        {
          // The callee is hosted on the node, it is online and its profile is initialized.
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
    public async Task<bool> ProcessMessageIncomingCallNotificationResponseAsync(Client Client, Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;
      
      RelayConnection relay = (RelayConnection)Request.Context;
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
    public async Task<Message> ProcessMessageApplicationServiceSendMessageRequestAsync(Client Client, Message RequestMessage)
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
    public async Task<bool> ProcessMessageApplicationServiceReceiveMessageNotificationResponseAsync(Client Client, Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;

      RelayMessageContext context = (RelayMessageContext)Request.Context;
      res = await context.Relay.RecipientConfirmedMessage(Client, ResponseMessage, context.SenderRequest);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes ProfileStatsRequest message from client.
    /// <para>Obtains identity profiles statistics from the node.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageProfileStatsRequestAsync(Client Client, Message RequestMessage)
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
          List<ProfileStatsItem> stats = await unitOfWork.HomeIdentityRepository.GetProfileStatsAsync();
          res = messageBuilder.CreateProfileStatsResponse(RequestMessage, stats);
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

    /// <summary>Maximum number of results the node can send in the response if images are included.</summary>
    public const int ProfileSearchMaxResponseRecordsWithImage = 100;

    /// <summary>Maximum number of results the node can send in the response if images are not included.</summary>
    public const int ProfileSearchMaxResponseRecordsWithoutImage = 1000;


    /// <summary>
    /// Processes ProfileSearchRequest message from client.
    /// <para>Performs a search operation to find all matching identities that this node hosts, possibly including identities hosted in the node's neighborhood.</para>
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessMessageProfileSearchRequestAsync(Client Client, Message RequestMessage)
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

            // First, we try to find enough results among identities hosted on this node.
            List<IdentityNetworkProfileInformation> searchResultsNeighborhood = new List<IdentityNetworkProfileInformation>();
            List<IdentityNetworkProfileInformation> searchResultsLocal = await ProfileSearch(unitOfWork.HomeIdentityRepository, maxResults, typeFilter, nameFilter, locationFilter, radius, extraDataFilter, includeImages, watch);
            if (searchResultsLocal != null)
            {
              bool error = false;
              // If possible and needed we try to find more results among identities hosted in this node's neighborhood.
              if (!profileSearchRequest.IncludeHostedOnly && (searchResultsLocal.Count < maxResults))
              {
                maxResults -= (uint)searchResultsLocal.Count;
                searchResultsNeighborhood = await ProfileSearch(unitOfWork.NeighborhoodIdentityRepository, maxResults, typeFilter, nameFilter, locationFilter, radius, extraDataFilter, includeImages, watch);
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

                res = messageBuilder.CreateProfileSearchResponse(RequestMessage, (uint)allResults.Count, maxResponseResults, responseResults);
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
    private async Task<List<IdentityNetworkProfileInformation>> ProfileSearch<T>(IdentityRepository<T> Repository, uint MaxResults, string TypeFilter, string NameFilter, GpsLocation LocationFilter, uint Radius, string ExtraDataFilter, bool IncludeImages, Stopwatch TimeoutWatch) where T:BaseIdentity
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
              if (Repository is HomeIdentityRepository)
              {
                inpi.IsHosted = true;
                inpi.IsOnline = clientList.IsIdentityOnline(identity.IdentityId);
              }
              else
              {
                inpi.IsHosted = false;
                inpi.NeighborNodeNetworkId = ProtocolHelper.ByteArrayToByteString(identity.HomeNodeId);
              }

              inpi.IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(identity.PublicKey);
              inpi.Type = identity.Type != null ? identity.Type : "";
              inpi.Name = identity.Name != null ? identity.Name : "";
              inpi.Latitude = identityLocation.GetLocationTypeLatitude();
              inpi.Longitude = identityLocation.GetLocationTypeLongitude();
              inpi.ExtraData = identity.ExtraData != null ? identity.ExtraData : "";
              if (IncludeImages)
              {
                byte[] image = await identity.GetThumbnailImageDataAsync();
                if (image != null)
                  inpi.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(image);
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
    public Message ProcessMessageProfileSearchPartRequest(Client Client, Message RequestMessage)
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
    public async Task<Message> ProcessMessageAddRelatedIdentityRequestAsync(Client Client, Message RequestMessage)
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
                  unitOfWork.RelatedIdentityRepository.Insert(newRelation);
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
    private bool ValidateAddRelatedIdentityRequest(Client Client, AddRelatedIdentityRequest AddRelatedIdentityRequest, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
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
        log.Debug("Card application ID is too long.");
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
        RelationshipCard emptyIdCard = new RelationshipCard()
        {
          CardId = ProtocolHelper.ByteArrayToByteString(new byte[RelatedIdentity.CardIdentifierLength]),
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
        if (Encoding.UTF8.GetByteCount(card.Type) > ProtocolHelper.MaxRelationshipCardTypeLengthBytes)
        {
          log.Debug("Card type is too long.");
          details = "signedCard.card.type";
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
    public async Task<Message> ProcessMessageRemoveRelatedIdentityRequestAsync(Client Client, Message RequestMessage)
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
    public async Task<Message> ProcessMessageGetIdentityRelationshipsInformationRequestAsync(Client Client, Message RequestMessage)
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
      }
      else
      {
        log.Warn("Type filter is too long.");
        res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "type");
      }

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }
  }
}
