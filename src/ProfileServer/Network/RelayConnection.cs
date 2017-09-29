using ProfileServer.Kernel;
using IopCrypto;
using IopProtocol;
using Iop.Profileserver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Network;
using Google.Protobuf;
using Iop.Shared;

namespace ProfileServer.Network
{
  /// <summary>Possible statuses of a relay.</summary>
  public enum RelayConnectionStatus
  {
    /// <summary>
    /// The caller initiated the call by sending CallIdentityApplicationServiceRequest
    /// and the profile server sent IncomingCallNotificationRequest to the callee.
    /// The profile server is waiting for IncomingCallNotificationResponse.
    /// </summary>
    WaitingForCalleeResponse,

    /// <summary>
    /// Both clients are now connected to clAppService port and their initialization message has been processed.
    /// They are now free to communicate among each other.
    /// </summary>
    Open,

    /// <summary>
    /// Relay has been destroyed.
    /// </summary>
    Destroyed
  }


  /// <summary>
  /// Context 
  /// </summary>
  public class RelayMessageContext
  {
    /// <summary>Open relay over which the message was sent.</summary>
    public RelayConnection Relay { get; }

    /// <summary>ApplicationServiceSendMessageRequest from sender.</summary>
    public IProtocolMessage<Message> SenderRequest { get; }

    /// <summary>
    /// Initializes the relay message context.
    /// </summary>
    public RelayMessageContext(RelayConnection relay, IProtocolMessage<Message> request)
    {
      this.Relay = relay;
      this.SenderRequest = request;
    }
  }


  /// <summary>
  /// Implementation of client to client network channel over the profile server bridge.
  /// </summary>
  public class RelayConnection : IDisposable
  {
    /// <summary>
    /// Time in seconds given to the callee to accept or reject the incoming call.
    /// If the callee fails to deliver a response to the incoming call in the time limit, 
    /// ERROR_NOT_AVAILABLE is send to the caller.
    /// </summary>
    public const int CalleeResponseCallNotificationDelayMaxSeconds = 10;

    /// <summary>Instance logger.</summary>
    private Logger _log;

    /// <summary>Lock object to protect access to relay.</summary>
    private SemaphoreSlim _lock;

    /// <summary>Status of the relay.</summary>
    private RelayConnectionStatus _status;

    /// <summary>
    /// Caller's network client.
    /// <para>
    /// If the status is WaitingForCalleeResponse, this is the client on clNonCustomer port or clCustomer port.
    /// </para>
    /// <para>
    /// If the status is WaitingForFirstInitMessage or WaitingForSecondInitMessage, this is the client on clAppService port 
    /// or null if the caller is not yet connected to clAppService port.
    /// </para>
    /// </summary>
    private IncomingClient _caller;

    /// <summary>
    /// Callee's network client.
    /// <para>
    /// If the status is WaitingForCalleeResponse, this is the client on clCustomer port.
    /// </para>
    /// <para>
    /// If the status is WaitingForFirstInitMessage or WaitingForSecondInitMessage, this is the client on clAppService port 
    /// or null if the callee is not yet connected to clAppService port.
    /// </para>
    /// </summary>
    private IncomingClient _callee;

    /// <summary>Name of the application service of the callee that is being used for the communication.</summary>
    private string _serviceName;

    /// <summary>
    /// If relay status is WaitingForCalleeResponse, this is a timer that expires if the callee fails to 
    /// reply to the incoming call notification request within a reasonable time.
    /// <para>
    /// If relay status is WaitingForFirstInitMessage, this is a timer that expires if neither of the clients 
    /// sends its initial message on the clAppService port within a reasonable time.
    /// </para>
    /// <para>
    /// If relay status is WaitingForSecondInitMessage, this is a timer that expires if the second peer fails 
    /// to send its initial message on the clAppService port within a reasonable time.
    /// </para>
    /// </summary>
    private Timer _timeoutTimer;

    /// <summary>
    /// If relay status is WaitingForCalleeResponse, this is the message that the caller sent to initiate the call.
    /// 
    /// <para>
    /// If relay status is WaitingForFirstInitMessage or WaitingForSecondInitMessage, this is the initialization message of the first client.
    /// </para>
    /// </summary>
    private readonly IProtocolMessage<Message> _callRequest;

    private readonly RelayList _relayList;

    /// <summary>
    /// Creates a new relay connection from a caller to a callee using a specific application service.
    /// </summary>
    /// <param name="caller">Network client of the caller.</param>
    /// <param name="callee">Network client of the callee.</param>
    /// <param name="serviceName">Name of the application service of the callee that is being used for the call.</param>
    /// <param name="request">CallIdentityApplicationServiceRequest message that the caller send in order to initiate the call.</param>
    public RelayConnection(RelayList relayList, IncomingClient caller, IncomingClient callee, string serviceName, IProtocolMessage<Message> request)
    {
      _lock = new SemaphoreSlim(1);

      Id = Guid.NewGuid();
      CallerToken = Guid.NewGuid();
      CalleeToken = Guid.NewGuid();
      _relayList = relayList;
      _caller = caller;
      _callee = callee;
      _callRequest = request;
      _serviceName = serviceName;
      _status = RelayConnectionStatus.WaitingForCalleeResponse;

      string logPrefix = string.Format("[{0}:{1}] ", Id, serviceName);
      string logName = "ProfileServer.Network.ClientList";
      _log = new Logger(logName, logPrefix);
      _log.Trace("(Caller.Id:{0},Callee.Id:{1},ServiceName:'{2}')", caller.Id.ToHex(), callee.Id.ToHex(), serviceName);
      _log.Trace("Caller token is '{0}'.", CallerToken);
      _log.Trace("Callee token is '{0}'.", CalleeToken);

      // Relay is created by caller's request, it will expire if the callee does not reply within reasonable time.
      _timeoutTimer = new Timer(TimeoutCallback, _status, CalleeResponseCallNotificationDelayMaxSeconds * 1000, Timeout.Infinite);

      _log.Trace("(-)");
    }

    /// <summary>
    /// Obtains relay identifier.
    /// </summary>
    /// <returns>Relay identifier.</returns>
    public Guid Id { get; }


    /// <summary>
    /// Obtains relay callee's token.
    /// </summary>
    /// <returns>Relay callee's token.</returns>
    public Guid CalleeToken { get; }

    /// <summary>
    /// Obtains relay caller's token.
    /// </summary>
    /// <returns>Relay caller's token.</returns>
    public Guid CallerToken { get; }


    /// <summary>
    /// Callback routine that is called once the timeoutTimer expires.
    /// <para>
    /// If relay status is WaitingForCalleeResponse, the callee has to reply to the incoming call notification 
    /// within a reasonable time. If it does the timer is cancelled. If it does not, the timeout occurs.
    /// </para>
    /// <para>
    /// If relay status is WaitingForFirstInitMessage, both clients are expected to connect to clAppService port 
    /// and send an initial message over that service. The timeoutTimer expires when none of the clients 
    /// connects to clAppService port and sends its initialization message within a reasonable time.
    /// </para>
    /// <para>
    /// Then if relay status is WaitingForSecondInitMessage, the profile server receives a message from the first client 
    /// on clAppService port, it starts the timer again, which now expires if the second client does not connect 
    /// and send its initial message within a reasonable time. 
    /// </para>
    /// </summary>
    /// <param name="state">Status of the relay when the timer was installed.</param>
    private async void TimeoutCallback(object state)
    {
      LogDiagnosticContext.Start();

      RelayConnectionStatus stateAtStartTimer = (RelayConnectionStatus)state;
      _log.Trace("(State:{0})", stateAtStartTimer);

      IncomingClient clientToSendMessage = null;
      IProtocolMessage<Message> messageToSend = null;
      bool destroyRelay = false;

      await _lock.WaitAsync();

      if (_timeoutTimer != null)
      {
        switch (_status)
        {
          case RelayConnectionStatus.WaitingForCalleeResponse:
            {
              // The caller requested the call and the callee was notified.
              // The callee failed to send us response on time, this is situation 2)
              // from ProcessMessageCallIdentityApplicationServiceRequestAsync.
              // We send ERROR_NOT_AVAILABLE to the caller and destroy the relay.
              _log.Debug("Callee failed to reply to the incoming call notification, closing relay.");

              clientToSendMessage = _caller;
              messageToSend = _caller.MessageBuilder.CreateErrorNotAvailableResponse(_callRequest);
              break;
            }

          default:
            _log.Debug("Time out triggered while the relay status was {0}.", _status);
            break;
        }

        // In case of any timeouts, we just destroy the relay.
        destroyRelay = true;
      }
      else _log.Debug("Timeout timer has already been destroyed, no action taken.");

      _lock.Release();


      if (messageToSend != null)
      {
        if (!await clientToSendMessage.SendMessageAsync(messageToSend))
          _log.Warn("Unable to send message to the client ID {0}, maybe it is not connected anymore.", clientToSendMessage.Id.ToHex());
      }

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
        await serverComponent.RelayList.DestroyNetworkRelay(this);
      }

      _log.Trace("(-)");

      LogDiagnosticContext.Stop();
    }

    /// <summary>
    /// Cancels timeoutTimer. Relay's lockObject has to be acquired.
    /// </summary>
    private void CancelTimeout()
    {
      _log.Trace("()");

      if (_timeoutTimer != null)
        _timeoutTimer.Dispose();
      _timeoutTimer = null;

      _log.Trace("(-)");
    }


    /// <summary>
    /// Handles situation when the callee replied to the incoming call notification request.
    /// </summary>
    /// <param name="response">Full response message from the callee.</param>
    /// <param name="request">Unfinished call request message of the caller that corresponds to the response message.</param>
    /// <returns></returns>
    public async Task<bool> CalleeRepliedToIncomingCallNotification(IProtocolMessage<Message> response, UnfinishedRequest<Message> request)
    {
      return await _log.TraceFuncAsync("()", async () => {
        await _lock.WaitAsync();
        try
        {

          if (_status != RelayConnectionStatus.WaitingForCalleeResponse)
          {
                  // The relay has probably been destroyed, or something bad happened to it.
                  // We take no action here regardless of what the callee's response is.
                  // If it rejected the call, there is nothing to be done since we do not have 
                  // any connection to the caller anymore.
                  _log.Debug("Relay status is {0}, nothing to be done.", _status);
              return false;
          }

          CancelTimeout();

          // The caller is still connected and waiting for an answer to its call request.
          Status responseStatus = response.Message.Response.Status;
          if (responseStatus == Status.Ok)
          {
              await _caller.SendMessageAsync(_caller.MessageBuilder.CreateCallIdentityApplicationServiceResponse(_callRequest, CallerToken.ToByteArray()));
                  _status = RelayConnectionStatus.Open;
                  _log.Debug("Relay status has been changed to {0}", _status);

              return true;
          }

          _log.Warn($"Callee '{_callee.Id.ToHex()}' sent error response '{responseStatus}' for call from {_caller.Id.ToHex()}");

          var callResponse = _caller.MessageBuilder.CreateResponse(_callRequest, responseStatus, response.Message.Response.Details);

          await _caller.SendMessageAsync(callResponse);
          await _relayList.DestroyNetworkRelay(this);
          return false;
        }
        finally
        {
          _lock.Release();
        }
      });
    }


    /// <summary>
    /// Processes ApplicationServiceSendMessageRequest message from a client.
    /// <para>
    /// Relay received message from one client and sends it to the other one. If this is the first request 
    /// a client sends after it connects to clAppService port, the request's message is ignored and the reply is sent 
    /// to the client as the other client is confirmed to join the relay.</para>
    /// </summary>
    /// <param name="client">Client that sent the message.</param>
    /// <param name="request">Full request message.</param>
    /// <param name="token">Sender's relay token.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<IProtocolMessage<Message>> ProcessIncomingMessage(IncomingClient client, IProtocolMessage<Message> request, Guid token)
    {
      return await _log.TraceFuncAsync("()", async () => {

        await _lock.WaitAsync();
        try
        {

          var (otherClient, otherToken) = CallerToken.Equals(token) ? (_callee, CalleeToken) : (_caller, CallerToken);
          if (_status == RelayConnectionStatus.Open && client.Relay == this)
          {
            // Relay is open, this means that all incoming messages are sent to the other client.
            byte[] payload = request.Message.Request.SingleRequest.ApplicationServiceSendMessage.Message.ToByteArray();
            var relayedMessage = otherClient.MessageBuilder.CreateApplicationServiceSendMessageRequest(otherToken.ToByteArray(), payload);
            RelayMessageContext context = new RelayMessageContext(this, request);

            if (await otherClient.SendMessageAndSaveUnfinishedRequestAsync(relayedMessage, context))
              return null;
          }

        }
        finally
        {
          _lock.Release();
        }

        await _relayList.DestroyNetworkRelay(this);
        if ((this != client.Relay) && (client.Relay != null))
          await _relayList.DestroyNetworkRelay(client.Relay);

        client.ForceDisconnect = true;
        return client.MessageBuilder.CreateErrorNotFoundResponse(request);
      });
    }


    /// <summary>
    /// Processes incoming confirmation from the message recipient over the relay.
    /// </summary>
    /// <param name="client">Client that sent the response.</param>
    /// <param name="response">Full response message.</param>
    /// <param name="request">Sender request message that the recipient confirmed.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> RecipientConfirmedMessage(IncomingClient client, IProtocolMessage<Message> response, IProtocolMessage<Message> request)
    {
      return await _log.TraceFuncAsync("()", async () => {

        await _lock.WaitAsync();
        try
        {

          if (_status == RelayConnectionStatus.Open)
          {
            IncomingClient otherClient = client == _caller ? _callee : _caller;
            var responseStatus = response.Message.Response.Status;
            _log.Trace($"Received confirmation (status code {responseStatus}) from client ID {client.Id.ToHex()} of a message sent by client ID {otherClient.Id.ToHex()}.");

            if (responseStatus == Status.Ok)
            {

              // We have received a confirmation from the recipient, so we just complete the sender's request to inform it that the message was delivered.
              var otherClientResponse = otherClient.MessageBuilder.CreateApplicationServiceSendMessageResponse(request);
              if (await otherClient.SendMessageAsync(otherClientResponse))
                return true;

            }
            else
            {
              // We have received error from the recipient, so we forward it to the sender and destroy the relay.
              var errorResponse = otherClient.MessageBuilder.CreateErrorNotFoundResponse(request);

              if (!await otherClient.SendMessageAsync(errorResponse))
                _log.Warn($"Unable to send error response to the sender client ID {otherClient.Id.ToHex()}, maybe it is disconnected already, destroying the relay.");
            }
          }

          await _relayList.DestroyNetworkRelay(this);
          return false;

        }
        finally
        {
          _lock.Release();
        }

      });
    }


    /// <summary>
    /// Handles situation when a client connected to a relay disconnected. 
    /// However, the closed connection might be either connection to clCustomer/clNonCustomer port, 
    /// or it might be connection to clAppService port.
    /// </summary>
    /// <param name="client">Client that disconnected.</param>
    /// <param name="isRelayConnection">true if the closed connection was to clAppService port, false otherwise.</param>
    public async Task HandleDisconnectedClient(IncomingClient client, bool isRelayConnection)
    {
      _log.Trace($"(Client.Id:{client.Id.ToHex()})");
      await _lock.WaitAsync();
      try
      {
        bool isCallee = client == _callee;
        _log.Trace($"{(isCallee ? "Callee" : "Caller")} '{client.Id.ToHex()}' disconnected, relay with status {_status}.");

        if (_status == RelayConnectionStatus.WaitingForCalleeResponse)
        {
          if (isCallee)
          {
            var message = _caller.MessageBuilder.CreateErrorNotAvailableResponse(_callRequest);
            await _caller.SendMessageAsync(message);
          }
          else
          {
            _log.Trace("Caller disconnected while waiting for call setup.");
          }
          await _relayList.DestroyNetworkRelay(this);
        }
        else if (_status == RelayConnectionStatus.Open)
        {
          IncomingClient otherClient = isCallee ? _caller : _callee;

          // Find all unfinished requests from this relay.
          // When a client sends ApplicationServiceSendMessageRequest, the profile server creates ApplicationServiceReceiveMessageNotificationRequest 
          // and adds it as an unfinished request with context set to RelayMessageContext, which contains the sender's ApplicationServiceSendMessageRequest.
          // This unfinished message is in the list of unfinished message of the recipient.
          var unfinishedRelayRequests = client.GetAndRemoveUnfinishedRequests();
          foreach (var unfinishedRequest in unfinishedRelayRequests)
          {
            Message unfinishedRequestMessage = (Message)unfinishedRequest.RequestMessage.Message;
            // Find ApplicationServiceReceiveMessageNotificationRequest request messages sent to the client who closed the connection.
            if ((unfinishedRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request)
              && (unfinishedRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
              && (unfinishedRequestMessage.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification))
            {
              // This unfinished request's context holds ApplicationServiceSendMessageRequest message of the client that is still connected.
              RelayMessageContext ctx = (RelayMessageContext)unfinishedRequest.Context;
              var responseError = otherClient.MessageBuilder.CreateErrorNotFoundResponse(ctx.SenderRequest);
              await otherClient.SendMessageAsync(responseError);
            }
          }

          await _relayList.DestroyNetworkRelay(this);
        }

      }
      finally
      {
        _lock.Release();
        _log.Trace("(-)");
      }

    }


    /// <summary>
    /// Checks if the the relay has been destroyed already and changes its status to Destroyed.
    /// </summary>
    /// <returns>true if the relay has been destroyed already, false otherwise.</returns>
    public async Task<bool> TestAndSetDestroyed()
    {
      _log.Trace("()");

      bool res = false;

      await _lock.WaitAsync();

      res = _status == RelayConnectionStatus.Destroyed;
      _status = RelayConnectionStatus.Destroyed;

      _lock.Release();

      _log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool _disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object _disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class.
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="disposing"/> is set.
    /// </summary>
    /// <param name="disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
      if (_disposed) return;
      if (!disposing) return;

      lock (_disposingLock)
      {
        _lock.Wait();

        _status = RelayConnectionStatus.Destroyed;
        CancelTimeout();

        _lock.Release();
        _disposed = true;
      }
    }
  }
}
