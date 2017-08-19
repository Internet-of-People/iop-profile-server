using Google.Protobuf;
using Iop.Locnet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IopCrypto;
using System.Collections;
using IopCommon;

namespace IopProtocol
{
  /// <summary>
  /// Representation of the protocol message in IoP Profile Server Network.
  /// </summary>
  class LocProtocolMessage : IProtocolMessage<Message>
  {
    /// <summary>Protocol specific message.</summary>
    public Message Message { get; }

    /// <summary>Unique message identifier within a session.</summary>
    public uint Id { get { return Message.Id; } }

    /// <summary>
    /// Initializes instance of the object using an existing Protobuf message.
    /// </summary>
    /// <param name="Message">Protobuf Profile Server Network message.</param>
    public LocProtocolMessage(Message Message)
    {
      this.Message = Message;
    }


    public override string ToString()
    {
      return Message.ToString();
    }
  }


  /// <summary>
  /// Allows easy construction of IoP Location Based Network protocol requests and responses.
  /// </summary>
  public class LocMessageBuilder
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("IopProtocol.LocMessageBuilder");

    /// <summary>Original identifier base.</summary>
    private int idBase;

    /// <summary>Identifier that is unique per class instance for each message.</summary>
    private int id;

    /// <summary>Supported protocol versions ordered by preference.</summary>
    private List<ByteString> supportedVersions;

    /// <summary>Selected protocol version.</summary>
    private ByteString version;

    /// <summary>
    /// Initializes message builder.
    /// </summary>
    /// <param name="IdBase">Base value for message IDs. First message will have ID set to IdBase + 1.</param>
    /// <param name="SupportedVersions">List of supported versions ordered by caller's preference.</param>
    public LocMessageBuilder(uint IdBase, List<SemVer> SupportedVersions)
    {
      idBase = (int)IdBase;
      id = idBase;
      supportedVersions = new List<ByteString>();
      foreach (SemVer version in SupportedVersions)
        supportedVersions.Add(version.ToByteString());

      version = supportedVersions[0];
    }


    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public static IProtocolMessage<Message> CreateMessageFromRawData(byte[] Data)
    {
      log.Trace("()");

      LocProtocolMessage res = null;
      try
      {
        res = new LocProtocolMessage(MessageWithHeader.Parser.ParseFrom(Data).Body);
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
      version = SelectedVersion.ToByteString();
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
      message.Request.Version = version;

      var res = new LocProtocolMessage(message);

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

      var res = new LocProtocolMessage(message);

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
        Request = new LocProtocolMessage(new Message { Id = 0x0BADC0DE });
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
    /// Creates a new error response for a specific request with ERROR_INTERNAL status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateErrorInternalResponse(IProtocolMessage<Message> Request)
    {
      return CreateResponse(Request, Status.ErrorInternal);
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
    /// Creates and initializes the LocalService part of the request.
    /// </summary>
    /// <returns>Request with initialized LocalService part.</returns>
    public IProtocolMessage<Message> CreateLocalServiceRequest()
    {
      var res = CreateRequest();
      res.Message.Request.LocalService = new LocalServiceRequest();
      return res;
    }

    /// <summary>
    /// Creates a new response for a specific request with initialized LocalService part and STATUS_OK status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Response with initialized LocalService part.</returns>
    public IProtocolMessage<Message> CreateLocalServiceOkResponse(IProtocolMessage<Message> Request)
    {
      var res = CreateOkResponse(Request);
      res.Message.Response.LocalService = new LocalServiceResponse();
      return res;
    }


    /// <summary>
    /// Creates a new RegisterServiceRequest message.
    /// </summary>
    /// <param name="ServiceInfo">Description of the service to register.</param>
    /// <returns>RegisterServiceRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateRegisterServiceRequest(ServiceInfo ServiceInfo)
    {
      RegisterServiceRequest registerServiceRequest = new RegisterServiceRequest();
      registerServiceRequest.Service = ServiceInfo;

      var res = CreateLocalServiceRequest();
      res.Message.Request.LocalService.RegisterService = registerServiceRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a RegisterServiceRequest message.
    /// </summary>
    /// <param name="Request">RegisterServiceRequest message for which the response is created.</param>
    /// <returns>RegisterServiceResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateRegisterServiceResponse(IProtocolMessage<Message> Request, GpsLocation Location)
    {
      RegisterServiceResponse registerServiceResponse = new RegisterServiceResponse();
      registerServiceResponse.Location = new Iop.Locnet.GpsLocation()
      {
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude()
      };

      var res = CreateLocalServiceOkResponse(Request);
      res.Message.Response.LocalService.RegisterService = registerServiceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new DeregisterServiceRequest message.
    /// </summary>
    /// <param name="ServiceType">Type of service to unregister.</param>
    /// <returns>DeregisterServiceRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateDeregisterServiceRequest(ServiceType ServiceType)
    {
      DeregisterServiceRequest deregisterServiceRequest = new DeregisterServiceRequest();
      deregisterServiceRequest.ServiceType = ServiceType;

      var res = CreateLocalServiceRequest();
      res.Message.Request.LocalService.DeregisterService = deregisterServiceRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a DeregisterServiceRequest message.
    /// </summary>
    /// <param name="Request">DeregisterServiceRequest message for which the response is created.</param>
    /// <returns>DeregisterServiceResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateDeregisterServiceResponse(IProtocolMessage<Message> Request)
    {
      DeregisterServiceResponse deregisterServiceResponse = new DeregisterServiceResponse();

      var res = CreateLocalServiceOkResponse(Request);
      res.Message.Response.LocalService.DeregisterService = deregisterServiceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new GetNeighbourNodesByDistanceLocalRequest message.
    /// </summary>
    /// <param name="KeepAlive">If set to true, the LOC server will send neighborhood updates over the open connection.</param>
    /// <returns>GetNeighbourNodesByDistanceLocalRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateGetNeighbourNodesByDistanceLocalRequest(bool KeepAlive = true)
    {
      GetNeighbourNodesByDistanceLocalRequest getNeighbourNodesByDistanceLocalRequest = new GetNeighbourNodesByDistanceLocalRequest();
      getNeighbourNodesByDistanceLocalRequest.KeepAliveAndSendUpdates = KeepAlive;

      var res = CreateLocalServiceRequest();
      res.Message.Request.LocalService.GetNeighbourNodes = getNeighbourNodesByDistanceLocalRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a GetNeighbourNodesByDistanceLocalRequest message.
    /// </summary>
    /// <param name="Request">GetNeighbourNodesByDistanceLocalRequest message for which the response is created.</param>
    /// <param name="Nodes">List of nodes in the neighborhood.</param>
    /// <returns>GetNeighbourNodesByDistanceLocalResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateGetNeighbourNodesByDistanceLocalResponse(IProtocolMessage<Message> Request, IEnumerable<NodeInfo> Nodes)
    {
      GetNeighbourNodesByDistanceResponse getNeighbourNodesByDistanceResponse = new GetNeighbourNodesByDistanceResponse();
      getNeighbourNodesByDistanceResponse.Nodes.AddRange(Nodes);

      var res = CreateLocalServiceOkResponse(Request);
      res.Message.Response.LocalService.GetNeighbourNodes = getNeighbourNodesByDistanceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new NeighbourhoodChangedNotificationRequest message.
    /// </summary>
    /// <param name="Changes">List of changes in the neighborhood.</param>
    /// <returns>NeighbourhoodChangedNotificationRequest message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateNeighbourhoodChangedNotificationRequest(IEnumerable<NeighbourhoodChange> Changes)
    {
      NeighbourhoodChangedNotificationRequest neighbourhoodChangedNotificationRequest = new NeighbourhoodChangedNotificationRequest();
      neighbourhoodChangedNotificationRequest.Changes.AddRange(Changes);

      var res = CreateLocalServiceRequest();
      res.Message.Request.LocalService.NeighbourhoodChanged = neighbourhoodChangedNotificationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a NeighbourhoodChangedNotificationRequest message.
    /// </summary>
    /// <param name="Request">NeighbourhoodChangedNotificationRequest message for which the response is created.</param>
    /// <returns>NeighbourhoodChangedNotificationResponse message that is ready to be sent.</returns>
    public IProtocolMessage<Message> CreateNeighbourhoodChangedNotificationResponse(IProtocolMessage<Message> Request)
    {
      NeighbourhoodChangedNotificationResponse neighbourhoodChangedNotificationResponse = new NeighbourhoodChangedNotificationResponse();

      var res = CreateLocalServiceOkResponse(Request);
      res.Message.Response.LocalService.NeighbourhoodUpdated = neighbourhoodChangedNotificationResponse;

      return res;
    }

  }
}
