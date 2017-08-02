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
    /// The callee replied to IncomingCallNotificationRequest with IncomingCallNotificationResponse and thus accepted the call.
    /// The caller was informed in response to its CallIdentityApplicationServiceRequest that the callee accepted the call.
    /// Both parties are now expected to call to clAppService port and send their initial messages.
    /// We are now waiting for the first client to connect and send its initialization message.
    /// </summary>
    WaitingForFirstInitMessage,

    /// <summary>
    /// One of the client already connected to clAppService port and it is waiting for the other one.
    /// We are now waiting for the second client to connect and send its initialization message.
    /// </summary>
    WaitingForSecondInitMessage,

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

    /// <summary>
    /// Time in seconds given to the first client to connect to clAppService port 
    /// and send its initialization message.
    /// </summary>
    public const int FirstAppServiceInitializationMessageDelayMaxSeconds = 30;

    /// <summary>
    /// Time in seconds given to the second client to connect to clAppService port 
    /// and send its initialization message.
    /// </summary>
    public const int SecondAppServiceInitializationMessageDelayMaxSeconds = 30;

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
    private IProtocolMessage<Message> _pendingMessage;

    /// <summary>
    /// Creates a new relay connection from a caller to a callee using a specific application service.
    /// </summary>
    /// <param name="caller">Network client of the caller.</param>
    /// <param name="callee">Network client of the callee.</param>
    /// <param name="serviceName">Name of the application service of the callee that is being used for the call.</param>
    /// <param name="request">CallIdentityApplicationServiceRequest message that the caller send in order to initiate the call.</param>
    public RelayConnection(IncomingClient caller, IncomingClient callee, string serviceName, IProtocolMessage<Message> request)
    {
      _lock = new SemaphoreSlim(1);

      Id = Guid.NewGuid();
      CallerToken = Guid.NewGuid();
      CalleeToken = Guid.NewGuid();
      _caller = caller;
      _callee = callee;
      _pendingMessage = request;
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

      RelayConnectionStatus previousStatus = (RelayConnectionStatus)state;
      _log.Trace("(State:{0})", previousStatus);

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
              messageToSend = _caller.MessageBuilder.CreateErrorNotAvailableResponse(_pendingMessage);
              break;
            }

          case RelayConnectionStatus.WaitingForFirstInitMessage:
            {
              // Neither client joined the channel on time, nothing to do, just destroy the relay.
              _log.Debug("None of the clients joined the relay on time, closing relay.");
              break;
            }

          case RelayConnectionStatus.WaitingForSecondInitMessage:
            {
              // One client is waiting for the other one to join, but the other client failed to join on time.
              // We send ERROR_NOT_FOUND to the waiting client and close its connection.
              _log.Debug("{0} failed to join the relay on time, closing relay.", _callee != null ? "Caller" : "Callee");

              clientToSendMessage = _callee != null ? _callee : _caller;
              messageToSend = clientToSendMessage.MessageBuilder.CreateErrorNotFoundResponse(_pendingMessage);
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
    /// Cancels timeoutTimer.
    /// </summary>
    private void CancelTimeoutTimer()
    {
      _log.Trace("()");

      _lock.Wait();

      CancelTimeoutTimerLocked();

      _lock.Release();

      _log.Trace("(-)");
    }

    /// <summary>
    /// Cancels timeoutTimer. Relay's lockObject has to be acquired.
    /// </summary>
    private void CancelTimeoutTimerLocked()
    {
      _log.Trace("()");

      if (_timeoutTimer != null) _timeoutTimer.Dispose();
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
      _log.Trace("()");

      bool res = false;

      bool destroyRelay = false;
      IncomingClient clientToSendMessage = null;
      IProtocolMessage<Message> messageToSend = null;

      await _lock.WaitAsync();


      if (_status == RelayConnectionStatus.WaitingForCalleeResponse)
      {
        CancelTimeoutTimerLocked();

        // The caller is still connected and waiting for an answer to its call request.
        if (response.Message.Response.Status == Status.Ok)
        {
          // The callee is now expected to connect to clAppService with its token.
          // We need to inform caller that the callee accepted the call.
          // This is option 4) from ProcessMessageCallIdentityApplicationServiceRequestAsync.
          messageToSend = _caller.MessageBuilder.CreateCallIdentityApplicationServiceResponse(_pendingMessage, CallerToken.ToByteArray());
          clientToSendMessage = _caller;
          _pendingMessage = null;

          _caller = null;
          _callee = null;
          _status = RelayConnectionStatus.WaitingForFirstInitMessage;
          _log.Debug("Relay status has been changed to {0}", _status);

          /// Install timeoutTimer to expire if the first client does not connect to clAppService port 
          /// and send its initialization message within reasonable time.
          _timeoutTimer = new Timer(TimeoutCallback, RelayConnectionStatus.WaitingForFirstInitMessage, FirstAppServiceInitializationMessageDelayMaxSeconds * 1000, Timeout.Infinite);

          res = true;
        }
        else
        {
          // The callee rejected the call or reported other error.
          // These are options 3) and 2) from ProcessMessageCallIdentityApplicationServiceRequestAsync.
          if (response.Message.Response.Status == Status.ErrorRejected)
          {
            _log.Debug("Callee ID '{0}' rejected the call from caller identity ID '{1}'", _callee.Id.ToHex(), _caller.Id.ToHex());
            messageToSend = _caller.MessageBuilder.CreateErrorRejectedResponse(_pendingMessage);
          }
          else
          {
            _log.Warn("Callee ID '0} sent error response '{1}' for call request from caller identity ID {2}", 
              _callee.Id.ToHex(), response.Message.Response.Status, _caller.Id.ToHex());

            messageToSend = _caller.MessageBuilder.CreateErrorNotAvailableResponse(_pendingMessage);
          }

          clientToSendMessage = _caller;
          destroyRelay = true;
        }
      }
      else
      {
        // The relay has probably been destroyed, or something bad happened to it.
        // We take no action here regardless of what the callee's response is.
        // If it rejected the call, there is nothing to be done since we do not have 
        // any connection to the caller anymore.
        _log.Debug("Relay status is {0}, nothing to be done.", _status);
      }
      
      _lock.Release();


      if (messageToSend != null)
      {
        if (await clientToSendMessage.SendMessageAsync(messageToSend)) _log.Debug("Response to call initiation request sent to the caller ID {0}.", clientToSendMessage.Id.ToHex());
        else _log.Debug("Unable to reponse to call initiation request to the caller ID {0}.", clientToSendMessage.Id.ToHex());
      }

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
        await serverComponent.RelayList.DestroyNetworkRelay(this);
      }

      _log.Trace("(-):{0}", res);
      return res;
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
      _log.Trace("()");

      IProtocolMessage<Message> res = null;
      bool destroyRelay = false;

      await _lock.WaitAsync();

      bool isCaller = CallerToken.Equals(token);

      IncomingClient otherClient = isCaller ? _callee : _caller;
      _log.Trace("Received message over relay '{0}' in status {1} with client ID {2} being {3} and the other client ID {4} is {5}.",
        Id, _status, client.Id.ToHex(), isCaller ? "caller" : "callee", otherClient != null ? otherClient.Id.ToHex() : "N/A", isCaller ? "callee" : "caller");

      switch (_status)
      {
        case RelayConnectionStatus.WaitingForCalleeResponse:
          {
            if (!isCaller)
            {
              // We have received a message from callee, but we did not receive its IncomingCallNotificationResponse.
              // This may be OK if this message has been sent by callee and it just not has been processed before 
              // the callee connected to clAppService port and sent us the initialization message.
              // In this case we will try to wait a couple of seconds and see if we receive IncomingCallNotificationResponse.
              // If yes, we continue as if we processed the message in the right order.
              // In all other cases, this is a fatal error and we have to destroy the relay.
              _lock.Release();

              bool statusChanged = false;
              _log.Warn("Callee sent initialization message before we received IncomingCallNotificationResponse. We will wait to see if it arrives soon.");
              for (int i = 0; i < 5; i++)
              {
                _log.Warn("Attempt #{0}, waiting 1 second.", i + 1);
                await Task.Delay(1000);

                await _lock.WaitAsync();

                _log.Warn("Attempt #{0}, checking relay status.", i + 1);
                if (_status != RelayConnectionStatus.WaitingForCalleeResponse)
                {
                  _log.Warn("Attempt #{0}, relay status changed to {1}.", i + 1, _status);
                  statusChanged = true;
                }

                _lock.Release();

                if (statusChanged) break;
              }

              await _lock.WaitAsync();
              if (statusChanged)
              {
                // Status of relay has change, which means either it has been destroyed already, or the IncomingCallNotificationResponse 
                // message we were waiting for arrived. In any case, we call this method recursively, but it can not happen that we would end up here again.
                _lock.Release();

                _log.Trace("Calling ProcessIncomingMessage recursively.");
                res = await ProcessIncomingMessage(client, request, token);

                await _lock.WaitAsync();
              }
              else
              {
                _log.Trace("Message received from caller and relay status is WaitingForCalleeResponse and IncomingCallNotificationResponse did not arrive, closing connection to client, destroying relay.");
                res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
                client.ForceDisconnect = true;
                destroyRelay = true;
              }
            }
            else
            {
              _log.Trace("Message received from caller and relay status is WaitingForCalleeResponse, closing connection to client, destroying relay.");
              res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
              client.ForceDisconnect = true;
              destroyRelay = true;
            }
            break;
          }

        case RelayConnectionStatus.WaitingForFirstInitMessage:
          {
            _log.Debug("Received an initialization message from the first client ID '{0}', waiting for the second client.", client.Id.ToHex());
            CancelTimeoutTimerLocked();

            if (client.Relay == null)
            {
              client.Relay = this;

              // Other peer is not connected yet, so we put this request on hold and wait for the other client.
              if (isCaller) _caller = client;
              else _callee = client;

              _status = RelayConnectionStatus.WaitingForSecondInitMessage;
              _log.Trace("Relay status changed to {0}", _status);

              _pendingMessage = request;
              _timeoutTimer = new Timer(TimeoutCallback, _status, SecondAppServiceInitializationMessageDelayMaxSeconds * 1000, Timeout.Infinite);

              // res remains null, which is OK as the request is put on hold until the other client joins the channel.
            }
            else
            {
              // Client already sent us the initialization message, this is protocol violation error, destroy the relay.
              // Since the relay should be upgraded to WaitingForSecondInitMessage status, this can happen 
              // only if a client does not use a separate connection for each clAppService session, which is forbidden.
              _log.Debug("Client ID {0} probably uses a single connection for two relays. Both relays will be destroyed.", client.Id.ToHex());
              res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
              destroyRelay = true;
            }
            break;
          }

        case RelayConnectionStatus.WaitingForSecondInitMessage:
          {
            _log.Debug("Received an initialization message from the second client");
            CancelTimeoutTimerLocked();

            if (client.Relay == null)
            {
              client.Relay = this;

              // Other peer is connected already, so we just inform it by sending response to its initial ApplicationServiceSendMessageRequest.
              if (isCaller) _caller = client;
              else _callee = client;

              _status = RelayConnectionStatus.Open;
              _log.Trace("Relay status changed to {0}.", _status);

              var otherClientResponse = otherClient.MessageBuilder.CreateApplicationServiceSendMessageResponse(_pendingMessage);
              _pendingMessage = null;
              if (await otherClient.SendMessageAsync(otherClientResponse))
              {
                // And we also send reply to the second client that the channel is now ready for communication.
                res = client.MessageBuilder.CreateApplicationServiceSendMessageResponse(request);
              }
              else
              {
                _log.Warn("Unable to send message to other client ID {0}, closing connection to client and destroying the relay.", otherClient.Id.ToHex());
                res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
                client.ForceDisconnect = true;
                destroyRelay = true;
              }
            }
            else
            {
              // Client already sent us the initialization message, this is error, destroy the relay.
              _log.Debug("Client ID {0} sent a message before receiving a reply to its initialization message. Relay will be destroyed.", client.Id.ToHex());
              res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
              destroyRelay = true;
            }

            break;
          }


        case RelayConnectionStatus.Open:
          {
            if (client.Relay == this)
            {
              // Relay is open, this means that all incoming messages are sent to the other client.
              byte[] messageForOtherClient = request.Message.Request.SingleRequest.ApplicationServiceSendMessage.Message.ToByteArray();
              var otherClientMessage = otherClient.MessageBuilder.CreateApplicationServiceReceiveMessageNotificationRequest(messageForOtherClient);
              RelayMessageContext context = new RelayMessageContext(this, request);
              if (await otherClient.SendMessageAndSaveUnfinishedRequestAsync(otherClientMessage, context))
              {
                // res is null, which is fine, the sender is put on hold and we will get back to it once the recipient confirms that it received the message.
                _log.Debug("Message from client ID {0} has been relayed to other client ID {1}.", client.Id.ToHex(), otherClient.Id.ToHex());
              }
              else
              {
                _log.Warn("Unable to relay message to other client ID {0}, closing connection to client and destroying the relay.", otherClient.Id.ToHex());
                res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
                client.ForceDisconnect = true;
                destroyRelay = true;
              }
            }
            else
            {
              // This means that the client used a single clAppService port connection for two different relays, which is forbidden.
              _log.Warn("Client ID {0} mixed relay '{1}' with relay '{2}', closing connection to client and destroying both relays.", otherClient.Id.ToHex(), client.Relay.Id, Id);
              res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
              client.ForceDisconnect = true;
              destroyRelay = true;
            }

            break;
          }

        case RelayConnectionStatus.Destroyed:
          {
            _log.Trace("Relay has been destroyed, closing connection to client.");
            res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
            client.ForceDisconnect = true;
            break;
          }
          
        default:
          _log.Trace("Relay status is '{0}', closing connection to client, destroying relay.", _status);
          res = client.MessageBuilder.CreateErrorNotFoundResponse(request);
          client.ForceDisconnect = true;
          destroyRelay = true;
          break;
      }      

      _lock.Release();

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
        await serverComponent.RelayList.DestroyNetworkRelay(this);
        if ((this != client.Relay) && (client.Relay != null))
          await serverComponent.RelayList.DestroyNetworkRelay(client.Relay);
      }

      _log.Trace("(-)");
      return res;
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
      _log.Trace("()");

      bool res = false;
      bool destroyRelay = false;

      await _lock.WaitAsync();

      if (_status == RelayConnectionStatus.Open)
      {
        bool isCaller = client == _caller;

        IncomingClient otherClient = isCaller ? _callee : _caller;
        _log.Trace($"Received confirmation (status code {response.Message.Response.Status}) from client ID {client.Id.ToHex()} of a message sent by client ID {otherClient.Id.ToHex()}.");

        if (response.Message.Response.Status == Status.Ok)
        {
          // We have received a confirmation from the recipient, so we just complete the sender's request to inform it that the message was delivered.
          var otherClientResponse = otherClient.MessageBuilder.CreateApplicationServiceSendMessageResponse(request);
          if (await otherClient.SendMessageAsync(otherClientResponse))
          {
            res = true;
          }
          else
          {
            _log.Warn($"Unable to send message to other client ID '0x{otherClient.Id:X16}', closing connection to client and destroying the relay.");
            destroyRelay = true;
          }
        }
        else
        {
          // We have received error from the recipient, so we forward it to the sender and destroy the relay.
          var errorResponse = otherClient.MessageBuilder.CreateErrorNotFoundResponse(request);

          if (!await otherClient.SendMessageAsync(errorResponse))
            _log.Warn($"Unable to send error response to the sender client ID {otherClient.Id.ToHex()}, maybe it is disconnected already, destroying the relay.");

          destroyRelay = true;
        }
      }
      else
      {
        // This should never happen unless the relay is destroyed already.
        _log.Debug($"Status is {_status} instead of Open, destroying relay if it is still active.");
        destroyRelay = _status != RelayConnectionStatus.Destroyed;
      }

      _lock.Release();

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
        await serverComponent.RelayList.DestroyNetworkRelay(this);
      }

      _log.Trace("(-)");
      return res;
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
      _log.Trace("(Client.Id:{0},IsRelayConnection:{1})", client.Id.ToHex(), isRelayConnection);

      IncomingClient clientToSendMessages = null;
      var messagesToSend = new List<IProtocolMessage<Message>>();
      IncomingClient clientToClose = null;

      await _lock.WaitAsync();

      bool isCallee = client == _callee;
      if (isRelayConnection)
        _log.Trace("Client ({0}) ID {1} disconnected, relay with status {2}.", isCallee ? "callee" : "caller", client.Id.ToHex(), _status);
      else
        _log.Trace("Client (customer) ID {0} disconnected, relay with status {1}.", client.Id.ToHex(), _status);

      bool destroyRelay = false;
      switch (_status)
      {
        case RelayConnectionStatus.WaitingForCalleeResponse:
          {
            if (isCallee)
            {
              // The client is callee in a relay that is being initialized. The caller is waiting for callee's response and the callee has just disconnected
              // from the profile server. This is situation 1) from the comment in ProcessMessageCallIdentityApplicationServiceRequestAsync.
              // We have to send ERROR_NOT_AVAILABLE to the caller and destroy the relay.
              _log.Trace("Callee disconnected from clCustomer port, message will be sent to the caller and relay destroyed.");
              clientToSendMessages = _caller;
              messagesToSend.Add(_caller.MessageBuilder.CreateErrorNotAvailableResponse(_pendingMessage));
              destroyRelay = true;
            }
            else
            {
              // The client is caller in a relay that is being initialized. The caller was waiting for callee's response, but the caller disconnected before 
              // the callee replied. The callee is now expected to reply and either accept or reject the call. If the call is rejected, everything is OK,
              // and we do not need to take any action. If the call is accepted, the callee will establish a new connection to clAppService port and will 
              // send us initial ApplicationServiceSendMessageRequest message. We will now destroy the relay so that the callee is disconnected 
              // as its token used in the initial message will not be found.
              _log.Trace("Caller disconnected from clCustomer port or clNonCustomer port, relay will be destroyed.");
              destroyRelay = true;
            }
            break;
          }

        case RelayConnectionStatus.WaitingForFirstInitMessage:
          {
            // In this relay status we do not care about connection to other than clAppService port.
            if (isRelayConnection)
            {
              // This should never happen because client's Relay is initialized only after 
              // its initialization message is received and that would upgrade the relay to WaitingForSecondInitMessage.
            }

            break;
          }

        case RelayConnectionStatus.WaitingForSecondInitMessage:
          {
            // In this relay status we do not care about connection to other than clAppService port.
            if (isRelayConnection)
            {
              // One of the clients has sent its initialization message to clAppService port 
              // and is waiting for the other client to do the same.
              bool isWaitingClient = (_callee == client) || (_caller == client);

              if (isWaitingClient)
              {
                // The client that disconnected was the waiting client. We destroy the relay. 
                // The other client is not connected yet or did not sent its initialization message yet.
                _log.Trace("First client on clAppService port closed its connection, destroying the relay.");
                destroyRelay = true;
              }
              else
              {
                // The client that disconnected was the client that the first client is waiting for.
                // We do not need to destroy the relay as the client may still connect again 
                // and send its initialization message on time.
                _log.Trace("Second client (that did not sent init message yet) on clAppService port closed its connection, no action taken.");
              }
            }

            break;
          }

        case RelayConnectionStatus.Open:
          {
            // In this relay status we do not care about connection to other than clAppService port.
            if (isRelayConnection)
            {
              // Both clients were connected. We disconnect the other client and destroy the relay.
              // However, there might be some unfinished ApplicationServiceSendMessageRequest requests 
              // that we have to send responses to.

              IncomingClient otherClient = isCallee ? _caller : _callee;
              _log.Trace($"{(isCallee ? "Callee" : "Caller")} disconnected, closing connection of {(isCallee ? "caller" : "callee")}.");
              clientToSendMessages = otherClient;
              clientToClose = otherClient;

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
                  var responseError = clientToSendMessages.MessageBuilder.CreateErrorNotFoundResponse(ctx.SenderRequest);
                  messagesToSend.Add(responseError);
                }
              }

              destroyRelay = true;
            }

            break;
          }

        case RelayConnectionStatus.Destroyed:
          // Nothing to be done.
          break;
      }

      _lock.Release();


      if (messagesToSend.Count > 0)
      {
        foreach (var messageToSend in messagesToSend)
        {
          if (!await clientToSendMessages.SendMessageAsync(messageToSend))
          {
            _log.Warn($"Unable to send message to the client ID {clientToSendMessages.Id.ToHex()}, maybe it is not connected anymore.");
            break;
          }
        }
      }


      if (clientToClose != null)
        await clientToClose.CloseConnectionAsync();


      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
        await serverComponent.RelayList.DestroyNetworkRelay(this);
      }

      _log.Trace("(-)");
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

      if (disposing)
      {
        lock (_disposingLock)
        {
          _status = RelayConnectionStatus.Destroyed;
          CancelTimeoutTimer();

          _disposed = true;
        }
      }
    }
  }
}
