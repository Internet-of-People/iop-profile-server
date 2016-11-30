using Google.Protobuf;
using Iop.Locnet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProfileServerCrypto;
using System.Collections;

namespace ProfileServerProtocol
{
  /// <summary>
  /// Allows easy construction of Location Based Network protocol requests and responses.
  /// </summary>
  public class MessageBuilderLocNet
  {
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
    public MessageBuilderLocNet(uint IdBase, List<SemVer> SupportedVersions)
    {
      idBase = (int)IdBase;
      id = idBase;
      supportedVersions = new List<ByteString>();
      foreach (SemVer version in SupportedVersions)
        supportedVersions.Add(version.ToByteString());

      version = supportedVersions[0];
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
    /// Creates a new error response for a specific request with ERROR_INTERNAL status code.
    /// </summary>
    /// <param name="Request">Request message for which the response is created.</param>
    /// <returns>Error response message that is ready to be sent.</returns>
    public Message CreateErrorInternalResponse(Message Request)
    {
      return CreateResponse(Request, Status.ErrorInternal);
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
    /// Creates a new RegisterServiceRequest message.
    /// </summary>
    /// <param name="ServiceType">Type of service to register.</param>
    /// <param name="NodeProfile">Node profile with interface to register.</param>
    /// <returns>RegisterServiceRequest message that is ready to be sent.</returns>
    public Message CreateRegisterServiceRequest(ServiceType ServiceType, NodeProfile NodeProfile)
    {
      RegisterServiceRequest registerServiceRequest = new RegisterServiceRequest();
      registerServiceRequest.ServiceType = ServiceType;
      registerServiceRequest.NodeProfile = NodeProfile;

      Message res = CreateRequest();
      res.Request.LocalService.RegisterService = registerServiceRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a RegisterServiceRequest message.
    /// </summary>
    /// <param name="Request">RegisterServiceRequest message for which the response is created.</param>
    /// <returns>RegisterServiceResponse message that is ready to be sent.</returns>
    public Message CreateRegisterServiceResponse(Message Request)
    {
      RegisterServiceResponse registerServiceResponse = new RegisterServiceResponse();

      Message res = CreateOkResponse(Request);
      res.Response.LocalService.RegisterService = registerServiceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new DeregisterServiceRequest message.
    /// </summary>
    /// <param name="ServiceType">Type of service to unregister.</param>
    /// <returns>DeregisterServiceRequest message that is ready to be sent.</returns>
    public Message CreateDeregisterServiceRequest(ServiceType ServiceType)
    {
      DeregisterServiceRequest deregisterServiceRequest = new DeregisterServiceRequest();
      deregisterServiceRequest.ServiceType = ServiceType;

      Message res = CreateRequest();
      res.Request.LocalService.DeregisterService = deregisterServiceRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a DeregisterServiceRequest message.
    /// </summary>
    /// <param name="Request">DeregisterServiceRequest message for which the response is created.</param>
    /// <returns>DeregisterServiceResponse message that is ready to be sent.</returns>
    public Message CreateDeregisterServiceResponse(Message Request)
    {
      DeregisterServiceResponse deregisterServiceResponse = new DeregisterServiceResponse();

      Message res = CreateOkResponse(Request);
      res.Response.LocalService.DeregisterService = deregisterServiceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new GetNeighbourNodesByDistanceLocalRequest message.
    /// </summary>
    /// <param name="KeepAlive">If set to true, the LBN server will send neighborhood updates over the open connection.</param>
    /// <returns>GetNeighbourNodesByDistanceLocalRequest message that is ready to be sent.</returns>
    public Message CreateGetNeighbourNodesByDistanceLocalRequest(bool KeepAlive)
    {
      GetNeighbourNodesByDistanceLocalRequest getNeighbourNodesByDistanceLocalRequest = new GetNeighbourNodesByDistanceLocalRequest();
      getNeighbourNodesByDistanceLocalRequest.KeepAliveAndSendUpdates = KeepAlive;

      Message res = CreateRequest();
      res.Request.LocalService.GetNeighbourNodes = getNeighbourNodesByDistanceLocalRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a GetNeighbourNodesByDistanceLocalRequest message.
    /// </summary>
    /// <param name="Request">GetNeighbourNodesByDistanceLocalRequest message for which the response is created.</param>
    /// <param name="Nodes">List of nodes in the neighborhood.</param>
    /// <returns>GetNeighbourNodesByDistanceLocalResponse message that is ready to be sent.</returns>
    public Message CreateGetNeighbourNodesByDistanceLocalResponse(Message Request, IEnumerable<NodeInfo> Nodes)
    {
      GetNeighbourNodesByDistanceResponse getNeighbourNodesByDistanceResponse = new GetNeighbourNodesByDistanceResponse();
      getNeighbourNodesByDistanceResponse.Nodes.AddRange(Nodes);

      Message res = CreateOkResponse(Request);
      res.Response.LocalService.GetNeighbourNodes = getNeighbourNodesByDistanceResponse;

      return res;
    }


    /// <summary>
    /// Creates a new NeighbourhoodChangedNotificationRequest message.
    /// </summary>
    /// <param name="Changes">List of changes in the neighborhood.</param>
    /// <returns>NeighbourhoodChangedNotificationRequest message that is ready to be sent.</returns>
    public Message CreateNeighbourhoodChangedNotificationRequest(IEnumerable<NeighbourhoodChange> Changes)
    {
      NeighbourhoodChangedNotificationRequest neighbourhoodChangedNotificationRequest = new NeighbourhoodChangedNotificationRequest();
      neighbourhoodChangedNotificationRequest.Changes.AddRange(Changes);

      Message res = CreateRequest();
      res.Request.LocalService.NeighbourhoodChanged = neighbourhoodChangedNotificationRequest;

      return res;
    }


    /// <summary>
    /// Creates a response message to a NeighbourhoodChangedNotificationRequest message.
    /// </summary>
    /// <param name="Request">NeighbourhoodChangedNotificationRequest message for which the response is created.</param>
    /// <returns>NeighbourhoodChangedNotificationResponse message that is ready to be sent.</returns>
    public Message CreateNeighbourhoodChangedNotificationResponse(Message Request)
    {
      NeighbourhoodChangedNotificationResponse neighbourhoodChangedNotificationResponse = new NeighbourhoodChangedNotificationResponse();

      Message res = CreateOkResponse(Request);
      res.Response.LocalService.NeighbourhoodUpdated = neighbourhoodChangedNotificationResponse;

      return res;
    }

  }
}
