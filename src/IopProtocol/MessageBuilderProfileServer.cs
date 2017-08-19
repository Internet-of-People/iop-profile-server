using Google.Protobuf;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IopCrypto;
using System.Collections;
using IopCommon;
using Iop.Can;
using System.Net;
using Iop.Shared;

namespace IopProtocol
{
  /// <summary>
  /// Representation of the protocol message in IoP Profile Server Network.
  /// </summary>
  class PsProtocolMessage : IProtocolMessage<Message>
  {
    /// <summary>Protocol specific message.</summary>
    public Message Message { get; }

    /// <summary>Unique message identifier within a session.</summary>
    public uint Id { get { return Message.Id; } }


    /// <summary>
    /// Initializes instance of the object using an existing Protobuf message.
    /// </summary>
    /// <param name="Message">Protobuf Profile Server Network message.</param>
    public PsProtocolMessage(Message Message)
    {
      this.Message = Message;
    }


    public override string ToString()
    {
      return Message.ToString();
    }
  }

  
  /// <summary>
  /// Allows easy construction of IoP Profile Server Network requests and responses.
  /// </summary>
  public class PsMessageBuilder
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("IopProtocol.PsMessageBuilder");

    /// <summary>Size in bytes of an authentication challenge data.</summary>
    public const int ChallengeDataSize = 32;

    /// <summary>Maximum number of bytes that type field in ProfileSearchRequest can occupy.</summary>
    public const int MaxProfileSearchTypeLengthBytes = 64;

    /// <summary>Maximum number of bytes that name field in ProfileSearchRequest can occupy.</summary>
    public const int MaxProfileSearchNameLengthBytes = 64;

    /// <summary>Maximum number of bytes that extraData field in ProfileSearchRequest can occupy.</summary>
    public const int MaxProfileSearchExtraDataLengthBytes = 256;

    /// <summary>Maximum number of bytes that type field in GetIdentityRelationshipsInformationRequest can occupy.</summary>
    public const int MaxGetIdentityRelationshipsTypeLengthBytes = 64;

    /// <summary>Maximum number of bytes that type field in RelationshipCard can occupy.</summary>
    public const int MaxRelationshipCardTypeLengthBytes = 64;



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
    public PsMessageBuilder(uint IdBase, List<SemVer> SupportedVersions, KeysEd25519 Keys)
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
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public static IProtocolMessage<Message> CreateMessageFromRawData(byte[] Data)
    {
      log.Trace("()");

      PsProtocolMessage res = null;
      try
      {
        res = new PsProtocolMessage(MessageWithHeader.Parser.ParseFrom(Data).Body);
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
    /// Converts an IoP Profile Server Network protocol message to a binary format.
    /// </summary>
    /// <param name="Data">IoP Profile Server Network protocol message.</param>
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
    public IProtocolMessage<Message> CreateRequest()
    {
      int newId = Interlocked.Increment(ref id);

      Message message = new Message();
      message.Id = (uint)newId;
      message.Request = new Request();

      var res = new PsProtocolMessage(message);

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
      message.Id = Request.Message.Id;
      message.Response = new Response();
      message.Response.Status = ResponseStatus;

      var res = new PsProtocolMessage(message);

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
    public IProtocolMessage<Message> CreateErrorProtocolViolationResponse(IProtocolMessage<Message> Request = null)
    {
      if (Request == null)
        Request = new PsProtocolMessage(new Message { Id = 0x0BADC0DE });
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
      res.Message.Request.SingleRequest.Version = Version;

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
    public IProtocolMessage<Message> CreatePingResponse(IProtocolMessage<Message> Request, byte[] Payload, DateTime Clock)
    {
      PingResponse pingResponse = new PingResponse();
      pingResponse.Clock = ProtocolHelper.DateTimeToUnixTimestampMs(Clock);
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
    /// Creates a new RegisterHostingRequest message.
    /// </summary>
    /// <param name="Contract">Hosting contract for one of the profile server's plan to base the hosting agreement on.</param>
    /// <returns>RegisterHostingRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateRegisterHostingRequest(HostingPlanContract Contract)
    {
      RegisterHostingRequest registerHostingRequest = new RegisterHostingRequest();
      registerHostingRequest.Contract = Contract;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.RegisterHosting = registerHostingRequest;

      SignConversationRequestBodyPart(res, Contract.ToByteArray());
      return res;
    }


    /// <summary>
    /// Creates a response message to a RegisterHostingRequest message.
    /// </summary>
    /// <param name="Request">RegisterHostingRequest message for which the response is created.</param>
    /// <param name="Contract">Contract copy from RegisterHostingRequest.Contract.</param>
    /// <returns>RegisterHostingResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateRegisterHostingResponse(IProtocolMessage<Message> Request, HostingPlanContract Contract)
    {
      RegisterHostingResponse registerHostingResponse = new RegisterHostingResponse();
      registerHostingResponse.Contract = Contract;

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.RegisterHosting = registerHostingResponse;

      SignConversationResponseBodyPart(res, Contract.ToByteArray());

      return res;
    }
    


    /// <summary>
    /// Creates a new CheckInRequest message.
    /// </summary>
    /// <param name="Challenge">Challenge received in StartConversationRequest.Challenge.</param>
    /// <returns>CheckInRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCheckInRequest(byte[] Challenge)
    {
      CheckInRequest checkInRequest = new CheckInRequest();
      checkInRequest.Challenge = ProtocolHelper.ByteArrayToByteString(Challenge);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.CheckIn = checkInRequest;

      SignConversationRequestBody(res, checkInRequest);
      return res;
    }

    /// <summary>
    /// Creates a response message to a CheckInRequest message.
    /// </summary>
    /// <param name="Request">CheckInRequest message for which the response is created.</param>
    /// <returns>CheckInResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCheckInResponse(IProtocolMessage<Message> Request)
    {
      CheckInResponse checkInResponse = new CheckInResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.CheckIn = checkInResponse;

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
    /// Creates a new UpdateProfileRequest message.
    /// </summary>
    /// <param name="Profile">Description of the profile.</param>
    /// <param name="ProfileImage">Profile image data or null if profile image is to be erased or not set.</param>
    /// <param name="ThumbnailImage">Thumbnail image data or null if thumbnail image is to be erased or not set.</param>
    /// <param name="NoPropagation">If set to true, the profile server will not propagate the update to the neighborhood.</param>
    /// <returns>UpdateProfileRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateUpdateProfileRequest(ProfileInformation Profile, byte[] ProfileImage = null, byte[] ThumbnailImage = null, bool NoPropagation = false)
    {
      UpdateProfileRequest updateProfileRequest = new UpdateProfileRequest();
      updateProfileRequest.Profile = Profile;

      if (ProfileImage != null)
        updateProfileRequest.ProfileImage = ProtocolHelper.ByteArrayToByteString(ProfileImage);

      if (ThumbnailImage != null)
        updateProfileRequest.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(ThumbnailImage);

      updateProfileRequest.NoPropagation = NoPropagation;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.UpdateProfile = updateProfileRequest;

      SignConversationRequestBodyPart(res, Profile.ToByteArray());

      return res;
    }


    /// <summary>
    /// Creates a response message to a UpdateProfileRequest message.
    /// </summary>
    /// <param name="Request">UpdateProfileRequest message for which the response is created.</param>
    /// <returns>UpdateProfileResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateUpdateProfileResponse(IProtocolMessage<Message> Request)
    {
      UpdateProfileResponse updateProfileResponse = new UpdateProfileResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.UpdateProfile = updateProfileResponse;

      return res;
    }


    /// <summary>
    /// Creates a new CancelHostingAgreementRequest message.
    /// </summary>
    /// <param name="NewProfileServerId">Network identifier of the identity's new profile server, or null if this information is not to be sent to the previous profile server.</param>
    /// <returns>CancelHostingAgreementRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCancelHostingAgreementRequest(byte[] NewProfileServerId)
    {
      CancelHostingAgreementRequest cancelHostingAgreementRequest = new CancelHostingAgreementRequest();
      cancelHostingAgreementRequest.RedirectToNewProfileServer = NewProfileServerId != null;
      if (cancelHostingAgreementRequest.RedirectToNewProfileServer)
        cancelHostingAgreementRequest.NewProfileServerNetworkId = ProtocolHelper.ByteArrayToByteString(NewProfileServerId);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.CancelHostingAgreement = cancelHostingAgreementRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CancelHostingAgreementRequest message.
    /// </summary>
    /// <param name="Request">CancelHostingAgreementRequest message for which the response is created.</param>
    /// <returns>CancelHostingAgreementResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCancelHostingAgreementResponse(IProtocolMessage<Message> Request)
    {
      CancelHostingAgreementResponse cancelHostingAgreementResponse = new CancelHostingAgreementResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.CancelHostingAgreement = cancelHostingAgreementResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ApplicationServiceAddRequest message.
    /// </summary>
    /// <param name="ServiceNames">List of service names to add to the list of services supported in the currently opened session.</param>
    /// <returns>ApplicationServiceAddRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceAddRequest(List<string> ServiceNames)
    {
      ApplicationServiceAddRequest applicationServiceAddRequest = new ApplicationServiceAddRequest();
      applicationServiceAddRequest.ServiceNames.Add(ServiceNames);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.ApplicationServiceAdd = applicationServiceAddRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceAddRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceAddRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceAddResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceAddResponse(IProtocolMessage<Message> Request)
    {
      ApplicationServiceAddResponse applicationServiceAddResponse = new ApplicationServiceAddResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.ApplicationServiceAdd = applicationServiceAddResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ApplicationServiceRemoveRequest message.
    /// </summary>
    /// <param name="ServiceName">Name of the application service to remove from the list of services supported in the currently opened session.</param>
    /// <returns>ApplicationServiceRemoveRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceRemoveRequest(string ServiceName)
    {
      ApplicationServiceRemoveRequest applicationServiceRemoveRequest = new ApplicationServiceRemoveRequest();
      applicationServiceRemoveRequest.ServiceName = ServiceName;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.ApplicationServiceRemove = applicationServiceRemoveRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceRemoveRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceRemoveRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceRemoveResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceRemoveResponse(IProtocolMessage<Message> Request)
    {
      ApplicationServiceRemoveResponse applicationServiceRemoveResponse = new ApplicationServiceRemoveResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.ApplicationServiceRemove = applicationServiceRemoveResponse;

      return res;
    }



    /// <summary>
    /// Creates a new GetProfileInformationRequest message.
    /// </summary>
    /// <param name="IdentityId">Identifier of the identity to get information about.</param>
    /// <param name="IncludeProfileImage">true if the caller wants to get the identity's profile image, false otherwise.</param>
    /// <param name="IncludeThumbnailImage">true if the caller wants to get the identity's thumbnail image, false otherwise.</param>
    /// <param name="IncludeApplicationServices">true if the caller wants to get the identity's list of application services, false otherwise.</param>
    /// <returns>GetProfileInformationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateGetProfileInformationRequest(byte[] IdentityId, bool IncludeProfileImage = false, bool IncludeThumbnailImage = false, bool IncludeApplicationServices = false)
    {
      GetProfileInformationRequest getProfileInformationRequest = new GetProfileInformationRequest();
      getProfileInformationRequest.IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(IdentityId);
      getProfileInformationRequest.IncludeProfileImage = IncludeProfileImage;
      getProfileInformationRequest.IncludeThumbnailImage = IncludeThumbnailImage;
      getProfileInformationRequest.IncludeApplicationServices = IncludeApplicationServices;

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.GetProfileInformation = getProfileInformationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a GetProfileInformationRequest message.
    /// </summary>
    /// <param name="Request">GetProfileInformationRequest message for which the response is created.</param>
    /// <param name="IsHosted">True if the requested identity is hosted by this profile server.</param>
    /// <param name="TargetProfileServerId">If <paramref name="IsHosted"/> is false, then this is the identifier of the requested identity's new profile server, or null if the profile server does not know network ID of the requested identity's new profile server.</param>
    /// <param name="IsOnline">If <paramref name="IsHosted"/> is true, this indicates whether the requested identity is currently online.</param>
    /// <param name="SignedProfile">If <paramref name="IsHosted"/> is true, this is signed profile information.</param>
    /// <param name="ProfileImage">If <paramref name="IsHosted"/> is true, this is the identity's profile image, or null if it was not requested.</param>
    /// <param name="ThumbnailImage">If <paramref name="IsHosted"/> is true, this is the identity's thumbnail image, or null if it was not requested.</param>
    /// <param name="ApplicationServices">If <paramref name="IsHosted"/> is true, this is the identity's list of supported application services, or null if it was not requested.</param>
    /// <returns>GetProfileInformationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateGetProfileInformationResponse(IProtocolMessage<Message> Request, bool IsHosted, byte[] TargetProfileServerId, bool IsOnline = false, SignedProfileInformation SignedProfile = null, byte[] ProfileImage = null, byte[] ThumbnailImage = null, HashSet<string> ApplicationServices = null)
    {
      GetProfileInformationResponse getProfileInformationResponse = new GetProfileInformationResponse();
      getProfileInformationResponse.IsHosted = IsHosted;
      getProfileInformationResponse.IsTargetProfileServerKnown = false;
      if (IsHosted)
      {
        getProfileInformationResponse.IsOnline = IsOnline;
        getProfileInformationResponse.SignedProfile = SignedProfile;
        if (ProfileImage != null) getProfileInformationResponse.ProfileImage = ProtocolHelper.ByteArrayToByteString(ProfileImage);
        if (ThumbnailImage != null) getProfileInformationResponse.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(ThumbnailImage);
        if (ApplicationServices != null) getProfileInformationResponse.ApplicationServices.Add(ApplicationServices);
      }
      else
      {
        getProfileInformationResponse.IsTargetProfileServerKnown = TargetProfileServerId != null;
        if (TargetProfileServerId != null)
          getProfileInformationResponse.TargetProfileServerNetworkId = ProtocolHelper.ByteArrayToByteString(TargetProfileServerId);
      }

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.GetProfileInformation = getProfileInformationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new CallIdentityApplicationServiceRequest message.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the callee's identity.</param>
    /// <param name="ServiceName">Name of the application service to use for the call.</param>
    /// <returns>CallIdentityApplicationServiceRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCallIdentityApplicationServiceRequest(byte[] IdentityId, string ServiceName)
    {
      CallIdentityApplicationServiceRequest callIdentityApplicationServiceRequest = new CallIdentityApplicationServiceRequest();
      callIdentityApplicationServiceRequest.IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(IdentityId);
      callIdentityApplicationServiceRequest.ServiceName = ServiceName;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.CallIdentityApplicationService = callIdentityApplicationServiceRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CallIdentityApplicationServiceRequest message.
    /// </summary>
    /// <param name="Request">CallIdentityApplicationServiceRequest message for which the response is created.</param>
    /// <param name="CallerToken">Token issued for the caller for clAppService connection.</param>
    /// <returns>CallIdentityApplicationServiceResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCallIdentityApplicationServiceResponse(IProtocolMessage<Message> Request, byte[] CallerToken)
    {
      CallIdentityApplicationServiceResponse callIdentityApplicationServiceResponse = new CallIdentityApplicationServiceResponse();
      callIdentityApplicationServiceResponse.CallerToken = ProtocolHelper.ByteArrayToByteString(CallerToken);

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.CallIdentityApplicationService = callIdentityApplicationServiceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new IncomingCallNotificationRequest message.
    /// </summary>
    /// <param name="CallerPublicKey">Public key of the caller.</param>
    /// <param name="ServiceName">Name of the application service the caller wants to use.</param>
    /// <param name="CalleeToken">Token issued for the callee for clAppService connection.</param>
    /// <returns>IncomingCallNotificationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateIncomingCallNotificationRequest(byte[] CallerPublicKey, string ServiceName, byte[] CalleeToken)
    {
      IncomingCallNotificationRequest incomingCallNotificationRequest = new IncomingCallNotificationRequest();
      incomingCallNotificationRequest.CallerPublicKey = ProtocolHelper.ByteArrayToByteString(CallerPublicKey);
      incomingCallNotificationRequest.ServiceName = ServiceName;
      incomingCallNotificationRequest.CalleeToken = ProtocolHelper.ByteArrayToByteString(CalleeToken);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.IncomingCallNotification = incomingCallNotificationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a IncomingCallNotificationRequest message.
    /// </summary>
    /// <param name="Request">IncomingCallNotificationRequest message for which the response is created.</param>
    /// <returns>IncomingCallNotificationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateIncomingCallNotificationResponse(IProtocolMessage<Message> Request)
    {
      IncomingCallNotificationResponse incomingCallNotificationResponse = new IncomingCallNotificationResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.IncomingCallNotification = incomingCallNotificationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ApplicationServiceSendMessageRequest message.
    /// </summary>
    /// <param name="Token">Client's token for clAppService connection.</param>
    /// <param name="Message">Message to be sent to the other peer, or null for channel initialization message.</param>
    /// <returns>ApplicationServiceSendMessageRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceSendMessageRequest(byte[] Token, byte[] Message)
    {
      ApplicationServiceSendMessageRequest applicationServiceSendMessageRequest = new ApplicationServiceSendMessageRequest();
      applicationServiceSendMessageRequest.Token = ProtocolHelper.ByteArrayToByteString(Token);
      if (Message != null)
        applicationServiceSendMessageRequest.Message = ProtocolHelper.ByteArrayToByteString(Message);

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ApplicationServiceSendMessage = applicationServiceSendMessageRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceSendMessageRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceSendMessageRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceSendMessageResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceSendMessageResponse(IProtocolMessage<Message> Request)
    {
      ApplicationServiceSendMessageResponse applicationServiceSendMessageResponse = new ApplicationServiceSendMessageResponse();

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ApplicationServiceSendMessage = applicationServiceSendMessageResponse;

      return res;
    }



    /// <summary>
    /// Creates a new ApplicationServiceReceiveMessageNotificationRequest message.
    /// </summary>
    /// <param name="Message">Message sent by the other peer.</param>
    /// <returns>ApplicationServiceReceiveMessageNotificationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceReceiveMessageNotificationRequest(byte[] Message)
    {
      ApplicationServiceReceiveMessageNotificationRequest applicationServiceReceiveMessageNotificationRequest = new ApplicationServiceReceiveMessageNotificationRequest();
      applicationServiceReceiveMessageNotificationRequest.Message = ProtocolHelper.ByteArrayToByteString(Message);

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ApplicationServiceReceiveMessageNotification = applicationServiceReceiveMessageNotificationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ApplicationServiceReceiveMessageNotificationRequest message.
    /// </summary>
    /// <param name="Request">ApplicationServiceReceiveMessageNotificationRequest message for which the response is created.</param>
    /// <returns>ApplicationServiceReceiveMessageNotificationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateApplicationServiceReceiveMessageNotificationResponse(IProtocolMessage<Message> Request)
    {
      ApplicationServiceReceiveMessageNotificationResponse applicationServiceReceiveMessageNotificationResponse = new ApplicationServiceReceiveMessageNotificationResponse();

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ApplicationServiceReceiveMessageNotification = applicationServiceReceiveMessageNotificationResponse;

      return res;
    }


    /// <summary>
    /// Creates a new ProfileStatsRequest message.
    /// </summary>
    /// <returns>ProfileStatsRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateProfileStatsRequest()
    {
      ProfileStatsRequest profileStatsRequest = new ProfileStatsRequest();

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ProfileStats = profileStatsRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a ProfileStatsRequest message.
    /// </summary>
    /// <param name="Request">ProfileStatsRequest message for which the response is created.</param>
    /// <param name="Stats">List of profile statistics.</param>
    /// <returns>ProfileStatsResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateProfileStatsResponse(IProtocolMessage<Message> Request, IEnumerable<ProfileStatsItem> Stats)
    {
      ProfileStatsResponse profileStatsResponse = new ProfileStatsResponse();
      if ((Stats != null) && (Stats.Count() > 0))
        profileStatsResponse.Stats.AddRange(Stats);

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ProfileStats = profileStatsResponse;

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
    /// <param name="MaxResponseRecordCount">Maximal number of results to be included in the response. This is an integer between 1 and 100 if <paramref name="IncludeThumnailImages"/> is true, otherwise this is integer between 1 and 1,000.</param>
    /// <param name="MaxTotalRecordCount">Maximal number of total results that the profile server will look for and save. This is an integer between 1 and 1000 if <paramref name="IncludeThumnailImages"/> is true, otherwise this is integer between 1 and 10,000.</param>
    /// <param name="IncludeHostedOnly">If set to true, the profile server only returns profiles of its own hosted identities. Otherwise, identities from the profile server's neighborhood can be included.</param>
    /// <param name="IncludeThumbnailImages">If set to true, the response will include a thumbnail image of each profile.</param>
    /// <returns>ProfileSearchRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateProfileSearchRequest(string IdentityType, string Name, string ExtraData, GpsLocation Location = null, uint Radius = 0, uint MaxResponseRecordCount = 100, uint MaxTotalRecordCount = 1000, bool IncludeHostedOnly = false, bool IncludeThumbnailImages = true)
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

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ProfileSearch = profileSearchRequest;

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
    public IProtocolMessage<Message> CreateProfileSearchResponse(IProtocolMessage<Message> Request, uint TotalRecordCount, uint MaxResponseRecordCount, IEnumerable<byte[]> CoveredServers, IEnumerable<ProfileQueryInformation> Results)
    {
      ProfileSearchResponse profileSearchResponse = new ProfileSearchResponse();
      profileSearchResponse.TotalRecordCount = TotalRecordCount;
      profileSearchResponse.MaxResponseRecordCount = MaxResponseRecordCount;

      foreach (byte[] coveredServers in CoveredServers)
        profileSearchResponse.CoveredServers.Add(ProtocolHelper.ByteArrayToByteString(coveredServers));

      if ((Results != null) && (Results.Count() > 0))
        profileSearchResponse.Profiles.AddRange(Results);
      
      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ProfileSearch = profileSearchResponse;

      return res;
    }



    /// <summary>
    /// Creates a new ProfileSearchPartRequest message.
    /// </summary>
    /// <param name="RecordIndex">Zero-based index of the first result to retrieve.</param>
    /// <param name="RecordCount">Number of results to retrieve. If 'ProfileSearchResponse.IncludeThumbnailImages' was set, this has to be an integer between 1 and 100, otherwise it has to be an integer between 1 and 1,000.</param>
    /// <returns>ProfileSearchRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateProfileSearchPartRequest(uint RecordIndex, uint RecordCount)
    {
      ProfileSearchPartRequest profileSearchPartRequest = new ProfileSearchPartRequest();
      profileSearchPartRequest.RecordIndex = RecordIndex;
      profileSearchPartRequest.RecordCount= RecordCount;

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.ProfileSearchPart = profileSearchPartRequest;

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
    public IProtocolMessage<Message> CreateProfileSearchPartResponse(IProtocolMessage<Message> Request, uint RecordIndex, uint RecordCount, IEnumerable<ProfileQueryInformation> Results)
    {
      ProfileSearchPartResponse profileSearchPartResponse = new ProfileSearchPartResponse();
      profileSearchPartResponse.RecordIndex = RecordIndex;
      profileSearchPartResponse.RecordCount = RecordCount;
      profileSearchPartResponse.Profiles.AddRange(Results);

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.ProfileSearchPart = profileSearchPartResponse;

      return res;
    }


    /// <summary>
    /// Creates a new AddRelatedIdentityRequest message.
    /// </summary>
    /// <param name="CardApplication">Description of the relationship proven by the signed relationship card.</param>
    /// <param name="SignedCard">Signed relationship card.</param>
    /// <returns>AddRelatedIdentityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateAddRelatedIdentityRequest(CardApplicationInformation CardApplication, SignedRelationshipCard SignedCard)
    {
      AddRelatedIdentityRequest addRelatedIdentityRequest = new AddRelatedIdentityRequest();
      addRelatedIdentityRequest.CardApplication = CardApplication;
      addRelatedIdentityRequest.SignedCard = SignedCard;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.AddRelatedIdentity = addRelatedIdentityRequest;

      SignConversationRequestBodyPart(res, CardApplication.ToByteArray());
      return res;
    }



    /// <summary>
    /// Creates a response message to a AddRelatedIdentityRequest message.
    /// </summary>
    /// <param name="Request">AddRelatedIdentityRequest message for which the response is created.</param>
    /// <returns>AddRelatedIdentityResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateAddRelatedIdentityResponse(IProtocolMessage<Message> Request)
    {
      AddRelatedIdentityResponse addRelatedIdentityResponse = new AddRelatedIdentityResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.AddRelatedIdentity = addRelatedIdentityResponse;

      return res;
    }


    /// <summary>
    /// Creates a new RemoveRelatedIdentityRequest message.
    /// </summary>
    /// <param name="CardApplicationIdentifier">Identifier of the card application to remove.</param>
    /// <returns>RemoveRelatedIdentityRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateRemoveRelatedIdentityRequest(byte[] CardApplicationIdentifier)
    {
      RemoveRelatedIdentityRequest removeRelatedIdentityRequest = new RemoveRelatedIdentityRequest();
      removeRelatedIdentityRequest.ApplicationId = ProtocolHelper.ByteArrayToByteString(CardApplicationIdentifier);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.RemoveRelatedIdentity = removeRelatedIdentityRequest;

      return res;
    }



    /// <summary>
    /// Creates a response message to a RemoveRelatedIdentityRequest message.
    /// </summary>
    /// <param name="Request">RemoveRelatedIdentityRequest message for which the response is created.</param>
    /// <returns>RemoveRelatedIdentityResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateRemoveRelatedIdentityResponse(IProtocolMessage<Message> Request)
    {
      RemoveRelatedIdentityResponse removeRelatedIdentityResponse = new RemoveRelatedIdentityResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.RemoveRelatedIdentity = removeRelatedIdentityResponse;

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
    public IProtocolMessage<Message> CreateGetIdentityRelationshipsInformationRequest(byte[] IdentityNetworkId, bool IncludeInvalid, string CardType, byte[] IssuerNetworkId)
    {
      GetIdentityRelationshipsInformationRequest getIdentityRelationshipsInformationRequest = new GetIdentityRelationshipsInformationRequest();
      getIdentityRelationshipsInformationRequest.IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(IdentityNetworkId);
      getIdentityRelationshipsInformationRequest.IncludeInvalid = IncludeInvalid;
      getIdentityRelationshipsInformationRequest.Type = CardType != null ? CardType : "";
      getIdentityRelationshipsInformationRequest.SpecificIssuer = IssuerNetworkId != null;
      if (IssuerNetworkId != null)
        getIdentityRelationshipsInformationRequest.IssuerNetworkId = ProtocolHelper.ByteArrayToByteString(IssuerNetworkId);

      var res = CreateSingleRequest();
      res.Message.Request.SingleRequest.GetIdentityRelationshipsInformation = getIdentityRelationshipsInformationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a GetIdentityRelationshipsInformationRequest message.
    /// </summary>
    /// <param name="Request">GetIdentityRelationshipsInformationRequest message for which the response is created.</param>
    /// <param name="Stats">List of profile statistics.</param>
    /// <returns>GetIdentityRelationshipsInformationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateGetIdentityRelationshipsInformationResponse(IProtocolMessage<Message> Request, IEnumerable<IdentityRelationship> Relationships)
    {
      GetIdentityRelationshipsInformationResponse getIdentityRelationshipsInformationResponse = new GetIdentityRelationshipsInformationResponse();
      getIdentityRelationshipsInformationResponse.Relationships.AddRange(Relationships);

      var res = CreateSingleResponse(Request);
      res.Message.Response.SingleResponse.GetIdentityRelationshipsInformation = getIdentityRelationshipsInformationResponse;

      return res;
    }



    /// <summary>
    /// Creates a new StartNeighborhoodInitializationRequest message.
    /// </summary>
    /// <param name="PrimaryPort">Primary interface port of the requesting profile server.</param>
    /// <param name="SrNeighborPort">Neighbors interface port of the requesting profile server.</param>
    /// <param name="IpAddress">External IP address of the requesting profile server.</param>
    /// <returns>StartNeighborhoodInitializationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateStartNeighborhoodInitializationRequest(uint PrimaryPort, uint SrNeighborPort, IPAddress IpAddress)
    {
      StartNeighborhoodInitializationRequest startNeighborhoodInitializationRequest = new StartNeighborhoodInitializationRequest();
      startNeighborhoodInitializationRequest.PrimaryPort = PrimaryPort;
      startNeighborhoodInitializationRequest.SrNeighborPort = SrNeighborPort;
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
    /// Creates a new NeighborhoodSharedProfileUpdateRequest message.
    /// </summary>
    /// <param name="Items">List of profile changes to share.</param>
    /// <returns>NeighborhoodSharedProfileUpdateRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateNeighborhoodSharedProfileUpdateRequest(IEnumerable<SharedProfileUpdateItem> Items = null)
    {
      NeighborhoodSharedProfileUpdateRequest neighborhoodSharedProfileUpdateRequest = new NeighborhoodSharedProfileUpdateRequest();
      if (Items != null) neighborhoodSharedProfileUpdateRequest.Items.AddRange(Items);

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate = neighborhoodSharedProfileUpdateRequest;
      
      return res;
    }


    /// <summary>
    /// Creates a response message to a NeighborhoodSharedProfileUpdateRequest message.
    /// </summary>
    /// <param name="Request">NeighborhoodSharedProfileUpdateRequest message for which the response is created.</param>
    /// <returns>NeighborhoodSharedProfileUpdateResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateNeighborhoodSharedProfileUpdateResponse(IProtocolMessage<Message> Request)
    {
      NeighborhoodSharedProfileUpdateResponse neighborhoodSharedProfileUpdateResponse = new NeighborhoodSharedProfileUpdateResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.NeighborhoodSharedProfileUpdate = neighborhoodSharedProfileUpdateResponse;

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



    /// <summary>
    /// Creates a new CanStoreDataRequest message.
    /// </summary>
    /// <param name="Data">Data to store in CAN, or null to just delete the old object.</param>
    /// <returns>CanStoreDataRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCanStoreDataRequest(CanIdentityData Data)
    {
      CanStoreDataRequest canStoreDataRequest = new CanStoreDataRequest();
      canStoreDataRequest.Data = Data;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.CanStoreData = canStoreDataRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CanStoreDataRequest message.
    /// </summary>
    /// <param name="Request">CanStoreDataRequest message for which the response is created.</param>
    /// <param name="Hash">Hash of 'CanStoreDataRequest.data' received from CAN, or null if 'CanStoreDataRequest.data' was null.</param>
    /// <returns>CanStoreDataResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCanStoreDataResponse(IProtocolMessage<Message> Request, byte[] Hash)
    {
      CanStoreDataResponse canStoreDataResponse = new CanStoreDataResponse();
      if (Hash != null) canStoreDataResponse.Hash = ProtocolHelper.ByteArrayToByteString(Hash);

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.CanStoreData = canStoreDataResponse;

      return res;
    }


    /// <summary>
    /// Creates a new CanPublishIpnsRecordRequest message.
    /// </summary>
    /// <param name="Record">Signed IPNS record.</param>
    /// <returns>CanPublishIpnsRecordRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCanPublishIpnsRecordRequest(CanIpnsEntry Record)
    {
      CanPublishIpnsRecordRequest canPublishIpnsRecordRequest = new CanPublishIpnsRecordRequest();
      canPublishIpnsRecordRequest.Record = Record;

      var res = CreateConversationRequest();
      res.Message.Request.ConversationRequest.CanPublishIpnsRecord = canPublishIpnsRecordRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a CanPublishIpnsRecordRequest message.
    /// </summary>
    /// <param name="Request">CanPublishIpnsRecordRequest message for which the response is created.</param>
    /// <returns>CanPublishIpnsRecordResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateCanPublishIpnsRecordResponse(IProtocolMessage<Message> Request)
    {
      CanPublishIpnsRecordResponse canPublishIpnsRecordResponse = new CanPublishIpnsRecordResponse();

      var res = CreateConversationResponse(Request);
      res.Message.Response.ConversationResponse.CanPublishIpnsRecord = canPublishIpnsRecordResponse;

      return res;
    }
  }
}
