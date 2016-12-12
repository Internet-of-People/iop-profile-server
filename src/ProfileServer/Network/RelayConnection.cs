using ProfileServer.Kernel;
using ProfileServer.Utils;
using ProfileServerCrypto;
using ProfileServerProtocol;
using Iop.Profileserver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    public RelayConnection Relay;

    /// <summary>ApplicationServiceSendMessageRequest from sender.</summary>
    public Message SenderRequest;

    /// <summary>
    /// Initializes the relay message context.
    /// </summary>
    public RelayMessageContext(RelayConnection Relay, Message SenderRequest)
    {
      this.Relay = Relay;
      this.SenderRequest = SenderRequest;
    }
  }


  /// <summary>
  /// Implementation of client to client network channel over the node bridge.
  /// </summary>
  public class RelayConnection : IDisposable
  {
    private PrefixLogger log;

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

    /// <summary>Lock object to protect access to relay.</summary>
    private SemaphoreSlim lockObject;

    /// <summary>Unique identifier of the relay.</summary>
    private Guid id;

    /// <summary>Status of the relay.</summary>
    private RelayConnectionStatus status;

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
    private IncomingClient caller;

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
    private IncomingClient callee;

    /// <summary>Unique token assigned to the caller.</summary>
    private Guid callerToken;

    /// <summary>Unique token assigned to the callee.</summary>
    private Guid calleeToken;

    /// <summary>Name of the application service of the callee that is being used for the communication.</summary>
    private string serviceName;



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
    private Timer timeoutTimer;


    /// <summary>
    /// If relay status is WaitingForCalleeResponse, this is the message that the caller sent to initiate the call.
    /// 
    /// <para>
    /// If relay status is WaitingForFirstInitMessage or WaitingForSecondInitMessage, this is the initialization message of the first client.
    /// </para>
    /// </summary>
    private Message pendingMessage;

    /// <summary>
    /// Creates a new relay connection from a caller to a callee using a specific application service.
    /// </summary>
    /// <param name="Caller">Network client of the caller.</param>
    /// <param name="Callee">Network client of the callee.</param>
    /// <param name="ServiceName">Name of the application service of the callee that is being used for the call.</param>
    /// <param name="RequestMessage">CallIdentityApplicationServiceRequest message that the caller send in order to initiate the call.</param>
    public RelayConnection(IncomingClient Caller, IncomingClient Callee, string ServiceName, Message RequestMessage)
    {
      lockObject = new SemaphoreSlim(1);
      id = Guid.NewGuid();
      string logPrefix = string.Format("[{0}:{1}] ", id, ServiceName);
      string logName = "ProfileServer.Network.ClientList";
      log = new PrefixLogger(logName, logPrefix);

      log.Trace("(Caller.Id:{0},Callee.Id:{1},ServiceName:'{2}')", Caller.Id.ToHex(), Callee.Id.ToHex(), ServiceName);
      serviceName = ServiceName;
      caller = Caller;
      callee = Callee;
      pendingMessage = RequestMessage;

      callerToken = Guid.NewGuid();
      calleeToken = Guid.NewGuid();
      log.Trace("Caller token is '{0}'.", callerToken);
      log.Trace("Callee token is '{0}'.", calleeToken);

      status = RelayConnectionStatus.WaitingForCalleeResponse;

      // Relay is created by caller's request, it will expire if the callee does not reply within reasonable time.
      timeoutTimer = new Timer(TimeoutCallback, status, CalleeResponseCallNotificationDelayMaxSeconds * 1000, Timeout.Infinite);

      log.Trace("(-)");
    }

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
    /// Then if relay status is WaitingForSecondInitMessage, the node receives a message from the first client 
    /// on clAppService port, it starts the timer again, which now expires if the second client does not connect 
    /// and send its initial message within a reasonable time. 
    /// </para>
    /// </summary>
    /// <param name="state">Status of the relay when the timer was installed.</param>
    private async void TimeoutCallback(object State)
    {
      LogDiagnosticContext.Start();

      RelayConnectionStatus previousStatus = (RelayConnectionStatus)State;
      log.Trace("(State:{0})", previousStatus);

      IncomingClient clientToSendMessage = null;
      Message messageToSend = null;
      bool destroyRelay = false;

      await lockObject.WaitAsync();

      if (timeoutTimer != null)
      {
        switch (status)
        {
          case RelayConnectionStatus.WaitingForCalleeResponse:
            {
              // The caller requested the call and the callee was notified.
              // The callee failed to send us response on time, this is situation 2)
              // from ProcessMessageCallIdentityApplicationServiceRequestAsync.
              // We send ERROR_NOT_AVAILABLE to the caller and destroy the relay.
              log.Debug("Callee failed to reply to the incoming call notification, closing relay.");

              clientToSendMessage = caller;
              messageToSend = caller.MessageBuilder.CreateErrorNotAvailableResponse(pendingMessage);
              break;
            }

          case RelayConnectionStatus.WaitingForFirstInitMessage:
            {
              // Neither client joined the channel on time, nothing to do, just destroy the relay.
              log.Debug("None of the clients joined the relay on time, closing relay.");
              break;
            }

          case RelayConnectionStatus.WaitingForSecondInitMessage:
            {
              // One client is waiting for the other one to join, but the other client failed to join on time.
              // We send ERROR_NOT_FOUND to the waiting client and close its connection.
              log.Debug("{0} failed to join the relay on time, closing relay.", callee != null ? "Caller" : "Callee");

              clientToSendMessage = callee != null ? callee : caller;
              messageToSend = clientToSendMessage.MessageBuilder.CreateErrorNotFoundResponse(pendingMessage);
              break;
            }

          default:
            log.Debug("Time out triggered while the relay status was {0}.", status);
            break;
        }

        // In case of any timeouts, we just destroy the relay.
        destroyRelay = true;
      }
      else log.Debug("Timeout timer of relay '{0}' has been destroyed, no action taken.", id);

      lockObject.Release();


      if (messageToSend != null)
      {
        if (!await clientToSendMessage.SendMessageAsync(messageToSend))
          log.Warn("Unable to send message to the client ID {0} in relay '{1}', maybe it is not connected anymore.", clientToSendMessage.Id.ToHex(), id);
      }

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
        IncomingClientList clientList = serverComponent.GetClientList();
        await clientList.DestroyNetworkRelay(this);
      }

      log.Trace("(-)");

      LogDiagnosticContext.Stop();
    }


    /// <summary>
    /// Obtains relay status.
    /// </summary>
    /// <returns>Relay status.</returns>
    public RelayConnectionStatus GetStatus()
    {
      return status;
    }

    /// <summary>
    /// Obtains relay identifier.
    /// </summary>
    /// <returns>Relay identifier.</returns>
    public Guid GetId()
    {
      return id;
    }


    /// <summary>
    /// Obtains relay callee's token.
    /// </summary>
    /// <returns>Relay callee's token.</returns>
    public Guid GetCalleeToken()
    {
      return calleeToken;
    }

    /// <summary>
    /// Obtains relay caller's token.
    /// </summary>
    /// <returns>Relay caller's token.</returns>
    public Guid GetCallerToken()
    {
      return callerToken;
    }

    

    /// <summary>
    /// Cancels timeoutTimer.
    /// </summary>
    private void CancelTimeoutTimer()
    {
      log.Trace("()");

      lockObject.Wait();

      CancelTimeoutTimerLocked();

      lockObject.Release();

      log.Trace("(-)");
    }

    /// <summary>
    /// Cancels timeoutTimer. Relay's lockObject has to be acquired.
    /// </summary>
    private void CancelTimeoutTimerLocked()
    {
      log.Trace("()");

      if (timeoutTimer != null) timeoutTimer.Dispose();
      timeoutTimer = null;

      log.Trace("(-)");
    }


    /// <summary>
    /// Handles situation when the callee replied to the incoming call notification request.
    /// </summary>
    /// <param name="ResponseMessage">Full response message from the callee.</param>
    /// <param name="Request">Unfinished call request message of the caller that corresponds to the response message.</param>
    /// <returns></returns>
    public async Task<bool> CalleeRepliedToIncomingCallNotification(Message ResponseMessage, UnfinishedRequest Request)
    {
      log.Trace("()");

      bool res = false;

      bool destroyRelay = false;
      IncomingClient clientToSendMessage = null;
      Message messageToSend = null;

      await lockObject.WaitAsync();


      if (status == RelayConnectionStatus.WaitingForCalleeResponse)
      {
        CancelTimeoutTimerLocked();

        // The caller is still connected and waiting for an answer to its call request.
        if (ResponseMessage.Response.Status == Status.Ok)
        {
          // The callee is now expected to connect to clAppService with its token.
          // We need to inform caller that the callee accepted the call.
          // This is option 4) from ProcessMessageCallIdentityApplicationServiceRequestAsync.
          messageToSend = caller.MessageBuilder.CreateCallIdentityApplicationServiceResponse(pendingMessage, callerToken.ToByteArray());
          clientToSendMessage = caller;
          pendingMessage = null;

          caller = null;
          callee = null;
          status = RelayConnectionStatus.WaitingForFirstInitMessage;
          log.Debug("Relay '{0}' status has been changed to {1}.", id, status);

          /// Install timeoutTimer to expire if the first client does not connect to clAppService port 
          /// and send its initialization message within reasonable time.
          timeoutTimer = new Timer(TimeoutCallback, RelayConnectionStatus.WaitingForFirstInitMessage, FirstAppServiceInitializationMessageDelayMaxSeconds * 1000, Timeout.Infinite);

          res = true;
        }
        else
        {
          // The callee rejected the call or reported other error.
          // These are options 3) and 2) from ProcessMessageCallIdentityApplicationServiceRequestAsync.
          if (ResponseMessage.Response.Status == Status.ErrorRejected)
          {
            log.Debug("Callee ID '{0}' rejected the call from caller identity ID '{1}', relay '{2}'.", callee.Id.ToHex(), caller.Id.ToHex(), id);
            messageToSend = caller.MessageBuilder.CreateErrorRejectedResponse(pendingMessage);
          }
          else
          {
            log.Warn("Callee ID '0} sent error response '{1}' for call request from caller identity ID {2}, relay '{3}'.", 
              callee.Id.ToHex(), ResponseMessage.Response.Status, caller.Id.ToHex(), id);

            messageToSend = caller.MessageBuilder.CreateErrorNotAvailableResponse(pendingMessage);
          }

          clientToSendMessage = caller;
          destroyRelay = true;
        }
      }
      else
      {
        // The relay has probably been destroyed, or something bad happened to it.
        // We take no action here regardless of what the callee's response is.
        // If it rejected the call, there is nothing to be done since we do not have 
        // any connection to the caller anymore.
        log.Debug("Relay status is {0}, nothing to be done.", status);
      }
      
      lockObject.Release();


      if (messageToSend != null)
      {
        if (await clientToSendMessage.SendMessageAsync(messageToSend)) log.Debug("Response to call initiation request sent to the caller ID {0}.", clientToSendMessage.Id.ToHex());
        else log.Debug("Unable to reponse to call initiation request to the caller ID {0}.", clientToSendMessage.Id.ToHex());
      }

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
        IncomingClientList clientList = serverComponent.GetClientList();
        await clientList.DestroyNetworkRelay(this);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes ApplicationServiceSendMessageRequest message from a client.
    /// <para>
    /// Relay received message from one client and sends it to the other one. If this is the first request 
    /// a client sends after it connects to clAppService port, the request's message is ignored and the reply is sent 
    /// to the client as the other client is confirmed to join the relay.</para>
    /// </summary>
    /// <param name="Client">Client that sent the message.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <param name="Token">Sender's relay token.</param>
    /// <returns>Response message to be sent to the client.</returns>
    public async Task<Message> ProcessIncomingMessage(IncomingClient Client, Message RequestMessage, Guid Token)
    {
      log.Trace("()");

      Message res = null;
      bool destroyRelay = false;

      await lockObject.WaitAsync();

      bool isCaller = callerToken.Equals(Token);

      IncomingClient otherClient = isCaller ? callee : caller;
      log.Trace("Received message over relay '{0}' in status {1} with client ID {2} being {3} and the other client ID {4} is {5}.",
        id, status, Client.Id.ToHex(), isCaller ? "caller" : "callee", otherClient != null ? otherClient.Id.ToHex() : "N/A", isCaller ? "callee" : "caller");

      switch (status)
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
              lockObject.Release();

              bool statusChanged = false;
              log.Warn("Callee sent initialization message before we received IncomingCallNotificationResponse. We will wait to see if it arrives soon.");
              for (int i = 0; i < 5; i++)
              {
                log.Warn("Attempt #{0}, waiting 1 second.", i + 1);
                await Task.Delay(1000);

                await lockObject.WaitAsync();

                log.Warn("Attempt #{0}, checking relay status.", i + 1);
                if (status != RelayConnectionStatus.WaitingForCalleeResponse)
                {
                  log.Warn("Attempt #{0}, relay status changed to {1}.", i + 1, status);
                  statusChanged = true;
                }

                lockObject.Release();

                if (statusChanged) break;
              }

              await lockObject.WaitAsync();
              if (statusChanged)
              {
                // Status of relay has change, which means either it has been destroyed already, or the IncomingCallNotificationResponse 
                // message we were waiting for arrived. In any case, we call this method recursively, but it can not happen that we would end up here again.
                lockObject.Release();

                log.Trace("Calling ProcessIncomingMessage recursively.");
                res = await ProcessIncomingMessage(Client, RequestMessage, Token);

                await lockObject.WaitAsync();
              }
              else
              {
                log.Trace("Message received from caller and relay status is WaitingForCalleeResponse and IncomingCallNotificationResponse did not arrive, closing connection to client, destroying relay.");
                res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
                Client.ForceDisconnect = true;
                destroyRelay = true;
              }
            }
            else
            {
              log.Trace("Message received from caller and relay status is WaitingForCalleeResponse, closing connection to client, destroying relay.");
              res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
              Client.ForceDisconnect = true;
              destroyRelay = true;
            }
            break;
          }

        case RelayConnectionStatus.WaitingForFirstInitMessage:
          {
            log.Debug("Received an initialization message from the first client ID '{0}' on relay '{1}', waiting for the second client.", Client.Id.ToHex(), id);
            CancelTimeoutTimerLocked();

            if (Client.Relay == null)
            {
              Client.Relay = this;

              // Other peer is not connected yet, so we put this request on hold and wait for the other client.
              if (isCaller) caller = Client;
              else callee = Client;

              status = RelayConnectionStatus.WaitingForSecondInitMessage;
              log.Trace("Relay '{0}' status changed to {1}.", id, status);

              pendingMessage = RequestMessage;
              timeoutTimer = new Timer(TimeoutCallback, status, SecondAppServiceInitializationMessageDelayMaxSeconds * 1000, Timeout.Infinite);

              // res remains null, which is OK as the request is put on hold until the other client joins the channel.
            }
            else
            {
              // Client already sent us the initialization message, this is protocol violation error, destroy the relay.
              // Since the relay should be upgraded to WaitingForSecondInitMessage status, this can happen 
              // only if a client does not use a separate connection for each clAppService session, which is forbidden.
              log.Debug("Client ID {0} on relay '{1}' probably uses a single connection for two relays. Both relays will be destroyed.", Client.Id.ToHex(), id);
              res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
              destroyRelay = true;
            }
            break;
          }

        case RelayConnectionStatus.WaitingForSecondInitMessage:
          {
            log.Debug("Received an initialization message from the second client on relay '{0}'.", id);
            CancelTimeoutTimerLocked();

            if (Client.Relay == null)
            {
              Client.Relay = this;

              // Other peer is connected already, so we just inform it by sending response to its initial ApplicationServiceSendMessageRequest.
              if (isCaller) caller = Client;
              else callee = Client;

              status = RelayConnectionStatus.Open;
              log.Trace("Relay '{0}' status changed to {1}.", id, status);

              Message otherClientResponse = otherClient.MessageBuilder.CreateApplicationServiceSendMessageResponse(pendingMessage);
              pendingMessage = null;
              if (await otherClient.SendMessageAsync(otherClientResponse))
              {
                // And we also send reply to the second client that the channel is now ready for communication.
                res = Client.MessageBuilder.CreateApplicationServiceSendMessageResponse(RequestMessage);
              }
              else
              {
                log.Warn("Unable to send message to other client ID {0}, closing connection to client and destroying the relay.", otherClient.Id.ToHex());
                res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
                Client.ForceDisconnect = true;
                destroyRelay = true;
              }
            }
            else
            {
              // Client already sent us the initialization message, this is error, destroy the relay.
              log.Debug("Client ID {0} on relay '{1}' sent a message before receiving a reply to its initialization message. Relay will be destroyed.", Client.Id.ToHex(), id);
              res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
              destroyRelay = true;
            }

            break;
          }


        case RelayConnectionStatus.Open:
          {
            if (Client.Relay == this)
            {
              // Relay is open, this means that all incoming messages are sent to the other client.
              byte[] messageForOtherClient = RequestMessage.Request.SingleRequest.ApplicationServiceSendMessage.Message.ToByteArray();
              Message otherClientMessage = otherClient.MessageBuilder.CreateApplicationServiceReceiveMessageNotificationRequest(messageForOtherClient);
              RelayMessageContext context = new RelayMessageContext(this, RequestMessage);
              if (await otherClient.SendMessageAndSaveUnfinishedRequestAsync(otherClientMessage, context))
              {
                // res is null, which is fine, the sender is put on hold and we will get back to it once the recipient confirms that it received the message.
                log.Debug("Message from client ID {0} has been relayed to other client ID {1}.", Client.Id.ToHex(), otherClient.Id.ToHex());
              }
              else
              {
                log.Warn("Unable to relay message to other client ID {0}, closing connection to client and destroying the relay.", otherClient.Id.ToHex());
                res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
                Client.ForceDisconnect = true;
                destroyRelay = true;
              }
            }
            else
            {
              // This means that the client used a single clAppService port connection for two different relays, which is forbidden.
              log.Warn("Client ID {0} mixed relay '{1}' with relay '{2}', closing connection to client and destroying both relays.", otherClient.Id.ToHex(), Client.Relay.id, id);
              res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
              Client.ForceDisconnect = true;
              destroyRelay = true;
            }

            break;
          }

        case RelayConnectionStatus.Destroyed:
          {
            log.Trace("Relay has been destroyed, closing connection to client.");
            res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
            Client.ForceDisconnect = true;
            break;
          }
          
        default:
          log.Trace("Relay status is '{0}', closing connection to client, destroying relay.", status);
          res = Client.MessageBuilder.CreateErrorNotFoundResponse(RequestMessage);
          Client.ForceDisconnect = true;
          destroyRelay = true;
          break;
      }      

      lockObject.Release();

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
        IncomingClientList clientList = serverComponent.GetClientList();
        await clientList.DestroyNetworkRelay(this);
        if ((this != Client.Relay) && (Client.Relay != null))
          await clientList.DestroyNetworkRelay(Client.Relay);
      }

      log.Trace("(-)");
      return res;
    }


    /// <summary>
    /// Processes incoming confirmation from the message recipient over the relay.
    /// </summary>
    /// <param name="Client">Client that sent the response.</param>
    /// <param name="ResponseMessage">Full response message.</param>
    /// <param name="SenderRequest">Sender request message that the recipient confirmed.</param>
    /// <returns>true if the connection to the client that sent the response should remain open, false if the client should be disconnected.</returns>
    public async Task<bool> RecipientConfirmedMessage(IncomingClient Client, Message ResponseMessage, Message SenderRequest)
    {
      log.Trace("()");

      bool res = false;
      bool destroyRelay = false;

      await lockObject.WaitAsync();

      if (status == RelayConnectionStatus.Open)
      {
        bool isCaller = Client == caller;

        IncomingClient otherClient = isCaller ? callee : caller;
        log.Trace("Over relay '{0}', received confirmation (status code {1}) from client ID {2} of a message sent by client ID {3}.",
          id, ResponseMessage.Response.Status, Client.Id.ToHex(), otherClient.Id.ToHex());

        if (ResponseMessage.Response.Status == Status.Ok)
        {
          // We have received a confirmation from the recipient, so we just complete the sender's request to inform it that the message was delivered.
          Message otherClientResponse = otherClient.MessageBuilder.CreateApplicationServiceSendMessageResponse(SenderRequest);
          if (await otherClient.SendMessageAsync(otherClientResponse))
          {
            res = true;
          }
          else
          {
            log.Warn("Unable to send message to other client ID '0x{0:X16}' on relay '{1}', closing connection to client and destroying the relay.", id, otherClient.Id);
            destroyRelay = true;
          }
        }
        else
        {
          // We have received error from the recipient, so we forward it to the sender and destroy the relay.
          Message errorResponse = otherClient.MessageBuilder.CreateErrorNotFoundResponse(SenderRequest);

          if (!await otherClient.SendMessageAsync(errorResponse))
            log.Warn("In relay '{0}', unable to send error response to the sender client ID {1}, maybe it is disconnected already, destroying the relay.", id, otherClient.Id.ToHex());

          destroyRelay = true;
        }
      }
      else
      {
        // This should never happen unless the relay is destroyed already.
        log.Debug("Relay '{0}' status is {1} instead of Open, destroying relay if it is still active.", id, status);
        destroyRelay = status != RelayConnectionStatus.Destroyed;
      }

      lockObject.Release();

      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
        IncomingClientList clientList = serverComponent.GetClientList();
        await clientList.DestroyNetworkRelay(this);
      }

      log.Trace("(-)");
      return res;
    }


    /// <summary>
    /// Handles situation when a client connected to a relay disconnected. 
    /// However, the closed connection might be either connection to clCustomer/clNonCustomer port, 
    /// or it might be connection to clAppService port.
    /// </summary>
    /// <param name="Client">Client that disconnected.</param>
    /// <param name="IsRelayConnection">true if the closed connection was to clAppService port, false otherwise.</param>
    public async Task HandleDisconnectedClient(IncomingClient Client, bool IsRelayConnection)
    {
      log.Trace("(Client.Id:{0},IsRelayConnection:{1})", Client.Id.ToHex(), IsRelayConnection);

      IncomingClient clientToSendMessages = null;
      List<Message> messagesToSend = new List<Message>();
      IncomingClient clientToClose = null;

      await lockObject.WaitAsync();

      bool isCallee = Client == callee;
      if (IsRelayConnection) log.Trace("Client ({0}) ID {1} disconnected, relay '{2}' status {3}.", isCallee ? "callee" : "caller", Client.Id.ToHex(), id, status);
      else log.Trace("Client (customer) ID {0} disconnected, relay '{1}' status {2}.", Client.Id.ToHex(), id, status);

      bool destroyRelay = false;
      switch (status)
      {
        case RelayConnectionStatus.WaitingForCalleeResponse:
          {
            if (isCallee)
            {
              // The client is callee in a relay that is being initialized. The caller is waiting for callee's response and the callee has just disconnected
              // from the node. This is situation 1) from the comment in ProcessMessageCallIdentityApplicationServiceRequestAsync.
              // We have to send ERROR_NOT_AVAILABLE to the caller and destroy the relay.
              log.Trace("Callee disconnected from clCustomer port of relay '{0}', message will be sent to the caller and relay destroyed.", id);
              clientToSendMessages = caller;
              messagesToSend.Add(caller.MessageBuilder.CreateErrorNotAvailableResponse(pendingMessage));
              destroyRelay = true;
            }
            else
            {
              // The client is caller in a relay that is being initialized. The caller was waiting for callee's response, but the caller disconnected before 
              // the callee replied. The callee is now expected to reply and either accept or reject the call. If the call is rejected, everything is OK,
              // and we do not need to take any action. If the call is accepted, the callee will establish a new connection to clAppService port and will 
              // send us initial ApplicationServiceSendMessageRequest message. We will now destroy the relay so that the callee is disconnected 
              // as its token used in the initial message will not be found.
              log.Trace("Caller disconnected from clCustomer port or clNonCustomer port of relay '{0}', relay will be destroyed.", id);
              destroyRelay = true;
            }
            break;
          }

        case RelayConnectionStatus.WaitingForFirstInitMessage:
          {
            // In this relay status we do not care about connection to other than clAppService port.
            if (IsRelayConnection)
            {
              // This should never happen because client's Relay is initialized only after 
              // its initialization message is received and that would upgrade the relay to WaitingForSecondInitMessage.
            }

            break;
          }

        case RelayConnectionStatus.WaitingForSecondInitMessage:
          {
            // In this relay status we do not care about connection to other than clAppService port.
            if (IsRelayConnection)
            {
              // One of the clients has sent its initialization message to clAppService port 
              // and is waiting for the other client to do the same.
              bool isWaitingClient = (callee == Client) || (caller == Client);

              if (isWaitingClient)
              {
                // The client that disconnected was the waiting client. We destroy the relay. 
                // The other client is not connected yet or did not sent its initialization message yet.
                log.Trace("First client on clAppService port of relay '{0}' closed its connection, destroying the relay.", id);
                destroyRelay = true;
              }
              else
              {
                // The client that disconnected was the client that the first client is waiting for.
                // We do not need to destroy the relay as the client may still connect again 
                // and send its initialization message on time.
                log.Trace("Second client (that did not sent init message yet) on clAppService port of relay '{0}' closed its connection, no action taken.", id);
              }
            }

            break;
          }

        case RelayConnectionStatus.Open:
          {
            // In this relay status we do not care about connection to other than clAppService port.
            if (IsRelayConnection)
            {
              // Both clients were connected. We disconnect the other client and destroy the relay.
              // However, there might be some unfinished ApplicationServiceSendMessageRequest requests 
              // that we have to send responses to.

              IncomingClient otherClient = isCallee ? caller : callee;
              log.Trace("{0} disconnected from relay '{1}', closing connection of {2}.", isCallee ? "Callee" : "Caller", id, isCallee ? "caller" : "callee");
              clientToSendMessages = otherClient;
              clientToClose = otherClient;

              // Find all unfinished requests from this relay.
              // When a client sends ApplicationServiceSendMessageRequest, the node creates ApplicationServiceReceiveMessageNotificationRequest 
              // and adds it as an unfinished request with context set to RelayMessageContext, which contains the sender's ApplicationServiceSendMessageRequest.
              // This unfinished message is in the list of unfinished message of the recipient.
              List<UnfinishedRequest> unfinishedRelayRequests = Client.GetAndRemoveUnfinishedRequests();
              foreach (UnfinishedRequest unfinishedRequest in unfinishedRelayRequests)
              {
                // Find ApplicationServiceReceiveMessageNotificationRequest request messages sent to the client who closed the connection.
                if ((unfinishedRequest.RequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request)
                  && (unfinishedRequest.RequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
                  && (unfinishedRequest.RequestMessage.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification))
                {
                  // This unfinished request's context holds ApplicationServiceSendMessageRequest message of the client that is still connected.
                  RelayMessageContext ctx = (RelayMessageContext)unfinishedRequest.Context;
                  Message responseError = clientToSendMessages.MessageBuilder.CreateErrorNotFoundResponse(ctx.SenderRequest);
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

      lockObject.Release();


      if (messagesToSend.Count > 0)
      {
        foreach (Message messageToSend in messagesToSend)
        {
          if (!await clientToSendMessages.SendMessageAsync(messageToSend))
          {
            log.Warn("Unable to send message to the client ID {0}, relay '{1}', maybe it is not connected anymore.", clientToSendMessages.Id.ToHex(), id);
            break;
          }
        }
      }


      if (clientToClose != null)
        await clientToClose.CloseConnectionAsync();


      if (destroyRelay)
      {
        Server serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
        IncomingClientList clientList = serverComponent.GetClientList();
        await clientList.DestroyNetworkRelay(this);
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Checks if the the relay has been destroyed already and changes its status to Destroyed.
    /// </summary>
    /// <returns>true if the relay has been destroyed already, false otherwise.</returns>
    public async Task<bool> TestAndSetDestroyed()
    {
      log.Trace("()");

      bool res = false;

      await lockObject.WaitAsync();

      res = status == RelayConnectionStatus.Destroyed;
      status = RelayConnectionStatus.Destroyed;

      lockObject.Release();

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class.
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected virtual void Dispose(bool Disposing)
    {
      if (disposed) return;

      if (Disposing)
      {
        lock (disposingLock)
        {
          status = RelayConnectionStatus.Destroyed;
          CancelTimeoutTimer();

          disposed = true;
        }
      }
    }
  }
}
