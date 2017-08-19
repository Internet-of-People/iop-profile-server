using Google.Protobuf;
using Iop.Proximityserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IopCrypto;
using System.Collections;
using IopCommon;
using System.Net;
using Iop.Shared;

namespace IopProtocol
{
  /// <summary>
  /// Representation of the protocol message in IoP Proximity Server Network.
  /// </summary>
  class ProxProtocolMessage : IProtocolMessage<Message>
  {
    /// <summary>Protocol specific message.</summary>
    public Message Message { get; }

    /// <summary>Unique message identifier within a session.</summary>
    public uint Id { get { return Message.Id; } }


    /// <summary>
    /// Initializes instance of the object using an existing Protobuf message.
    /// </summary>
    /// <param name="Message">Protobuf Proximity Server Network message.</param>
    public ProxProtocolMessage(Message Message)
    {
      this.Message = Message;
    }


    public override string ToString()
    {
      return Message.ToString();
    }
  }

  
  /// <summary>
  /// Allows easy construction of IoP Proximity Server Network requests and responses.
  /// </summary>
  public class ProxMessageBuilder
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("IopProtocol.ProxMessageBuilder");

    /// <summary>Size in bytes of an authentication challenge data.</summary>
    public const int ChallengeDataSize = 32;

    /// <summary>Threshold within the proximity server accepts the update of an activity even if it is not the closest proximity server to its new location.
    /// Let D be the distance of the proximity server to the activity's new location and let K be distance of the closest neighbor server to that location.
    /// Then the threshold value T means that the proximity server will accept the activity update if D is less than or equal to K * (1 + T).</summary>
    public const double ActivityMigrationDistanceTolerance = 0.10;

    /// <summary>Maximum number of bytes that type field in ActivitySearchRequest can occupy.</summary>
    public const int MaxActivitySearchTypeLengthBytes = 64;

    /// <summary>Maximum number of bytes that extraData field in ActivitySearchRequest can occupy.</summary>
    public const int MaxActivitySearchExtraDataLengthBytes = 256;


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
    /// <param name="SupportedVersions">List of supported versions ordered by caller's preference.</param>
    /// <param name="Keys">Cryptographic key set representing the caller's identity.</param>
    public ProxMessageBuilder(uint IdBase, List<SemVer> SupportedVersions, KeysEd25519 Keys)
    {
      idBase = (int)IdBase;
      id = idBase;
      supportedVersions = SupportedVersions
        .Select(v => v.ToByteString())
        .ToList();
      version = supportedVersions[0];
      keys = Keys;
    }


    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public static IProtocolMessage<Message> CreateMessageFromRawData(byte[] Data)
    {
      log.Trace("()");

      ProxProtocolMessage res = null;
      try
      {
        res = new ProxProtocolMessage(MessageWithHeader.Parser.ParseFrom(Data).Body);
        string msgStr = res.ToString();
        log.Trace("Received message:\n{0}", msgStr.SubstrMax(512));
      }
      catch (Exception e)
      {
        log.Warn("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        // Connection will be closed in calling function.
      }

      log.Trace("(-):{0}", res != null ? "Message" : "null");
      return res;
    }


    /// <summary>
    /// Converts an IoP Proximity Server Network protocol message to a binary format.
    /// </summary>
    /// <param name="Data">IoP Proximity Server Network protocol message.</param>
    /// <returns>Binary representation of the message to be sent over the network.</returns>
    public static byte[] MessageToByteArray(IProtocolMessage<Message> Data)
    {
      MessageWithHeader mwh = new MessageWithHeader();
      mwh.Body = Data.Message;
      // We have to initialize the header before calling CalculateSize.
      mwh.Header = 1;
      mwh.Header = (uint)mwh.CalculateSize() - ProtocolHelper.HeaderSize;
      return mwh.ToByteArray();
    }


    /// <summary>
    /// Sets the version of the protocol that will be used by the message builder.
    /// </summary>
    /// <param name="SelectedVersion">Selected version information.</param>
    public void SetProtocolVersion(SemVer SelectedVersion)
    {
      version =  SelectedVersion.ToByteString();
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
    public IProtocolMessage<Message> CreateRequest()
    {
      int newId = Interlocked.Increment(ref id);

      Message message = new Message();
      message.Id = (uint)newId;
      message.Request = new Request();

      var res = new ProxProtocolMessage(message);

      return res;
    }


    /// <summary>
    /// Creates a new response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <param name="ResponseStatus">Status code of the response.</param>
    /// <returns>Response message template for the request.</returns>
    public IProtocolMessage<Message> CreateResponse(IProtocolMessage<Message> Request, Status ResponseStatus)
    {
      Message message = new Message();
      message.Id = Request.Id;
      message.Response = new Response();
      message.Response.Status = ResponseStatus;

      var res = new ProxProtocolMessage(message);

      return res;
    }

    /// <summary>
    /// Creates a new successful response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Response message template for the request.</returns>
    public IProtocolMessage<Message> CreateOkResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.Ok);
    }


    /// <summary>
    /// Creates a new error response for a specific request with ERROR_PROTOCOL_VIOLATION status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorProtocolViolationResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorProtocolViolation);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_UNSUPPORTED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorUnsupportedResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorUnsupported);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BANNED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorBannedResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorBanned);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BUSY status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorBusyResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorBusy);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_UNAUTHORIZED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorUnauthorizedResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorUnauthorized);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BAD_ROLE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorBadRoleResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorBadRole);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_BAD_CONVERSATION_STATUS status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorBadConversationStatusResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorBadConversationStatus);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_INTERNAL status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorInternalResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorInternal);
    }


    /// <summary>
    /// Creates a new error response for a specific request with ERROR_QUOTA_EXCEEDED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorQuotaExceededResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorQuotaExceeded);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_INVALID_SIGNATURE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorInvalidSignatureResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorInvalidSignature);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_NOT_FOUND status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorNotFoundResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorNotFound);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_INVALID_VALUE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <param name="Details">Optionally, details about the error to be sent in 'Response.details'.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorInvalidValueResponse(IProtocolMessage<Message> Request, string Details = null)
    {
      var res = CreateResponse(Request, Status.ErrorInvalidValue);
      if (Details != null)
        res.Message.Response.Details = Details;

      return res;
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_ALREADY_EXISTS status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorAlreadyExistsResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorAlreadyExists);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_NOT_AVAILABLE status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorNotAvailableResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorNotAvailable);
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_REJECTED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <param name="Details">Optionally, details about the error to be sent in 'Response.details'.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorRejectedResponse(IProtocolMessage<Message> Request, string Details = null)
    {
      var res = CreateResponse(Request, Status.ErrorRejected);
      if (Details != null)
        res.Message.Response.Details = Details;

      return res;
    }

    /// <summary>
    /// Creates a new error response for a specific request with ERROR_UNINITIALIZED status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorUninitializedResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorUninitialized);
    }






    /// <summary>
    /// Creates a new single request.
    /// </summary>
    /// <returns>New single request message template.</returns>
    public IProtocolMessage<Message> CreateSingleRequest()
    {
      var res = CreateRequest();
      res.Message.Request.SingleRequest = new SingleRequest();
      res.Message.Request.SingleRequest.Version = version;

      return res;
    }

    /// <summary>
    /// Creates a new conversation request.
    /// </summary>
    /// <returns>New conversation request message template.</returns>
    public IProtocolMessage<Message> CreateConversationRequest()
    {
      var res = CreateRequest();
      res.Message.Request.ConversationRequest = new ConversationRequest();

      return res;
    }


    /// <summary>
    /// Signs a request body with identity private key and puts the signature to the ConversationRequest.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationRequest.</param>
    /// <param name="RequestBody">Part of the request to sign.</param>
    public void SignConversationRequestBody(IProtocolMessage<Message> Message, IMessage RequestBody)
    {
      byte[] msg = RequestBody.ToByteArray();
      SignConversationRequestBodyPart(Message, msg);
    }


    /// <summary>
    /// Signs a part of the request body with identity private key and puts the signature to the ConversationRequest.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationRequest.</param>
    /// <param name="BodyPart">Part of the request to sign.</param>
    public void SignConversationRequestBodyPart(IProtocolMessage<Message> Message, byte[] BodyPart)
    {
      byte[] signature = Ed25519.Sign(BodyPart, keys.ExpandedPrivateKey);
      Message.Message.Request.ConversationRequest.Signature = ProtocolHelper.ByteArrayToByteString(signature);
    }


    /// <summary>
    /// Verifies ConversationRequest.Signature signature of a request body with a given public key.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationRequest.</param>
    /// <param name="RequestBody">Part of the request that was signed.</param>
    /// <param name="PublicKey">Public key of the identity that created the signature.</param>
    /// <returns>true if the signature is valid, false otherwise including missing signature.</returns>
    public bool VerifySignedConversationRequestBody(IProtocolMessage<Message> Message, IMessage RequestBody, byte[] PublicKey)
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
    public bool VerifySignedConversationRequestBodyPart(IProtocolMessage<Message> Message, byte[] BodyPart, byte[] PublicKey)
    {
      byte[] signature = Message.Message.Request.ConversationRequest.Signature.ToByteArray();

      bool res = Ed25519.Verify(signature, BodyPart, PublicKey);
      return res;
    }


    /// <summary>
    /// Signs a response body with identity private key and puts the signature to the ConversationResponse.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="ResponseBody">Part of the response to sign.</param>
    public void SignConversationResponseBody(IProtocolMessage<Message> Message, IMessage ResponseBody)
    {
      byte[] msg = ResponseBody.ToByteArray();
      SignConversationResponseBodyPart(Message, msg);
    }


    /// <summary>
    /// Signs a part of the response body with identity private key and puts the signature to the ConversationResponse.Signature.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="BodyPart">Part of the response to sign.</param>
    public void SignConversationResponseBodyPart(IProtocolMessage<Message> Message, byte[] BodyPart)
    {
      byte[] signature = Ed25519.Sign(BodyPart, keys.ExpandedPrivateKey);
      Message.Message.Response.ConversationResponse.Signature = ProtocolHelper.ByteArrayToByteString(signature);
    }


    /// <summary>
    /// Verifies ConversationResponse.Signature signature of a response body with a given public key.
    /// </summary>
    /// <param name="Message">Whole message which contains an initialized ConversationResponse.</param>
    /// <param name="ResponseBody">Part of the request that was signed.</param>
    /// <param name="PublicKey">Public key of the identity that created the signature.</param>
    /// <returns>true if the signature is valid, false otherwise including missing signature.</returns>
    public bool VerifySignedConversationResponseBody(IProtocolMessage<Message> Message, IMessage ResponseBody, byte[] PublicKey)
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
    public bool VerifySignedConversationResponseBodyPart(IProtocolMessage<Message> Message, byte[] BodyPart, byte[] PublicKey)
    {
      byte[] signature = Message.Message.Response.ConversationResponse.Signature.ToByteArray();

      bool res = Ed25519.Verify(signature, BodyPart, PublicKey);
      return res;
    }

    /// <summary>
    /// Creates a new successful single response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Single response message template for the request.</returns>
    public IProtocolMessage<Message> CreateSingleResponse(IProtocolMessage<Message> Request)
    {
      var res = CreateOkResponse(Request);
      res.Message.Response.SingleResponse = new SingleResponse();
      res.Message.Response.SingleResponse.Version = Request.Message.Request.SingleRequest.Version;

      return res;
    }

    /// <summary>
    /// Creates a new successful conversation response template for a specific request.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Conversation response message template for the request.</returns>
    public IProtocolMessage<Message> CreateConversationResponse(IProtocolMessage<Message> Request)
    {
      var res = CreateOkResponse(Request);
      res.Message.Response.ConversationResponse = new ConversationResponse();

      return res;
    }


    /// <summary>
    /// Creates a new PingRequest message.
    /// </summary>
    /// <param name="Payload">Caller defined payload to be sent to the other peer.</param>
    /// <returns>PingRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreatePingRequest(byte[] Payload)
    {
      PingRequest pingRequest = new PingRequest();
      pingRequest.Payload = ProtocolHelper.ByteArrayToByteString(Payload);

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.Ping = pingRequest;

      return res;
    }

    /// <summary>
    /// Creates a response message to a PingRequest message.
    /// </summary>
    /// <param name="Request">PingRequest message for which the response is created.</param>
    /// <param name="Payload">Payload to include in the response.</param>
    /// <param name="Clock">Timestamp to include in the response.</param>
    /// <returns>PingResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreatePingResponse(IProtocolMessage<Message> Request, byte[] Payload, long Clock)
    {
      PingResponse pingResponse = new PingResponse();
      pingResponse.Clock = Clock;
      pingResponse.Payload = ProtocolHelper.ByteArrayToByteString(Payload);

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.Ping = pingResponse;

      return res;
    }

    /// <summary>
    /// Creates a new ListRolesRequest message.
    /// </summary>
    /// <returns>ListRolesRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateListRolesRequest()
    {
      ListRolesRequest listRolesRequest = new ListRolesRequest();

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ListRoles = listRolesRequest;

      return res;
    }

    /// <summary>
    /// Creates a response message to a ListRolesRequest message.
    /// </summary>
    /// <param name="Request">ListRolesRequest message for which the response is created.</param>
    /// <param name="Roles">List of role server descriptions to be included in the response.</param>
    /// <returns>ListRolesResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateListRolesResponse(IProtocolMessage<Message> Request, List<ServerRole> Roles)
    {
      ListRolesResponse listRolesResponse = new ListRolesResponse();
      listRolesResponse.Roles.AddRange(Roles);

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ListRoles = listRolesResponse;

      return res;
    }


    /// <summary>
    /// Creates a new StartConversationRequest message.
    /// </summary>
    /// <param name="Challenge">Client's generated challenge data for server's authentication.</param>
    /// <returns>StartConversationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateStartConversationRequest(byte[] Challenge)
    {
      StartConversationRequest startConversationRequest = new StartConversationRequest();
      startConversationRequest.SupportedVersions.Add(supportedVersions);

      startConversationRequest.PublicKey = ProtocolHelper.ByteArrayToByteString(keys.PublicKey);
      startConversationRequest.ClientChallenge = ProtocolHelper.ByteArrayToByteString(Challenge);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.Start = startConversationRequest;

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
    public IProtocolMessage<Message> CreateStartConversationResponse(IProtocolMessage<Message> Request, SemVer Version, byte[] PublicKey, byte[] Challenge, byte[] ClientChallenge)
    {
      StartConversationResponse startConversationResponse = new StartConversationResponse();
      startConversationResponse.Version = Version.ToByteString();
      startConversationResponse.PublicKey = ProtocolHelper.ByteArrayToByteString(PublicKey);
      startConversationResponse.Challenge = ProtocolHelper.ByteArrayToByteString(Challenge);
      startConversationResponse.ClientChallenge = ProtocolHelper.ByteArrayToByteString(ClientChallenge);

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.Start = startConversationResponse;

      SignConversationResponseBodyPart(res, ClientChallenge);

      return res;
    }



    /// <summary>
    /// Creates a new VerifyIdentityRequest message.
    /// </summary>
    /// <param name="Challenge">Challenge received in StartConversationRequest.Challenge.</param>
    /// <returns>VerifyIdentityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateVerifyIdentityRequest(byte[] Challenge)
    {
      VerifyIdentityRequest verifyIdentityRequest = new VerifyIdentityRequest();
      verifyIdentityRequest.Challenge = ProtocolHelper.ByteArrayToByteString(Challenge);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.VerifyIdentity = verifyIdentityRequest;

      SignConversationRequestBody(res, verifyIdentityRequest);
      return res;
    }

    /// <summary>
    /// Creates a response message to a VerifyIdentityRequest message.
    /// </summary>
    /// <param name="Request">VerifyIdentityRequest message for which the response is created.</param>
    /// <returns>VerifyIdentityResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateVerifyIdentityResponse(IProtocolMessage<Message> Request)
    {
      VerifyIdentityResponse verifyIdentityResponse = new VerifyIdentityResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.VerifyIdentity = verifyIdentityResponse;

      return res;
    }



    /// <summary>
    /// Creates a new CreateActivityRequest message.
    /// </summary>
    /// <param name="Version">Version of the activity structure.</param>
    /// <param name="ActivityId">Unique identifier of the client’s activity.</param>
    /// <param name="PsNetworkId">Network identifier of the profile server that hosts the profile of the owner identity of the activity.</param>
    /// <param name="PsIpAddress">IP address of the profile server that hosts the profile of the owner identity of the activity.</param>
    /// <param name="PsPrimaryPort">Primary port of the profile server that hosts the profile of the owner identity of the activity.</param>
    /// <param name="ActivityType">Type of activity in human readable form.</param>
    /// <param name="Location">Initial GPS location of the activity.</param>
    /// <param name="Precision">Location precision information in metres.</param>
    /// <param name="StartTime">Time when the activity starts.</param>
    /// <param name="ExpirationTime">Time when the activity expires.</param>
    /// <param name="ExtraData">Extra data about the activity.</param>
    /// <param name="IgnoreServerIds">List of network identifiers of proximity servers to ignore.</param>
    /// <returns>CreateActivityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCreateActivityRequest(SemVer Version, uint ActivityId, byte[] PsNetworkId, IPAddress PsIpAddress, uint PsPrimaryPort, string ActivityType, GpsLocation Location, uint Precision, DateTime StartTime, DateTime ExpirationTime, string ExtraData, List<byte[]> IgnoreServerIds)
    {
      ActivityInformation activity = new ActivityInformation()
      {
        Version = Version.ToByteString(),
        Id = ActivityId,
        OwnerPublicKey = ProtocolHelper.ByteArrayToByteString(keys.PublicKey),
        ProfileServerContact = new ServerContactInfo()
        {
          NetworkId = ProtocolHelper.ByteArrayToByteString(PsNetworkId),
          IpAddress = ProtocolHelper.ByteArrayToByteString(PsIpAddress.GetAddressBytes()),
          PrimaryPort = PsPrimaryPort,
        },
        Type = ActivityType != null ? ActivityType : "",
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude(),
        Precision = Precision,
        StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(StartTime),
        ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(ExpirationTime),
        ExtraData = ExtraData != null ? ExtraData : ""
      };

      return CreateCreateActivityRequest(activity, IgnoreServerIds);
    }

    /// <summary>
    /// Creates a new CreateActivityRequest message.
    /// </summary>
    /// <param name="Activity">Description of the activity.</param>
    /// <param name="NoPropagation">If set to true, the proximity server will not propagate the update to the neighborhood.</param>
    /// <param name="IgnoreServerIds">List of network identifiers of proximity servers to ignore.</param>
    /// <returns>CreateActivityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCreateActivityRequest(ActivityInformation Activity, List<byte[]> IgnoreServerIds = null)
    {
      CreateActivityRequest createActivityRequest = new CreateActivityRequest();
      createActivityRequest.Activity = Activity;

      foreach (byte[] ignoredServerId in IgnoreServerIds)
        createActivityRequest.IgnoreServerIds.Add(ProtocolHelper.ByteArrayToByteString(ignoredServerId));

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.CreateActivity = createActivityRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CreateActivityRequest message.
    /// </summary>
    /// <param name="Request">CreateActivityRequest message for which the response is created.</param>
    /// <returns>CreateActivityResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCreateActivityResponse(IProtocolMessage<Message> Request)
    {
      CreateActivityResponse createActivityResponse = new CreateActivityResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.CreateActivity = createActivityResponse;

      return res;
    }


    /// <summary>
    /// Creates a new UpdateActivityRequest message.
    /// </summary>
    /// <param name="Version">Version of the activity structure.</param>
    /// <param name="ActivityId">Unique identifier of the client’s activity.</param>
    /// <param name="PsNetworkId">Network identifier of the profile server that hosts the profile of the owner identity of the activity.</param>
    /// <param name="PsIpAddress">IP address of the profile server that hosts the profile of the owner identity of the activity.</param>
    /// <param name="PsPrimaryPort">Primary port of the profile server that hosts the profile of the owner identity of the activity.</param>
    /// <param name="ActivityType">Type of activity in human readable form.</param>
    /// <param name="Location">Initial GPS location of the activity.</param>
    /// <param name="Precision">Location precision information in metres.</param>
    /// <param name="StartTime">Time when the activity starts.</param>
    /// <param name="ExpirationTime">Time when the activity expires.</param>
    /// <param name="ExtraData">Extra data about the activity.</param>
    /// <param name="NoPropagation">If set to true, the proximity server will not propagate the update to the neighborhood.</param>
    /// <param name="IgnoreServerIds">List of network identifiers of proximity servers to ignore.</param>
    /// <returns>UpdateActivityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateUpdateActivityRequest(SemVer Version, uint ActivityId, byte[] PsNetworkId, IPAddress PsIpAddress, uint PsPrimaryPort, string ActivityType, GpsLocation Location, uint Precision, DateTime StartTime, DateTime ExpirationTime, string ExtraData, bool NoPropagation = false, List<byte[]> IgnoreServerIds = null)
    {
      ActivityInformation activity = new ActivityInformation()
      {
        Version = Version.ToByteString(),
        Id = ActivityId,
        OwnerPublicKey = ProtocolHelper.ByteArrayToByteString(keys.PublicKey),
        ProfileServerContact = new ServerContactInfo()
        {
          NetworkId = ProtocolHelper.ByteArrayToByteString(PsNetworkId),
          IpAddress = ProtocolHelper.ByteArrayToByteString(PsIpAddress.GetAddressBytes()),
          PrimaryPort = PsPrimaryPort,
        },
        Type = ActivityType != null ? ActivityType : "",
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude(),
        Precision = Precision,
        StartTime = ProtocolHelper.DateTimeToUnixTimestampMs(StartTime),
        ExpirationTime = ProtocolHelper.DateTimeToUnixTimestampMs(ExpirationTime),
        ExtraData = ExtraData != null ? ExtraData : ""
      };

      return CreateUpdateActivityRequest(activity, NoPropagation, IgnoreServerIds);
    }


    /// <summary>
    /// Creates a new UpdateActivityRequest message.
    /// </summary>
    /// <param name="Activity">Description of the activity.</param>
    /// <param name="NoPropagation">If set to true, the proximity server will not propagate the update to the neighborhood.</param>
    /// <param name="IgnoreServerIds">List of network identifiers of proximity servers to ignore.</param>
    /// <returns>UpdateActivityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateUpdateActivityRequest(ActivityInformation Activity, bool NoPropagation = false, List<byte[]> IgnoreServerIds = null)
    {
      UpdateActivityRequest updateActivityRequest = new UpdateActivityRequest();
      updateActivityRequest.Activity = Activity;
      updateActivityRequest.NoPropagation = NoPropagation;

      foreach (byte[] ignoredServerId in IgnoreServerIds)
        updateActivityRequest.IgnoreServerIds.Add(ProtocolHelper.ByteArrayToByteString(ignoredServerId));

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.UpdateActivity = updateActivityRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a UpdateActivityRequest message.
    /// </summary>
    /// <param name="Request">UpdateActivityRequest message for which the response is created.</param>
    /// <returns>UpdateActivityResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateUpdateActivityResponse(IProtocolMessage<Message> Request)
    {
      UpdateActivityResponse updateActivityResponse = new UpdateActivityResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.UpdateActivity = updateActivityResponse;

      return res;
    }


    /// <summary>
    /// Creates a new DeleteActivityRequest message.
    /// </summary>
    /// <param name="ActivityId">Unique identifier of the client’s activity to delete.</param>
    /// <returns>DeleteActivityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateDeleteActivityRequest(uint ActivityId)
    {
      DeleteActivityRequest deleteActivityRequest = new DeleteActivityRequest();
      deleteActivityRequest.Id = ActivityId;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.DeleteActivity = deleteActivityRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a DeleteActivityRequest message.
    /// </summary>
    /// <param name="Request">DeleteActivityRequest message for which the response is created.</param>
    /// <returns>DeleteActivityResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateDeleteActivityResponse(IProtocolMessage<Message> Request)
    {
      DeleteActivityResponse deleteActivityResponse = new DeleteActivityResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.DeleteActivity = deleteActivityResponse;

      return res;
    }



    /// <summary>
    /// Creates a new GetActivityInformationRequest message.
    /// </summary>
    /// <param name="ActivityId">Identifier of the activity.</param>
    /// <param name="OwnerNetworkId">Network identifier of the owner of the activity.</param>
    /// <returns>GetActivityInformationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateGetActivityInformationRequest(uint ActivityId, byte[] OwnerNetworkId)
    {
      GetActivityInformationRequest getActivityInformationRequest = new GetActivityInformationRequest();
      getActivityInformationRequest.Id = ActivityId;
      getActivityInformationRequest.OwnerNetworkId = ProtocolHelper.ByteArrayToByteString(OwnerNetworkId);

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.GetActivityInformation = getActivityInformationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a GetActivityInformationRequest message.
    /// </summary>
    /// <param name="Request">GetActivityInformationRequest message for which the response is created.</param>
    /// <param name="QueryInformation">Information about the activity.</param>
    /// <returns>GetActivityInformationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateGetActivityInformationResponse(IProtocolMessage<Message> Request, ActivityQueryInformation QueryInformation)
    {
      GetActivityInformationResponse getActivityInformationResponse = new GetActivityInformationResponse();
      getActivityInformationResponse.Info = QueryInformation;

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.GetActivityInformation = getActivityInformationResponse;

      return res;
    }



    /// <summary>
    /// Creates a new ActivitySearchRequest message.
    /// </summary>
    /// <param name="ActivityType">Wildcard string filter for activity type. If filtering by activity type is not required this is set to null.</param>
    /// <param name="ExtraData">Regular expression string filter for activity's extra data information. If filtering by extra data information is not required this is set to null.</param>
    /// <param name="StartNotAfter">Specification of maximal start time of activity. If filtering by start time is not required this is set to null.</param>
    /// <param name="ExpirationNotBefore">Specification of minimal expiration time of activity. If filtering by expiration time is not required this is set to null.</param>
    /// <param name="OwnerNetworkId">Network identifier of the creator of activity. If filtering by creator is not required this is set to null.</param>
    /// <param name="Location">GPS location, near which the target activities has to be located. If no location filtering is required this is set to null.</param>
    /// <param name="Radius">If <paramref name="Location"/> is not 0, this is radius in metres that together with <paramref name="Location"/> defines the target area.</param>
    /// <param name="MaxResponseRecordCount">Maximal number of results to be included in the response. This is an integer between 1 and 1,000.</param>
    /// <param name="MaxTotalRecordCount">Maximal number of total results that the proximity server will look for and save. This is an integer between 1 and 10,000.</param>
    /// <param name="IncludePrimaryOnly">If set to true, the proximity server only returns activities for which it acts as the primary proximity server. 
    /// Otherwise, activities from the proximity server's neighborhood can be included.</param>
    /// <returns>ActivitySearchRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateActivitySearchRequest(string ActivityType, string ExtraData, DateTime? StartNotAfter, DateTime? ExpirationNotBefore, byte[] OwnerNetworkId = null, GpsLocation Location = null, uint Radius = 0, uint MaxResponseRecordCount = 100, uint MaxTotalRecordCount = 1000, bool IncludePrimaryOnly = false)
    {
      ActivitySearchRequest activitySearchRequest = new ActivitySearchRequest();
      activitySearchRequest.IncludePrimaryOnly = IncludePrimaryOnly;
      activitySearchRequest.MaxResponseRecordCount = MaxResponseRecordCount;
      activitySearchRequest.MaxTotalRecordCount = MaxTotalRecordCount;
      activitySearchRequest.OwnerNetworkId = ProtocolHelper.ByteArrayToByteString(OwnerNetworkId);
      activitySearchRequest.Type = ActivityType != null ? ActivityType : "";
      activitySearchRequest.StartNotAfter = StartNotAfter != null ? ProtocolHelper.DateTimeToUnixTimestampMs(StartNotAfter.Value) : 0;
      activitySearchRequest.ExpirationNotBefore = ExpirationNotBefore != null ? ProtocolHelper.DateTimeToUnixTimestampMs(ExpirationNotBefore.Value) : 0;
      activitySearchRequest.Latitude = Location != null ? Location.GetLocationTypeLatitude() : GpsLocation.NoLocationLocationType;
      activitySearchRequest.Longitude = Location != null ? Location.GetLocationTypeLongitude() : GpsLocation.NoLocationLocationType;
      activitySearchRequest.Radius = Location != null ? Radius : 0;
      activitySearchRequest.ExtraData = ExtraData != null ? ExtraData : "";

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ActivitySearch = activitySearchRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ActivitySearchRequest message.
    /// </summary>
    /// <param name="Request">ActivitySearchRequest message for which the response is created.</param>
    /// <param name="TotalRecordCount">Total number of activities that matched the search criteria.</param>
    /// <param name="MaxResponseRecordCount">Limit of the number of results provided.</param>
    /// <param name="CoveredServers">List of proximity servers whose activity databases were be used to produce the result.</param>
    /// <param name="Results">List of results that contains up to <paramref name="MaxRecordCount"/> items.</param>
    /// <returns>ActivitySearchResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateActivitySearchResponse(IProtocolMessage<Message> Request, uint TotalRecordCount, uint MaxResponseRecordCount, IEnumerable<byte[]> CoveredServers, IEnumerable<ActivityQueryInformation> Results)
    {
      ActivitySearchResponse activitySearchResponse = new ActivitySearchResponse();
      activitySearchResponse.TotalRecordCount = TotalRecordCount;
      activitySearchResponse.MaxResponseRecordCount = MaxResponseRecordCount;

      foreach (byte[] coveredServers in CoveredServers)
        activitySearchResponse.CoveredServers.Add(ProtocolHelper.ByteArrayToByteString(coveredServers));

      if ((Results != null) && (Results.Count() > 0))
        activitySearchResponse.Activities.AddRange(Results);

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ActivitySearch = activitySearchResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ActivitySearchPartRequest message.
    /// </summary>
    /// <param name="RecordIndex">Zero-based index of the first result to retrieve.</param>
    /// <param name="RecordCount">Number of results to retrieve. This has to be an integer between 1 and 1,000.</param>
    /// <returns>ActivitySearchPartRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateActivitySearchPartRequest(uint RecordIndex, uint RecordCount)
    {
      ActivitySearchPartRequest activitySearchPartRequest = new ActivitySearchPartRequest();
      activitySearchPartRequest.RecordIndex = RecordIndex;
      activitySearchPartRequest.RecordCount = RecordCount;

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ActivitySearchPart = activitySearchPartRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ActivitySearchPartRequest message.
    /// </summary>
    /// <param name="Request">ActivitySearchPartRequest message for which the response is created.</param>
    /// <param name="RecordIndex">Index of the first result.</param>
    /// <param name="RecordCount">Number of results.</param>
    /// <param name="Results">List of results that contains <paramref name="RecordCount"/> items.</param>
    /// <returns>ActivitySearchPartResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateActivitySearchPartResponse(IProtocolMessage<Message> Request, uint RecordIndex, uint RecordCount, IEnumerable<ActivityQueryInformation> Results)
    {
      ActivitySearchPartResponse activitySearchPartResponse = new ActivitySearchPartResponse();
      activitySearchPartResponse.RecordIndex = RecordIndex;
      activitySearchPartResponse.RecordCount = RecordCount;
      activitySearchPartResponse.Activities.AddRange(Results);

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ActivitySearchPart = activitySearchPartResponse;

      return res;
    }


    /// <summary>
    /// Creates a new StartNeighborhoodInitializationRequest message.
    /// </summary>
    /// <param name="PrimaryPort">Primary interface port of the requesting proximity server.</param>
    /// <param name="NeighborPort">Neighbors interface port of the requesting proximity server.</param>
    /// <param name="IpAddress">External IP address of the requesting proximity server.</param>
    /// <returns>StartNeighborhoodInitializationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateStartNeighborhoodInitializationRequest(uint PrimaryPort, uint NeighborPort, IPAddress IpAddress)
    {
      StartNeighborhoodInitializationRequest startNeighborhoodInitializationRequest = new StartNeighborhoodInitializationRequest();
      startNeighborhoodInitializationRequest.PrimaryPort = PrimaryPort;
      startNeighborhoodInitializationRequest.NeighborPort = NeighborPort;
      startNeighborhoodInitializationRequest.IpAddress = ProtocolHelper.ByteArrayToByteString(IpAddress.GetAddressBytes());

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.StartNeighborhoodInitialization = startNeighborhoodInitializationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a StartNeighborhoodInitializationRequest message.
    /// </summary>
    /// <param name="Request">StartNeighborhoodInitializationRequest message for which the response is created.</param>
    /// <returns>StartNeighborhoodInitializationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateStartNeighborhoodInitializationResponse(IProtocolMessage<Message> Request)
    {
      StartNeighborhoodInitializationResponse startNeighborhoodInitializationResponse = new StartNeighborhoodInitializationResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.StartNeighborhoodInitialization = startNeighborhoodInitializationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new FinishNeighborhoodInitializationRequest message.
    /// </summary>
    /// <returns>FinishNeighborhoodInitializationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateFinishNeighborhoodInitializationRequest()
    {
      FinishNeighborhoodInitializationRequest finishNeighborhoodInitializationRequest = new FinishNeighborhoodInitializationRequest();

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.FinishNeighborhoodInitialization = finishNeighborhoodInitializationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a FinishNeighborhoodInitializationRequest message.
    /// </summary>
    /// <param name="Request">FinishNeighborhoodInitializationRequest message for which the response is created.</param>
    /// <returns>FinishNeighborhoodInitializationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateFinishNeighborhoodInitializationResponse(IProtocolMessage<Message> Request)
    {
      FinishNeighborhoodInitializationResponse finishNeighborhoodInitializationResponse = new FinishNeighborhoodInitializationResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.FinishNeighborhoodInitialization = finishNeighborhoodInitializationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new NeighborhoodSharedActivityUpdateRequest message.
    /// </summary>
    /// <param name="Items">List of activities changes to share.</param>
    /// <returns>NeighborhoodSharedActivityUpdateRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateNeighborhoodSharedActivityUpdateRequest(IEnumerable<SharedActivityUpdateItem> Items = null)
    {
      NeighborhoodSharedActivityUpdateRequest neighborhoodSharedActivityUpdateRequest = new NeighborhoodSharedActivityUpdateRequest();
      if (Items != null) neighborhoodSharedActivityUpdateRequest.Items.AddRange(Items);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.NeighborhoodSharedActivityUpdate = neighborhoodSharedActivityUpdateRequest;;

      return res;
    }


    /// <summary>
    /// Creates a response message to a NeighborhoodSharedActivityUpdateRequest message.
    /// </summary>
    /// <param name="Request">NeighborhoodSharedActivityUpdateRequest message for which the response is created.</param>
    /// <returns>NeighborhoodSharedActivityUpdateResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateNeighborhoodSharedActivityUpdateResponse(IProtocolMessage<Message> Request)
    {
      NeighborhoodSharedActivityUpdateResponse neighborhoodSharedActivityUpdateResponse = new NeighborhoodSharedActivityUpdateResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.NeighborhoodSharedActivityUpdate = neighborhoodSharedActivityUpdateResponse;

      return res;
    }


    /// <summary>
    /// Creates a new StopNeighborhoodUpdatesRequest message.
    /// </summary>
    /// <returns>StopNeighborhoodUpdatesRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateStopNeighborhoodUpdatesRequest()
    {
      StopNeighborhoodUpdatesRequest stopNeighborhoodUpdatesRequest = new StopNeighborhoodUpdatesRequest();

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.StopNeighborhoodUpdates = stopNeighborhoodUpdatesRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a StopNeighborhoodUpdatesRequest message.
    /// </summary>
    /// <param name="Request">StopNeighborhoodUpdatesRequest message for which the response is created.</param>
    /// <returns>StopNeighborhoodUpdatesResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateStopNeighborhoodUpdatesResponse(IProtocolMessage<Message> Request)
    {
      StopNeighborhoodUpdatesResponse stopNeighborhoodUpdatesResponse = new StopNeighborhoodUpdatesResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.StopNeighborhoodUpdates = stopNeighborhoodUpdatesResponse;

      return res;
    }
  }
}
