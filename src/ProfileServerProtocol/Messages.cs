using Google.Protobuf;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProfileServerCrypto;
using System.Collections;
using System.Net;

namespace ProfileServerProtocol
{
  /// <summary>
  /// Allows easy construction of IoP Network requests and responses.
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
    public ByteString Version;

    /// <summary>Cryptographic key set representing the identity.</summary>
    private KeysEd25519 keys;

    /// <summary>
    /// Initializes message builder.
    /// </summary>
    /// <param name="IdBase">Base value for message IDs. First message will have ID set to IdBase + 1.</param>
    /// <param name="SupportedVersions">List of supported versions ordered by caller's preference.</param>
    /// <param name="Keys">Cryptographic key set representing the caller's identity.</param>
    public MessageBuilder(uint IdBase, List<SemVer> SupportedVersions, KeysEd25519 Keys)
    {
      idBase = (int)IdBase;
      id = idBase;
      supportedVersions = new List<ByteString>();
      foreach (SemVer version in SupportedVersions)
        supportedVersions.Add(version.ToByteString());

      Version = supportedVersions[0];
      keys = Keys;
    }


    /// <summary>
    /// Sets the version of the protocol that will be used by the message builder.
    /// </summary>
    /// <param name="SelectedVersion">Selected version information.</param>
    public void SetProtocolVersion(SemVer SelectedVersion)
    {
      Version =  SelectedVersion.ToByteString();
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
    /// <param name="Details">Optionally, details about the error to be sent in 'Response.details'.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorRejectedResponse(Message Request, string Details = null)
    {
      Message res = CreateResponse(Request, Status.ErrorRejected);
      if (Details != null)
        res.Response.Details = Details;
      return res;
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
      res.Request.SingleRequest.Version = Version;

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
      SignConversationRequestBodyPart(Message, msg);
    }


    /// <summary>
    /// Signs a part of the request body with identity private key and puts the signature to the ConversationRequest.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationRequest.</param>
    /// <param name="BodyPart">Part of the request to sign.</param>
    public void SignConversationRequestBodyPart(Message Message, byte[] BodyPart)
    {
      byte[] signature = Ed25519.Sign(BodyPart, keys.ExpandedPrivateKey);
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
      return VerifySignedConversationRequestBodyPart(Message, msg, PublicKey);
    }


    /// <summary>
    /// Verifies ConversationRequest.Signature signature of a request body part with a given public key.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationRequest.</param>
    /// <param name="BodyPart">Part of the request body that was signed.</param>
    /// <param name="PublicKey">Public key of the identity that created the signature.</param>
    /// <returns>true if the signature is valid, false otherwise including missing signature.</returns>
    public bool VerifySignedConversationRequestBodyPart(Message Message, byte[] BodyPart, byte[] PublicKey)
    {
      byte[] signature = Message.Request.ConversationRequest.Signature.ToByteArray();

      bool res = Ed25519.Verify(signature, BodyPart, PublicKey);
      return res;
    }


    /// <summary>
    /// Signs a response body with identity private key and puts the signature to the ConversationResponse.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="ResponseBody">Part of the response to sign.</param>
    public void SignConversationResponseBody(Message Message, IMessage ResponseBody)
    {
      byte[] msg = ResponseBody.ToByteArray();
      SignConversationResponseBodyPart(Message, msg);
    }


    /// <summary>
    /// Signs a part of the response body with identity private key and puts the signature to the ConversationResponse.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="BodyPart">Part of the response to sign.</param>
    public void SignConversationResponseBodyPart(Message Message, byte[] BodyPart)
    {
      byte[] signature = Ed25519.Sign(BodyPart, keys.ExpandedPrivateKey);
      Message.Response.ConversationResponse.Signature = ProtocolHelper.ByteArrayToByteString(signature);
    }


    /// <summary>
    /// Verifies ConversationResponse.Signature signature of a response body with a given public key.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="ResponseBody">Part of the request that was signed.</param>
    /// <param name="PublicKey">Public key of the identity that created the signature.</param>
    /// <returns>true if the signature is valid, false otherwise including missing signature.</returns>
    public bool VerifySignedConversationResponseBody(Message Message, IMessage ResponseBody, byte[] PublicKey)
    {
      byte[] msg = ResponseBody.ToByteArray();
      return VerifySignedConversationResponseBodyPart(Message, msg, PublicKey);
    }


    /// <summary>
    /// Verifies ConversationResponse.Signature signature of a response body part with a given public key.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="BodyPart">Part of the response body that was signed.</param>
    /// <param name="PublicKey">Public key of the identity that created the signature.</param>
    /// <returns>true if the signature is valid, false otherwise including missing signature.</returns>
    public bool VerifySignedConversationResponseBodyPart(Message Message, byte[] BodyPart, byte[] PublicKey)
    {
      byte[] signature = Message.Response.ConversationResponse.Signature.ToByteArray();

      bool res = Ed25519.Verify(signature, BodyPart, PublicKey);
      return res;
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
    /// <param name="Challenge">Client's generated challenge data for server's authentication.</param>
    /// <returns>StartConversationRequest message that is ready to be sent.</returns>
    public Message CreateStartConversationRequest(byte[] Challenge)
    {
      StartConversationRequest startConversationRequest = new StartConversationRequest();
      startConversationRequest.SupportedVersions.Add(supportedVersions);

      startConversationRequest.PublicKey = ProtocolHelper.ByteArrayToByteString(keys.PublicKey);
      startConversationRequest.ClientChallenge = ProtocolHelper.ByteArrayToByteString(Challenge);

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
    /// <param name="Challenge">ClientChallenge from StartConversationRequest that the server received from the client.</param>
    /// <returns>StartConversationResponse message that is ready to be sent.</returns>
    public Message CreateStartConversationResponse(Message Request, SemVer Version, byte[] PublicKey, byte[] Challenge, byte[] ClientChallenge)
    {
      StartConversationResponse startConversationResponse = new StartConversationResponse();
      startConversationResponse.Version = Version.ToByteString();
      startConversationResponse.PublicKey = ProtocolHelper.ByteArrayToByteString(PublicKey);
      startConversationResponse.Challenge = ProtocolHelper.ByteArrayToByteString(Challenge);
      startConversationResponse.ClientChallenge = ProtocolHelper.ByteArrayToByteString(ClientChallenge);

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.Start = startConversationResponse;

      SignConversationResponseBodyPart(res, ClientChallenge);

      return res;
    }



    /// <summary>
    /// Creates a new RegisterHostingRequest message.
    /// </summary>
    /// <param name="Contract">Hosting contract for one of the profile server's plan to base the hosting agreement on.</param>
    /// <returns>RegisterHostingRequest message that is ready to be sent.</returns>
    public Message CreateRegisterHostingRequest(HostingPlanContract Contract)
    {
      RegisterHostingRequest registerHostingRequest = new RegisterHostingRequest();
      registerHostingRequest.Contract = Contract;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.RegisterHosting = registerHostingRequest;

      SignConversationRequestBody(res, registerHostingRequest);
      return res;
    }


    /// <summary>
    /// Creates a response message to a RegisterHostingRequest message.
    /// </summary>
    /// <param name="Request">RegisterHostingRequest message for which the response is created.</param>
    /// <param name="Contract">Contract copy from RegisterHostingRequest.Contract.</param>
    /// <returns>RegisterHostingResponse message that is ready to be sent.</returns>
    public Message CreateRegisterHostingResponse(Message Request, HostingPlanContract Contract)
    {
      RegisterHostingResponse registerHostingResponse = new RegisterHostingResponse();
      registerHostingResponse.Contract = Contract;

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.RegisterHosting = registerHostingResponse;

      SignConversationResponseBody(res, registerHostingResponse);

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
    /// <param name="Image">Profile image data or null if profile image is not to be changed, to erase image, use empty byte array.</param>
    /// <param name="Location">Profile location information or null if location is not to be changed.</param>
    /// <param name="ExtraData">Profile's extra data information or null if profile's extra data is not to be changed.</param>
    /// <returns>CreateUpdateProfileRequest message that is ready to be sent.</returns>
    public Message CreateUpdateProfileRequest(SemVer? Version, string Name, byte[] Image, GpsLocation Location, string ExtraData)
    {
      UpdateProfileRequest updateProfileRequest = new UpdateProfileRequest();
      updateProfileRequest.SetVersion = Version != null;
      updateProfileRequest.SetName = Name != null;
      updateProfileRequest.SetImage = Image != null;
      updateProfileRequest.SetLocation = Location != null;
      updateProfileRequest.SetExtraData = ExtraData != null;

      if (updateProfileRequest.SetVersion)
        updateProfileRequest.Version = Version.Value.ToByteString();

      if (updateProfileRequest.SetName)
        updateProfileRequest.Name = Name;

      if (updateProfileRequest.SetImage)
        updateProfileRequest.Image = ProtocolHelper.ByteArrayToByteString(Image);

      if (updateProfileRequest.SetLocation)
      {
        updateProfileRequest.Latitude = Location.GetLocationTypeLatitude();
        updateProfileRequest.Longitude = Location.GetLocationTypeLongitude();
      }

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
    /// Creates a new CancelHostingAgreementRequest message.
    /// </summary>
    /// <param name="NewProfileServerId">Network identifier of the identity's new profile server, or null if this information is not to be sent to the previous profile server.</param>
    /// <returns>CancelHostingAgreementRequest message that is ready to be sent.</returns>
    public Message CreateCancelHostingAgreementRequest(byte[] NewProfileServerId)
    {
      CancelHostingAgreementRequest cancelHostingAgreementRequest = new CancelHostingAgreementRequest();
      cancelHostingAgreementRequest.RedirectToNewProfileServer = NewProfileServerId != null;
      if (cancelHostingAgreementRequest.RedirectToNewProfileServer)
        cancelHostingAgreementRequest.NewProfileServerNetworkId = ProtocolHelper.ByteArrayToByteString(NewProfileServerId);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.CancelHostingAgreement = cancelHostingAgreementRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CancelHostingAgreementRequest message.
    /// </summary>
    /// <param name="Request">CancelHostingAgreementRequest message for which the response is created.</param>
    /// <returns>CancelHostingAgreementResponse message that is ready to be sent.</returns>
    public Message CreateCancelHostingAgreementResponse(Message Request)
    {
      CancelHostingAgreementResponse cancelHostingAgreementResponse = new CancelHostingAgreementResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.CancelHostingAgreement = cancelHostingAgreementResponse;

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
    /// <param name="IsHosted">True if the requested identity is hosted by this profile server.</param>
    /// <param name="TargetProfileServerId">If <paramref name="IsHosted"/> is false, then this is the identifier of the requested identity's new profile server, or null if the profile server does not know network ID of the requested identity's new profile server.</param>
    /// <param name="Version">If <paramref name="IsHosted"/> is true, this is version of the identity's profile structure.</param>
    /// <param name="IsOnline">If <paramref name="IsHosted"/> is true, this indicates whether the requested identity is currently online.</param>
    /// <param name="PublicKey">If <paramref name="IsHosted"/> is true, this is the public key of the requested identity.</param>
    /// <param name="Name">If <paramref name="IsHosted"/> is true, this is the name of the requested identity.</param>
    /// <param name="Type">If <paramref name="IsHosted"/> is true, this is the type of the requested identity.</param>
    /// <param name="Location">If <paramref name="IsHosted"/> is true, this is GPS location information of the requested identity.</param>
    /// <param name="ExtraData">If <paramref name="IsHosted"/> is true, this is the extra data information of the requested identity.</param>
    /// <param name="ProfileImage">If <paramref name="IsHosted"/> is true, this is the identity's profile image, or null if it was not requested.</param>
    /// <param name="ThumbnailImage">If <paramref name="IsHosted"/> is true, this is the identity's thumbnail image, or null if it was not requested.</param>
    /// <param name="ApplicationServices">If <paramref name="IsHosted"/> is true, this is the identity's list of supported application services, or null if it was not requested.</param>
    /// <returns>GetIdentityInformationResponse message that is ready to be sent.</returns>
    public Message CreateGetIdentityInformationResponse(Message Request, bool IsHosted, byte[] TargetProfileServerId, SemVer? Version = null, bool IsOnline = false, byte[] PublicKey = null, string Name = null, string Type = null, GpsLocation Location = null, string ExtraData = null, byte[] ProfileImage = null, byte[] ThumbnailImage = null, HashSet<string> ApplicationServices = null)
    {
      GetIdentityInformationResponse getIdentityInformationResponse = new GetIdentityInformationResponse();
      getIdentityInformationResponse.IsHosted = IsHosted;
      getIdentityInformationResponse.IsTargetProfileServerKnown = false;
      if (IsHosted)
      {
        getIdentityInformationResponse.IsOnline = IsOnline;
        getIdentityInformationResponse.Version = Version.Value.ToByteString();
        getIdentityInformationResponse.IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(PublicKey);
        if (Name != null) getIdentityInformationResponse.Name = Name;
        if (Type != null) getIdentityInformationResponse.Type = Type;
        if (Location != null)
        {
          getIdentityInformationResponse.Latitude = Location.GetLocationTypeLatitude();
          getIdentityInformationResponse.Longitude = Location.GetLocationTypeLongitude();
        }
        if (ExtraData != null) getIdentityInformationResponse.ExtraData = ExtraData;
        if (ProfileImage != null) getIdentityInformationResponse.ProfileImage = ProtocolHelper.ByteArrayToByteString(ProfileImage);
        if (ThumbnailImage != null) getIdentityInformationResponse.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(ThumbnailImage);
        if (ApplicationServices != null) getIdentityInformationResponse.ApplicationServices.Add(ApplicationServices);
      }
      else
      {
        getIdentityInformationResponse.IsTargetProfileServerKnown = TargetProfileServerId != null;
        if (TargetProfileServerId != null)
          getIdentityInformationResponse.TargetProfileServerNetworkId = ProtocolHelper.ByteArrayToByteString(TargetProfileServerId);
      }

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.GetIdentityInformation = getIdentityInformationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new CallIdentityApplicationServiceRequest message.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the callee's identity.</param>
    /// <param name="ServiceName">Name of the application service to use for the call.</param>
    /// <returns>CallIdentityApplicationServiceRequest message that is ready to be sent.</returns>
    public Message CreateCallIdentityApplicationServiceRequest(byte[] IdentityId, string ServiceName)
    {
      CallIdentityApplicationServiceRequest callIdentityApplicationServiceRequest = new CallIdentityApplicationServiceRequest();
      callIdentityApplicationServiceRequest.IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(IdentityId);
      callIdentityApplicationServiceRequest.ServiceName = ServiceName;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.CallIdentityApplicationService = callIdentityApplicationServiceRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CallIdentityApplicationServiceRequest message.
    /// </summary>
    /// <param name="Request">CallIdentityApplicationServiceRequest message for which the response is created.</param>
    /// <param name="CallerToken">Token issued for the caller for clAppService connection.</param>
    /// <returns>CallIdentityApplicationServiceResponse message that is ready to be sent.</returns>
    public Message CreateCallIdentityApplicationServiceResponse(Message Request, byte[] CallerToken)
    {
      CallIdentityApplicationServiceResponse callIdentityApplicationServiceResponse = new CallIdentityApplicationServiceResponse();
      callIdentityApplicationServiceResponse.CallerToken = ProtocolHelper.ByteArrayToByteString(CallerToken);

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.CallIdentityApplicationService = callIdentityApplicationServiceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new IncomingCallNotificationRequest message.
    /// </summary>
    /// <param name="CallerPublicKey">Public key of the caller.</param>
    /// <param name="ServiceName">Name of the application service the caller wants to use.</param>
    /// <param name="CalleeToken">Token issued for the callee for clAppService connection.</param>
    /// <returns>IncomingCallNotificationRequest message that is ready to be sent.</returns>
    public Message CreateIncomingCallNotificationRequest(byte[] CallerPublicKey, string ServiceName, byte[] CalleeToken)
    {
      IncomingCallNotificationRequest incomingCallNotificationRequest = new IncomingCallNotificationRequest();
      incomingCallNotificationRequest.CallerPublicKey = ProtocolHelper.ByteArrayToByteString(CallerPublicKey);
      incomingCallNotificationRequest.ServiceName = ServiceName;
      incomingCallNotificationRequest.CalleeToken = ProtocolHelper.ByteArrayToByteString(CalleeToken);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.IncomingCallNotification = incomingCallNotificationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a IncomingCallNotificationRequest message.
    /// </summary>
    /// <param name="Request">IncomingCallNotificationRequest message for which the response is created.</param>
    /// <returns>IncomingCallNotificationResponse message that is ready to be sent.</returns>
    public Message CreateIncomingCallNotificationResponse(Message Request)
    {
      IncomingCallNotificationResponse incomingCallNotificationResponse = new IncomingCallNotificationResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.IncomingCallNotification = incomingCallNotificationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ApplicationServiceSendMessageRequest message.
    /// </summary>
    /// <param name="Token">Client's token for clAppService connection.</param>
    /// <param name="Message">Message to be sent to the other peer, or null for channel initialization message.</param>
    /// <returns>ApplicationServiceSendMessageRequest message that is ready to be sent.</returns>
    public Message CreateApplicationServiceSendMessageRequest(byte[] Token, byte[] Message)
    {
      ApplicationServiceSendMessageRequest applicationServiceSendMessageRequest = new ApplicationServiceSendMessageRequest();
      applicationServiceSendMessageRequest.Token = ProtocolHelper.ByteArrayToByteString(Token);
      if (Message != null)
        applicationServiceSendMessageRequest.Message = ProtocolHelper.ByteArrayToByteString(Message);

      Message res = CreateSingleRequest();
      res.Request.SingleRequest.ApplicationServiceSendMessage = applicationServiceSendMessageRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceSendMessageRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceSendMessageRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceSendMessageResponse message that is ready to be sent.</returns>
    public Message CreateApplicationServiceSendMessageResponse(Message Request)
    {
      ApplicationServiceSendMessageResponse applicationServiceSendMessageResponse = new ApplicationServiceSendMessageResponse();

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.ApplicationServiceSendMessage = applicationServiceSendMessageResponse;

      return res;
    }



    /// <summary>
    /// Creates a new ApplicationServiceReceiveMessageNotificationRequest message.
    /// </summary>
    /// <param name="Message">Message sent by the other peer.</param>
    /// <returns>ApplicationServiceReceiveMessageNotificationRequest message that is ready to be sent.</returns>
    public Message CreateApplicationServiceReceiveMessageNotificationRequest(byte[] Message)
    {
      ApplicationServiceReceiveMessageNotificationRequest applicationServiceReceiveMessageNotificationRequest = new ApplicationServiceReceiveMessageNotificationRequest();
      applicationServiceReceiveMessageNotificationRequest.Message = ProtocolHelper.ByteArrayToByteString(Message);

      Message res = CreateSingleRequest();
      res.Request.SingleRequest.ApplicationServiceReceiveMessageNotification = applicationServiceReceiveMessageNotificationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceReceiveMessageNotificationRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceReceiveMessageNotificationRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceReceiveMessageNotificationResponse message that is ready to be sent.</returns>
    public Message CreateApplicationServiceReceiveMessageNotificationResponse(Message Request)
    {
      ApplicationServiceReceiveMessageNotificationResponse applicationServiceReceiveMessageNotificationResponse = new ApplicationServiceReceiveMessageNotificationResponse();

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.ApplicationServiceReceiveMessageNotification = applicationServiceReceiveMessageNotificationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ProfileStatsRequest message.
    /// </summary>
    /// <returns>ProfileStatsRequest message that is ready to be sent.</returns>
    public Message CreateProfileStatsRequest()
    {
      ProfileStatsRequest profileStatsRequest = new ProfileStatsRequest();

      Message res = CreateSingleRequest();
      res.Request.SingleRequest.ProfileStats = profileStatsRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ProfileStatsRequest message.
    /// </summary>
    /// <param name="Request">ProfileStatsRequest message for which the response is created.</param>
    /// <param name="Stats">List of profile statistics.</param>
    /// <returns>ProfileStatsResponse message that is ready to be sent.</returns>
    public Message CreateProfileStatsResponse(Message Request, IEnumerable<ProfileStatsItem> Stats)
    {
      ProfileStatsResponse profileStatsResponse = new ProfileStatsResponse();
      if ((Stats != null) && (Stats.Count() > 0))
        profileStatsResponse.Stats.AddRange(Stats);

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.ProfileStats = profileStatsResponse;

      return res;
    }




    /// <summary>
    /// Creates a new ProfileSearchRequest message.
    /// </summary>
    /// <param name="IdentityType">Wildcard string filter for identity type. If filtering by identity type is not required this is set to null.</param>
    /// <param name="Name">Wildcard string filter for profile name. If filtering by profile name is not required this is set to null.</param>
    /// <param name="ExtraData">Regular expression string filter for profile's extra data information. If filtering by extra data information is not required this is set to null.</param>
    /// <param name="Location">GPS location, near which the target identities has to be located. If no location filtering is required this is set to null.</param>
    /// <param name="Radius">If <paramref name="Location"/> is not 0, this is radius in metres that together with <paramref name="Location"/> defines the target area.</param>
    /// <param name="MaxResponseRecordCount">Maximal number of results to be included in the response. This is an integer between 1 and 100 if <paramref name="IncludeThumnailImages"/> is true, otherwise this is integer between 1 and 1000.</param>
    /// <param name="MaxTotalRecordCount">Maximal number of total results that the profile server will look for and save. This is an integer between 1 and 1000 if <paramref name="IncludeThumnailImages"/> is true, otherwise this is integer between 1 and 10000.</param>
    /// <param name="IncludeHostedOnly">If set to true, the profile server only returns profiles of its own hosted identities. Otherwise, identities from the profile server's neighborhood can be included.</param>
    /// <param name="IncludeThumbnailImages">If set to true, the response will include a thumbnail image of each profile.</param>
    /// <returns>ProfileSearchRequest message that is ready to be sent.</returns>
    public Message CreateProfileSearchRequest(string IdentityType, string Name, string ExtraData, GpsLocation Location = null, uint Radius = 0, uint MaxResponseRecordCount = 100, uint MaxTotalRecordCount = 1000, bool IncludeHostedOnly = false, bool IncludeThumbnailImages = true)
    {
      ProfileSearchRequest profileSearchRequest = new ProfileSearchRequest();
      profileSearchRequest.IncludeHostedOnly = IncludeHostedOnly;
      profileSearchRequest.IncludeThumbnailImages = IncludeThumbnailImages;
      profileSearchRequest.MaxResponseRecordCount = MaxResponseRecordCount;
      profileSearchRequest.MaxTotalRecordCount = MaxTotalRecordCount;
      profileSearchRequest.Type = IdentityType != null ? IdentityType : "";
      profileSearchRequest.Name = Name != null ? Name : "";
      profileSearchRequest.Latitude = Location != null ? Location.GetLocationTypeLatitude() : GpsLocation.NoLocationLocationType;
      profileSearchRequest.Longitude = Location != null ? Location.GetLocationTypeLongitude() : GpsLocation.NoLocationLocationType;
      profileSearchRequest.Radius = Location != null ? Radius : 0;
      profileSearchRequest.ExtraData = ExtraData != null ? ExtraData : "";

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.ProfileSearch = profileSearchRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ProfileSearchRequest message.
    /// </summary>
    /// <param name="Request">ProfileSearchRequest message for which the response is created.</param>
    /// <param name="TotalRecordCount">Total number of profiles that matched the search criteria.</param>
    /// <param name="MaxResponseRecordCount">Limit of the number of results provided.</param>
    /// <param name="CoveredServers">List of profile servers whose profile databases were be used to produce the result.</param>
    /// <param name="Results">List of results that contains up to <paramref name="MaxRecordCount"/> items.</param>
    /// <returns>ProfileSearchResponse message that is ready to be sent.</returns>
    public Message CreateProfileSearchResponse(Message Request, uint TotalRecordCount, uint MaxResponseRecordCount, IEnumerable<byte[]> CoveredServers, IEnumerable<IdentityNetworkProfileInformation> Results)
    {
      ProfileSearchResponse profileSearchResponse = new ProfileSearchResponse();
      profileSearchResponse.TotalRecordCount = TotalRecordCount;
      profileSearchResponse.MaxResponseRecordCount = MaxResponseRecordCount;

      foreach (byte[] coveredServers in CoveredServers)
        profileSearchResponse.CoveredServers.Add(ProtocolHelper.ByteArrayToByteString(coveredServers));

      if ((Results != null) && (Results.Count() > 0))
        profileSearchResponse.Profiles.AddRange(Results);
      
      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.ProfileSearch = profileSearchResponse;

      return res;
    }



    /// <summary>
    /// Creates a new ProfileSearchPartRequest message.
    /// </summary>
    /// <param name="RecordIndex">Zero-based index of the first result to retrieve.</param>
    /// <param name="RecordCount">Number of results to retrieve. If 'ProfileSearchResponse.IncludeThumbnailImages' was set, this has to be an integer between 1 and 100, otherwise it has to be an integer between 1 and 1000.</param>
    /// <returns>ProfileSearchRequest message that is ready to be sent.</returns>
    public Message CreateProfileSearchPartRequest(uint RecordIndex, uint RecordCount)
    {
      ProfileSearchPartRequest profileSearchPartRequest = new ProfileSearchPartRequest();
      profileSearchPartRequest.RecordIndex = RecordIndex;
      profileSearchPartRequest.RecordCount= RecordCount;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.ProfileSearchPart = profileSearchPartRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ProfileSearchPartRequest message.
    /// </summary>
    /// <param name="Request">ProfileSearchPartRequest message for which the response is created.</param>
    /// <param name="RecordIndex">Index of the first result.</param>
    /// <param name="RecordCount">Number of results.</param>
    /// <param name="Results">List of results that contains <paramref name="RecordCount"/> items.</param>
    /// <returns>ProfileSearchPartResponse message that is ready to be sent.</returns>
    public Message CreateProfileSearchPartResponse(Message Request, uint RecordIndex, uint RecordCount, IEnumerable<IdentityNetworkProfileInformation> Results)
    {
      ProfileSearchPartResponse profileSearchPartResponse = new ProfileSearchPartResponse();
      profileSearchPartResponse.RecordIndex = RecordIndex;
      profileSearchPartResponse.RecordCount = RecordCount;
      profileSearchPartResponse.Profiles.AddRange(Results);

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.ProfileSearchPart = profileSearchPartResponse;

      return res;
    }


    /// <summary>
    /// Creates a new AddRelatedIdentityRequest message.
    /// </summary>
    /// <param name="CardApplication">Description of the relationship proven by the signed relationship card.</param>
    /// <param name="SignedCard">Signed relationship card.</param>
    /// <returns>AddRelatedIdentityRequest message that is ready to be sent.</returns>
    public Message CreateAddRelatedIdentityRequest(CardApplicationInformation CardApplication, SignedRelationshipCard SignedCard)
    {
      AddRelatedIdentityRequest addRelatedIdentityRequest = new AddRelatedIdentityRequest();
      addRelatedIdentityRequest.CardApplication = CardApplication;
      addRelatedIdentityRequest.SignedCard = SignedCard;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.AddRelatedIdentity = addRelatedIdentityRequest;

      SignConversationRequestBodyPart(res, CardApplication.ToByteArray());
      return res;
    }



    /// <summary>
    /// Creates a response message to a AddRelatedIdentityRequest message.
    /// </summary>
    /// <param name="Request">AddRelatedIdentityRequest message for which the response is created.</param>
    /// <returns>AddRelatedIdentityResponse message that is ready to be sent.</returns>
    public Message CreateAddRelatedIdentityResponse(Message Request)
    {
      AddRelatedIdentityResponse addRelatedIdentityResponse = new AddRelatedIdentityResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.AddRelatedIdentity = addRelatedIdentityResponse;

      return res;
    }


    /// <summary>
    /// Creates a new RemoveRelatedIdentityRequest message.
    /// </summary>
    /// <param name="CardApplicationIdentifier">Identifier of the card application to remove.</param>
    /// <returns>RemoveRelatedIdentityRequest message that is ready to be sent.</returns>
    public Message CreateRemoveRelatedIdentityRequest(byte[] CardApplicationIdentifier)
    {
      RemoveRelatedIdentityRequest removeRelatedIdentityRequest = new RemoveRelatedIdentityRequest();
      removeRelatedIdentityRequest.ApplicationId = ProtocolHelper.ByteArrayToByteString(CardApplicationIdentifier);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.RemoveRelatedIdentity = removeRelatedIdentityRequest;

      return res;
    }



    /// <summary>
    /// Creates a response message to a RemoveRelatedIdentityRequest message.
    /// </summary>
    /// <param name="Request">RemoveRelatedIdentityRequest message for which the response is created.</param>
    /// <returns>RemoveRelatedIdentityResponse message that is ready to be sent.</returns>
    public Message CreateRemoveRelatedIdentityResponse(Message Request)
    {
      RemoveRelatedIdentityResponse removeRelatedIdentityResponse = new RemoveRelatedIdentityResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.RemoveRelatedIdentity = removeRelatedIdentityResponse;

      return res;
    }



    /// <summary>
    /// Creates a new GetIdentityRelationshipsInformationRequest message.
    /// </summary>
    /// <param name="IdentityNetworkId">Identity's network identifier.</param>
    /// <param name="IncludeInvalid">If set to true, the response may include relationships which cards are no longer valid or not yet valid.</param>
    /// <param name="CardType">Wildcard string filter for card type. If filtering by card type name is not required this is set to null.</param>
    /// <param name="IssuerPublicKey">Network identifier of the card issuer whose relationships with the target identity are being queried.</param>
    /// <returns>GetIdentityRelationshipsInformationRequest message that is ready to be sent.</returns>
    public Message CreateGetIdentityRelationshipsInformationRequest(byte[] IdentityNetworkId, bool IncludeInvalid, string CardType, byte[] IssuerNetworkId)
    {
      GetIdentityRelationshipsInformationRequest getIdentityRelationshipsInformationRequest = new GetIdentityRelationshipsInformationRequest();
      getIdentityRelationshipsInformationRequest.IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(IdentityNetworkId);
      getIdentityRelationshipsInformationRequest.IncludeInvalid = IncludeInvalid;
      getIdentityRelationshipsInformationRequest.Type = CardType != null ? CardType : "";
      getIdentityRelationshipsInformationRequest.SpecificIssuer = IssuerNetworkId != null;
      if (IssuerNetworkId != null)
        getIdentityRelationshipsInformationRequest.IssuerNetworkId = ProtocolHelper.ByteArrayToByteString(IssuerNetworkId);

      Message res = CreateSingleRequest();
      res.Request.SingleRequest.GetIdentityRelationshipsInformation = getIdentityRelationshipsInformationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a GetIdentityRelationshipsInformationRequest message.
    /// </summary>
    /// <param name="Request">GetIdentityRelationshipsInformationRequest message for which the response is created.</param>
    /// <param name="Stats">List of profile statistics.</param>
    /// <returns>GetIdentityRelationshipsInformationResponse message that is ready to be sent.</returns>
    public Message CreateGetIdentityRelationshipsInformationResponse(Message Request, IEnumerable<IdentityRelationship> Relationships)
    {
      GetIdentityRelationshipsInformationResponse getIdentityRelationshipsInformationResponse = new GetIdentityRelationshipsInformationResponse();
      getIdentityRelationshipsInformationResponse.Relationships.AddRange(Relationships);

      Message res = CreateSingleResponse(Request);
      res.Response.SingleResponse.GetIdentityRelationshipsInformation = getIdentityRelationshipsInformationResponse;

      return res;
    }



    /// <summary>
    /// Creates a new StartNeighborhoodInitializationRequest message.
    /// </summary>
    /// <param name="PrimaryPort">Primary interface port of the requesting profile server.</param>
    /// <param name="SrNeighborPort">Neighbors interface port of the requesting profile server.</param>
    /// <param name="IpAddress">External IP address of the requesting profile server.</param>
    /// <returns>StartNeighborhoodInitializationRequest message that is ready to be sent.</returns>
    public Message CreateStartNeighborhoodInitializationRequest(uint PrimaryPort, uint SrNeighborPort, IPAddress IpAddress)
    {
      StartNeighborhoodInitializationRequest startNeighborhoodInitializationRequest = new StartNeighborhoodInitializationRequest();
      startNeighborhoodInitializationRequest.PrimaryPort = PrimaryPort;
      startNeighborhoodInitializationRequest.SrNeighborPort = SrNeighborPort;
      startNeighborhoodInitializationRequest.IpAddress = ProtocolHelper.ByteArrayToByteString(IpAddress.GetAddressBytes());

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.StartNeighborhoodInitialization = startNeighborhoodInitializationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a StartNeighborhoodInitializationRequest message.
    /// </summary>
    /// <param name="Request">StartNeighborhoodInitializationRequest message for which the response is created.</param>
    /// <returns>StartNeighborhoodInitializationResponse message that is ready to be sent.</returns>
    public Message CreateStartNeighborhoodInitializationResponse(Message Request)
    {
      StartNeighborhoodInitializationResponse startNeighborhoodInitializationResponse = new StartNeighborhoodInitializationResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.StartNeighborhoodInitialization = startNeighborhoodInitializationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new FinishNeighborhoodInitializationRequest message.
    /// </summary>
    /// <returns>FinishNeighborhoodInitializationRequest message that is ready to be sent.</returns>
    public Message CreateFinishNeighborhoodInitializationRequest()
    {
      FinishNeighborhoodInitializationRequest finishNeighborhoodInitializationRequest = new FinishNeighborhoodInitializationRequest();

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.FinishNeighborhoodInitialization = finishNeighborhoodInitializationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a FinishNeighborhoodInitializationRequest message.
    /// </summary>
    /// <param name="Request">FinishNeighborhoodInitializationRequest message for which the response is created.</param>
    /// <returns>FinishNeighborhoodInitializationResponse message that is ready to be sent.</returns>
    public Message CreateFinishNeighborhoodInitializationResponse(Message Request)
    {
      FinishNeighborhoodInitializationResponse finishNeighborhoodInitializationResponse = new FinishNeighborhoodInitializationResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.FinishNeighborhoodInitialization = finishNeighborhoodInitializationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new NeighborhoodSharedProfileUpdateRequest message.
    /// </summary>
    /// <param name="Items">List of profile changes to share.</param>
    /// <returns>NeighborhoodSharedProfileUpdateRequest message that is ready to be sent.</returns>
    public Message CreateNeighborhoodSharedProfileUpdateRequest(IEnumerable<SharedProfileUpdateItem> Items = null)
    {
      NeighborhoodSharedProfileUpdateRequest neighborhoodSharedProfileUpdateRequest = new NeighborhoodSharedProfileUpdateRequest();
      if (Items != null) neighborhoodSharedProfileUpdateRequest.Items.AddRange(Items);

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.NeighborhoodSharedProfileUpdate = neighborhoodSharedProfileUpdateRequest;
      
      return res;
    }


    /// <summary>
    /// Creates a response message to a NeighborhoodSharedProfileUpdateRequest message.
    /// </summary>
    /// <param name="Request">NeighborhoodSharedProfileUpdateRequest message for which the response is created.</param>
    /// <returns>NeighborhoodSharedProfileUpdateResponse message that is ready to be sent.</returns>
    public Message CreateNeighborhoodSharedProfileUpdateResponse(Message Request)
    {
      NeighborhoodSharedProfileUpdateResponse neighborhoodSharedProfileUpdateResponse = new NeighborhoodSharedProfileUpdateResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.NeighborhoodSharedProfileUpdate = neighborhoodSharedProfileUpdateResponse;

      return res;
    }


    /// <summary>
    /// Creates a new StopNeighborhoodUpdatesRequest message.
    /// </summary>
    /// <returns>StopNeighborhoodUpdatesRequest message that is ready to be sent.</returns>
    public Message CreateStopNeighborhoodUpdatesRequest()
    {
      StopNeighborhoodUpdatesRequest stopNeighborhoodUpdatesRequest = new StopNeighborhoodUpdatesRequest();

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.StopNeighborhoodUpdates = stopNeighborhoodUpdatesRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a StopNeighborhoodUpdatesRequest message.
    /// </summary>
    /// <param name="Request">StopNeighborhoodUpdatesRequest message for which the response is created.</param>
    /// <returns>StopNeighborhoodUpdatesResponse message that is ready to be sent.</returns>
    public Message CreateStopNeighborhoodUpdatesResponse(Message Request)
    {
      StopNeighborhoodUpdatesResponse stopNeighborhoodUpdatesResponse = new StopNeighborhoodUpdatesResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.StopNeighborhoodUpdates = stopNeighborhoodUpdatesResponse;

      return res;
    }



    /// <summary>
    /// Creates a new CanStoreDataRequest message.
    /// </summary>
    /// <param name="Data">Data to store in CAN, or null to just delete the old object.</param>
    /// <returns>CanStoreDataRequest message that is ready to be sent.</returns>
    public Message CreateCanStoreDataRequest(CanIdentityData Data)
    {
      CanStoreDataRequest canStoreDataRequest = new CanStoreDataRequest();
      canStoreDataRequest.Data = Data;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.CanStoreData = canStoreDataRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CanStoreDataRequest message.
    /// </summary>
    /// <param name="Request">CanStoreDataRequest message for which the response is created.</param>
    /// <param name="Hash">Hash of 'CanStoreDataRequest.data' received from CAN, or null if 'CanStoreDataRequest.data' was null.</param>
    /// <returns>CanStoreDataResponse message that is ready to be sent.</returns>
    public Message CreateCanStoreDataResponse(Message Request, byte[] Hash)
    {
      CanStoreDataResponse canStoreDataResponse = new CanStoreDataResponse();
      if (Hash != null) canStoreDataResponse.Hash = ProtocolHelper.ByteArrayToByteString(Hash);

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.CanStoreData = canStoreDataResponse;

      return res;
    }


    /// <summary>
    /// Creates a new CanPublishIpnsRecordRequest message.
    /// </summary>
    /// <param name="Record">Signed IPNS record.</param>
    /// <returns>CanPublishIpnsRecordRequest message that is ready to be sent.</returns>
    public Message CreateCanPublishIpnsRecordRequest(CanIpnsEntry Record)
    {
      CanPublishIpnsRecordRequest canPublishIpnsRecordRequest = new CanPublishIpnsRecordRequest();
      canPublishIpnsRecordRequest.Record = Record;

      Message res = CreateConversationRequest();
      res.Request.ConversationRequest.CanPublishIpnsRecord = canPublishIpnsRecordRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CanPublishIpnsRecordRequest message.
    /// </summary>
    /// <param name="Request">CanPublishIpnsRecordRequest message for which the response is created.</param>
    /// <returns>CanPublishIpnsRecordResponse message that is ready to be sent.</returns>
    public Message CreateCanPublishIpnsRecordResponse(Message Request)
    {
      CanPublishIpnsRecordResponse canPublishIpnsRecordResponse = new CanPublishIpnsRecordResponse();

      Message res = CreateConversationResponse(Request);
      res.Response.ConversationResponse.CanPublishIpnsRecord = canPublishIpnsRecordResponse;

      return res;
    }
  }
}
