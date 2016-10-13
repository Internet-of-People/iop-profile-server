using Google.Protobuf;
using Iop.Homenode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HomeNetCrypto;

namespace HomeNetProtocol
{
  /// <summary>
  /// Allows easy construction of requests and responses.
  /// </summary>
  public class MessageBuilder
  {
    /// <summary>Original identifier base.</summary>
    private int idBase;

    /// <summary>Identifier that is unique per class instance for each message.</summary>
    private int id;

    /// <summary>Supported protocol versions ordered by preference.</summary>
    private List<ByteString> supportedVersions;

    /// <summary>Selected protocol version.</summary>
    private ByteString version;

    /// <summary>Cryptographic key set representing the identity.</summary>
    private KeysEd25519 keys;

    /// <summary>
    /// Initializes message builder.
    /// </summary>
    /// <param name="IdBase">Base value for message IDs. First message will have ID set to IdBase + 1.</param>
    /// <param name="SupportedVersions">List of supported versions in binary form ordered by claler's preference.</param>
    /// <param name="Keys">Cryptographic key set representing the caller's identity.</param>
    public MessageBuilder(uint IdBase, List<byte[]> SupportedVersions, KeysEd25519 Keys)
    {
      idBase = (int)IdBase;
      id = idBase;
      supportedVersions = new List<ByteString>();
      foreach (byte[] version in SupportedVersions)
        supportedVersions.Add(ProtocolHelper.VersionToByteString(version));

      version = supportedVersions[0];
      keys = Keys;
    }


    /// <summary>
    /// Sets the version of the protocol that will be used by the message builder.
    /// </summary>
    /// <param name="SelectedVersion">Selected version information in binary format.</param>
    public void SetProtocolVersion(byte[] SelectedVersion)
    {
      version = ProtocolHelper.VersionToByteString(SelectedVersion);
    }

    /// <summary>
    /// Resets message identifier to its original value.
    /// </summary>
    public void ResetId()
    {
      id = idBase;
    }

    /// <summary>
    /// Creates a new request template and sets its ID to ID of the last message + 1.
    /// </summary>
    /// <returns>New request message template.</returns>
    public Message CreateRequest()
    {
      int newId = Interlocked.Increment(ref id);

      Message res = new Message();
      res.Id = (uint)newId;
      res.Request = new Request();

      return res;
    }


    /// <summary>
    /// Creates a new response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <param name="ResponseStatus">Status code of the response.</param>
    /// <returns>Response message template for the request.</returns>
    public Message CreateResponse(Message Request, Status ResponseStatus)
    {
      Message res = new Message();
      res.Id = Request.Id;
      res.Response = new Response();
      res.Response.Status = ResponseStatus;

      return res;
    }

    /// <summary>
    /// Creates a new successful response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Response message template for the request.</returns>
    public Message CreateOkResponse(Message Request)
    {
      return CreateResponse(Request, Status.Ok);
    }


    /// <summary>
    /// Creates a new error response for a specific request with ERROR_PROTOCOL_VIOLATION status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorProtocolViolationResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorProtocolViolation);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_UNSUPPORTED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorUnsupportedResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorUnsupported);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BANNED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorBannedResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorBanned);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BUSY status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorBusyResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorBusy);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_UNAUTHORIZED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorUnauthorizedResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorUnauthorized);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BAD_ROLE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorBadRoleResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorBadRole);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BAD_CONVERSATION_STATUS status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorBadConversationStatusResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorBadConversationStatus);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_INTERNAL status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorInternalResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorInternal);
    }


    /// <summary>
    /// Creates a new error response for a specific request with ERROR_QUOTA_EXCEEDED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorQuotaExceededResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorQuotaExceeded);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_INVALID_SIGNATURE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorInvalidSignatureResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorInvalidSignature);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_NOT_FOUND status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorNotFoundResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorNotFound);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_INVALID_VALUE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <param name="Details">Optionally, details about the error to be sent in 'Response.details'.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorInvalidValueResponse(Message Request, string Details = null)
    {
      Message res = CreateResponse(Request, Status.ErrorInvalidValue);
      if (Details != null)
        res.Response.Details = Details;
      return res;
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_ALREADY_EXISTS status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorAlreadyExistsResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorAlreadyExists);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_NOT_AVAILABLE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorNotAvailableResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorNotAvailable);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_REJECTED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorRejectedResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorRejected);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_UNINITIALIZED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorUninitializedResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorUninitialized);
    }






    /// <summary>
    /// Creates a new single request.
    /// </summary>
    /// <returns>New single request message template.</returns>
    public Message CreateSingleRequest()
    {
      Message res = CreateRequest();
      res.Request.SingleRequest = new SingleRequest();
      res.Request.SingleRequest.Version = version;

      return res;
    }

    /// <summary>
    /// Creates a new conversation request.
    /// </summary>
    /// <returns>New conversation request message template.</returns>
    public Message CreateConversationRequest()
    {
      Message res = CreateRequest();
      res.Request.ConversationRequest = new ConversationRequest();

      return res;
    }


    /// <summary>
    /// Signs a request body with identity private key and puts the signature to the ConversationRequest.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationRequest.</param>
    /// <param name="RequestBody">Part of the request to sign.</param>
    public void SignConversationRequestBody(Message Message, IMessage RequestBody)
    {
      byte[] msg = RequestBody.ToByteArray();
      byte[] signature = Ed25519.Sign(msg, keys.ExpandedPrivateKey);
      Message.Request.ConversationRequest.Signature = ProtocolHelper.ByteArrayToByteString(signature);
    }


    /// <summary>
    /// Verifies ConversationRequest.Signature signature of a request body with a given public key.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationRequest.</param>
    /// <param name="RequestBody">Part of the request that was signed.</param>
    /// <param name="PublicKey">Public key of the identity that created the signature.</param>
    /// <returns>true if the signature is valid, false otherwise including missing signature.</returns>
    public bool VerifySignedConversationRequestBody(Message Message, IMessage RequestBody, byte[] PublicKey)
    {
      byte[] msg = RequestBody.ToByteArray();
      byte[] signature = Message.Request.ConversationRequest.Signature.ToByteArray();

      bool res = Ed25519.Verify(signature, msg, PublicKey);
      return res;
    }


    /// <summary>
    /// Signs a response body with identity private key and puts the signature to the ConversationResponse.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="RequestBody">Part of the request to sign.</param>
    public void SignConversationResponseBody(Message Message, IMessage ResponseBody)
    {
      byte[] msg = ResponseBody.ToByteArray();
      byte[] signature = Ed25519.Sign(msg, keys.ExpandedPrivateKey);
      Message.Response.ConversationResponse.Signature = ProtocolHelper.ByteArrayToByteString(signature);
    }


    /// <summary>
    /// Creates a new successful single response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Single response message template for the request.</returns>
    public Message CreateSingleResponse(Message Request)
    {
      Message res = CreateOkResponse(Request);
      res.Response.SingleResponse = new SingleResponse();
      res.Response.SingleResponse.Version = Request.Request.SingleRequest.Version;

      return res;
    }

    /// <summary>
    /// Creates a new successful conversation response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Conversation response message template for the request.</returns>
    public Message CreateConversationResponse(Message Request)
    {
      Message res = CreateOkResponse(Request);
      res.Response.ConversationResponse = new ConversationResponse();

      return res;
    }


    /// <summary>
    /// Creates a new PingRequest message.
    /// </summary>
    /// <param name="Payload">Caller defined payload to be sent to the other peer.</param>
    /// <returns>PingRequest message that is ready to be sent.</returns>
    public Message CreatePingRequest(byte[] Payload)
    {
      PingRequest pingRequest = new PingRequest();
      pingRequest.Payload = ProtocolHelper.ByteArrayToByteString(Payload);

      Message res = CreateSingleRequest();
      res.Request.SingleRequest.Ping = pingRequest;

      return res;
    }

    /// <summary>
    /// Creates a response message to a PingRequest message.
    /// </summary>
    /// <param name="Request">PingRequest message for which the response is created.</param>
    /// <param name="Payload">Payload to include in the response.</param>
    /// <param name="Clock">Timestamp to include in the response.</param>
    /// <returns>PingResponse message that is ready to be sent.</returns>
    public Message CreatePingResponse(Message Request, byte[] Payload, long Clock)
    {
      PingResponse pingResponse = new PingResponse();
      pingResponse.Clock = Clock;
      pingResponse.Payload = ProtocolHelper.ByteArrayToByteString(Payload);

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.Ping = pingResponse;

      return res;
    }

    /// <summary>
    /// Creates a new ListRolesRequest message.
    /// </summary>
    /// <returns>ListRolesRequest message that is ready to be sent.</returns>
    public Message CreateListRolesRequest()
    {
      ListRolesRequest listRolesRequest = new ListRolesRequest();

      Message res = CreateSingleRequest();
      res.Request.SingleRequest.ListRoles = listRolesRequest;

      return res;
    }

    /// <summary>
    /// Creates a response message to a ListRolesRequest message.
    /// </summary>
    /// <param name="Request">ListRolesRequest message for which the response is created.</param>
    /// <param name="Roles">List of role server descriptions to be included in the response.</param>
    /// <returns>ListRolesResponse message that is ready to be sent.</returns>
    public Message CreateListRolesResponse(Message Request, List<ServerRole> Roles)
    {
      ListRolesResponse listRolesResponse = new ListRolesResponse();
      listRolesResponse.Roles.AddRange(Roles);

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.ListRoles = listRolesResponse;

      return res;
    }


    /// <summary>
    /// Creates a new StartConversationRequest message.
    /// </summary>
    /// <returns>StartConversationRequest message that is ready to be sent.</returns>
    public Message CreateStartConversationRequest()
    {
      StartConversationRequest startConversationRequest = new StartConversationRequest();
      startConversationRequest.SupportedVersions.Add(supportedVersions);

      startConversationRequest.PublicKey = ProtocolHelper.ByteArrayToByteString(keys.PublicKey);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.Start = startConversationRequest;

      return res;
    }

    /// <summary>
    /// Creates a response message to a StartConversationRequest message.
    /// </summary>
    /// <param name="Request">StartConversationRequest message for which the response is created.</param>
    /// <param name="Version">Selected version that both server and client support.</param>
    /// <param name="PublicKey">Server's public key.</param>
    /// <param name="Challenge">Server's generated challenge data for client's authentication.</param>
    /// <returns>StartConversationResponse message that is ready to be sent.</returns>
    public Message CreateStartConversationResponse(Message Request, byte[] Version, byte[] PublicKey, byte[] Challenge)
    {
      StartConversationResponse startConversationResponse = new StartConversationResponse();
      startConversationResponse.Version = ProtocolHelper.VersionToByteString(Version);
      startConversationResponse.PublicKey = ProtocolHelper.ByteArrayToByteString(PublicKey);
      startConversationResponse.Challenge = ProtocolHelper.ByteArrayToByteString(Challenge);

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.Start = startConversationResponse;

      return res;
    }



    /// <summary>
    /// Creates a new HomeNodeRequestRequest message.
    /// </summary>
    /// <param name="Contract">List of supported protocol versions.</param>
    /// <returns>HomeNodeRequestRequest message that is ready to be sent.</returns>
    public Message CreateHomeNodeRequestRequest(HomeNodePlanContract Contract)
    {
      HomeNodeRequestRequest homeNodeRequestRequest = new HomeNodeRequestRequest();
      homeNodeRequestRequest.Contract = Contract;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.HomeNodeRequest = homeNodeRequestRequest;

      SignConversationRequestBody(res, homeNodeRequestRequest);
      return res;
    }


    /// <summary>
    /// Creates a response message to a HomeNodeRequestRequest message.
    /// </summary>
    /// <param name="Request">HomeNodeRequestRequest message for which the response is created.</param>
    /// <param name="Contract">Contract copy from HomeNodeRequest.Contract.</param>
    /// <returns>HomeNodeRequestResponse message that is ready to be sent.</returns>
    public Message CreateHomeNodeRequestResponse(Message Request, HomeNodePlanContract Contract)
    {
      HomeNodeRequestResponse homeNodeRequestResponse = new HomeNodeRequestResponse();
      homeNodeRequestResponse.Contract = Contract;

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.HomeNodeRequest = homeNodeRequestResponse;

      SignConversationResponseBody(res, homeNodeRequestResponse);

      return res;
    }
    


    /// <summary>
    /// Creates a new CheckInRequest message.
    /// </summary>
    /// <param name="Challenge">Challenge received in StartConversationRequest.Challenge.</param>
    /// <returns>CheckInRequest message that is ready to be sent.</returns>
    public Message CreateCheckInRequest(byte[] Challenge)
    {
      CheckInRequest checkInRequest = new CheckInRequest();
      checkInRequest.Challenge = ProtocolHelper.ByteArrayToByteString(Challenge);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.CheckIn = checkInRequest;

      SignConversationRequestBody(res, checkInRequest);
      return res;
    }

    /// <summary>
    /// Creates a response message to a CheckInRequest message.
    /// </summary>
    /// <param name="Request">CheckInRequest message for which the response is created.</param>
    /// <returns>CheckInResponse message that is ready to be sent.</returns>
    public Message CreateCheckInResponse(Message Request)
    {
      CheckInResponse checkInResponse = new CheckInResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.CheckIn = checkInResponse;

      return res;
    }


    /// <summary>
    /// Creates a new VerifyIdentityRequest message.
    /// </summary>
    /// <param name="Challenge">Challenge received in StartConversationRequest.Challenge.</param>
    /// <returns>VerifyIdentityRequest message that is ready to be sent.</returns>
    public Message CreateVerifyIdentityRequest(byte[] Challenge)
    {
      VerifyIdentityRequest verifyIdentityRequest = new VerifyIdentityRequest();
      verifyIdentityRequest.Challenge = ProtocolHelper.ByteArrayToByteString(Challenge);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.VerifyIdentity = verifyIdentityRequest;

      SignConversationRequestBody(res, verifyIdentityRequest);
      return res;
    }

    /// <summary>
    /// Creates a response message to a VerifyIdentityRequest message.
    /// </summary>
    /// <param name="Request">VerifyIdentityRequest message for which the response is created.</param>
    /// <returns>VerifyIdentityResponse message that is ready to be sent.</returns>
    public Message CreateVerifyIdentityResponse(Message Request)
    {
      VerifyIdentityResponse verifyIdentityResponse = new VerifyIdentityResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.VerifyIdentity = verifyIdentityResponse;

      return res;
    }


    /// <summary>
    /// Creates a new UpdateProfileRequest message.
    /// </summary>
    /// <param name="Version">Profile version information or null if profile version is not to be changed.</param>
    /// <param name="Name">Identity name or null if identity name is not to be changed.</param>
    /// <param name="Image">Profile image data or null if profile image is not to be changed.</param>
    /// <param name="Location">Encoded profile location information or null if location is not to be changed.</param>
    /// <param name="ExtraData">Profile's extra data information or null if profile's extra data is not to be changed.</param>
    /// <returns>CreateUpdateProfileRequest message that is ready to be sent.</returns>
    public Message CreateUpdateProfileRequest(byte[] Version, string Name, byte[] Image, uint? Location, string ExtraData)
    {
      UpdateProfileRequest updateProfileRequest = new UpdateProfileRequest();
      updateProfileRequest.SetVersion = Version != null;
      updateProfileRequest.SetName = Name != null;
      updateProfileRequest.SetImage = Image != null;
      updateProfileRequest.SetLocation = Location != null;
      updateProfileRequest.SetExtraData = ExtraData != null;

      if (updateProfileRequest.SetVersion)
        updateProfileRequest.Version = ProtocolHelper.ByteArrayToByteString(Version);

      if (updateProfileRequest.SetName)
        updateProfileRequest.Name = Name;

      if (updateProfileRequest.SetImage)
        updateProfileRequest.Image = ProtocolHelper.ByteArrayToByteString(Image);

      if (updateProfileRequest.SetLocation)
        updateProfileRequest.Location = Location.Value;

      if (updateProfileRequest.SetExtraData)
        updateProfileRequest.ExtraData = ExtraData;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.UpdateProfile = updateProfileRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a UpdateProfileRequest message.
    /// </summary>
    /// <param name="Request">UpdateProfileRequest message for which the response is created.</param>
    /// <returns>UpdateProfileResponse message that is ready to be sent.</returns>
    public Message CreateUpdateProfileResponse(Message Request)
    {
      UpdateProfileResponse updateProfileResponse = new UpdateProfileResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.UpdateProfile = updateProfileResponse;

      return res;
    }


    /// <summary>
    /// Creates a new CancelHomeNodeAgreementRequest message.
    /// </summary>
    /// <param name="NewHomeNodeId">Network identifier of the identity's new home node, or null if this information is not to be sent to the previous home node.</param>
    /// <returns>CancelHomeNodeAgreementRequest message that is ready to be sent.</returns>
    public Message CreateCancelHomeNodeAgreementRequest(byte[] NewHomeNodeId)
    {
      CancelHomeNodeAgreementRequest cancelHomeNodeAgreementRequest = new CancelHomeNodeAgreementRequest();
      cancelHomeNodeAgreementRequest.RedirectToNewHomeNode = NewHomeNodeId != null;
      if (cancelHomeNodeAgreementRequest.RedirectToNewHomeNode)
        cancelHomeNodeAgreementRequest.NewHomeNodeNetworkId = ProtocolHelper.ByteArrayToByteString(NewHomeNodeId);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.CancelHomeNodeAgreement = cancelHomeNodeAgreementRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CancelHomeNodeAgreementRequest message.
    /// </summary>
    /// <param name="Request">CancelHomeNodeAgreementRequest message for which the response is created.</param>
    /// <returns>CancelHomeNodeAgreementResponse message that is ready to be sent.</returns>
    public Message CreateCancelHomeNodeAgreementResponse(Message Request)
    {
      CancelHomeNodeAgreementResponse cancelHomeNodeAgreementResponse = new CancelHomeNodeAgreementResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.CancelHomeNodeAgreement = cancelHomeNodeAgreementResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ApplicationServiceAddRequest message.
    /// </summary>
    /// <param name="ServiceNames">List of service names to add to the list of services supported in the currently opened session.</param>
    /// <returns>ApplicationServiceAddRequest message that is ready to be sent.</returns>
    public Message CreateApplicationServiceAddRequest(List<string> ServiceNames)
    {
      ApplicationServiceAddRequest applicationServiceAddRequest = new ApplicationServiceAddRequest();
      applicationServiceAddRequest.ServiceNames.Add(ServiceNames);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.ApplicationServiceAdd = applicationServiceAddRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceAddRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceAddRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceAddResponse message that is ready to be sent.</returns>
    public Message CreateApplicationServiceAddResponse(Message Request)
    {
      ApplicationServiceAddResponse applicationServiceAddResponse = new ApplicationServiceAddResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.ApplicationServiceAdd = applicationServiceAddResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ApplicationServiceRemoveRequest message.
    /// </summary>
    /// <param name="ServiceName">Name of the application service to remove from the list of services supported in the currently opened session.</param>
    /// <returns>ApplicationServiceRemoveRequest message that is ready to be sent.</returns>
    public Message CreateApplicationServiceRemoveRequest(string ServiceName)
    {
      ApplicationServiceRemoveRequest applicationServiceRemoveRequest = new ApplicationServiceRemoveRequest();
      applicationServiceRemoveRequest.ServiceName = ServiceName;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.ApplicationServiceRemove = applicationServiceRemoveRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceRemoveRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceRemoveRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceRemoveResponse message that is ready to be sent.</returns>
    public Message CreateApplicationServiceRemoveResponse(Message Request)
    {
      ApplicationServiceRemoveResponse applicationServiceRemoveResponse = new ApplicationServiceRemoveResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.ApplicationServiceRemove = applicationServiceRemoveResponse;

      return res;
    }



    /// <summary>
    /// Creates a new GetIdentityInformationRequest message.
    /// </summary>
    /// <param name="IdentityId">Identifier of the identity to get information about.</param>
    /// <param name="IncludeProfileImage">true if the caller wants to get the identity's profile image, false otherwise.</param>
    /// <param name="IncludeThumbnailImage">true if the caller wants to get the identity's thumbnail image, false otherwise.</param>
    /// <param name="IncludeApplicationServices">true if the caller wants to get the identity's list of application services, false otherwise.</param>
    /// <returns>GetIdentityInformationRequest message that is ready to be sent.</returns>
    public Message CreateGetIdentityInformationRequest(byte[] IdentityId, bool IncludeProfileImage = false, bool IncludeThumbnailImage = false, bool IncludeApplicationServices = false)
    {
      GetIdentityInformationRequest getIdentityInformationRequest = new GetIdentityInformationRequest();
      getIdentityInformationRequest.IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(IdentityId);
      getIdentityInformationRequest.IncludeProfileImage = IncludeProfileImage;
      getIdentityInformationRequest.IncludeThumbnailImage = IncludeThumbnailImage;
      getIdentityInformationRequest.IncludeApplicationServices = IncludeApplicationServices;

      Message res = CreateSingleRequest();
      res.Request.SingleRequest.GetIdentityInformation = getIdentityInformationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a GetIdentityInformationRequest message.
    /// </summary>
    /// <param name="Request">GetIdentityInformationRequest message for which the response is created.</param>
    /// <param name="IsHosted">True if the requested identity is hosted by this node.</param>
    /// <param name="TargetHomeNodeId">If <paramref name="IsHosted"/> is false, then this is the identifier of the requested identity's new home node, or null if the node does not know network ID of the requested identity's new home node.</param>
    /// <param name="IsOnline">If <paramref name="IsHosted"/> is true, this indicates whether the requested identity is currently online.</param>
    /// <param name="PublicKey">If <paramref name="IsHosted"/> is true, this is the public key of the requested identity.</param>
    /// <param name="Name">If <paramref name="IsHosted"/> is true, this is the name of the requested identity.</param>
    /// <param name="ExtraData">If <paramref name="IsHosted"/> is true, this is the extra data information of the requested identity.</param>
    /// <param name="ProfileImage">If <paramref name="IsHosted"/> is true, this is the identity's profile image, or null if it was not requested.</param>
    /// <param name="ThumbnailImage">If <paramref name="IsHosted"/> is true, this is the identity's thumbnail image, or null if it was not requested.</param>
    /// <param name="ApplicationServices">If <paramref name="IsHosted"/> is true, this is the identity's list of supported application services, or null if it was not requested.</param>
    /// <returns>GetIdentityInformationResponse message that is ready to be sent.</returns>
    public Message CreateGetIdentityInformationResponse(Message Request, bool IsHosted, byte[] TargetHomeNodeId, bool IsOnline = false, byte[] PublicKey = null, string Name = null, string ExtraData = null, byte[] ProfileImage = null, byte[] ThumbnailImage = null, HashSet<string> ApplicationServices = null)
    {
      GetIdentityInformationResponse getIdentityInformationResponse = new GetIdentityInformationResponse();
      getIdentityInformationResponse.IsHosted = IsHosted;
      getIdentityInformationResponse.IsTargetHomeNodeKnown = false;
      if (IsHosted)
      {
        getIdentityInformationResponse.IsOnline = IsOnline;
        getIdentityInformationResponse.IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(PublicKey);
        if (Name != null) getIdentityInformationResponse.Name = Name;
        if (ExtraData != null) getIdentityInformationResponse.ExtraData = ExtraData;
        if (ProfileImage != null) getIdentityInformationResponse.ProfileImage = ProtocolHelper.ByteArrayToByteString(ProfileImage);
        if (ThumbnailImage != null) getIdentityInformationResponse.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(ThumbnailImage);
        if (ApplicationServices != null) getIdentityInformationResponse.ApplicationServices.Add(ApplicationServices);
      }
      else
      {
        getIdentityInformationResponse.IsTargetHomeNodeKnown = TargetHomeNodeId != null;
        if (TargetHomeNodeId != null)
          getIdentityInformationResponse.TargetHomeNodeNetworkId = ProtocolHelper.ByteArrayToByteString(TargetHomeNodeId);
      }

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.GetIdentityInformation = getIdentityInformationResponse;

      return res;
    }

  }
}
