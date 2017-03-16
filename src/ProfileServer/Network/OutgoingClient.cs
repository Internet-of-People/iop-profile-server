using Iop.Profileserver;
using ProfileServer.Data.Models;
using ProfileServer.Kernel;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using IopCommon;
using IopServerCore.Network;
using Google.Protobuf;

namespace ProfileServer.Network
{
  /// <summary>
  /// Outgoing client class represents a TCP client the profile server, in the role of a client, uses to connect to other profile servers.
  /// </summary>
  public class OutgoingClient : ClientBase, IDisposable
  {
    /// <summary>Special value for LastResponseDetails to indicate connection failure.</summary>
    public const string LastResponseDetailsConnectionFailed = "<ConnectionFailed>";

    /// <summary>Server's public key received when starting conversation.</summary>
    private byte[] serverKey;
    /// <summary>Server's public key received when starting conversation.</summary>
    public byte[] ServerKey { get { return serverKey; } }

    /// <summary>Server's network ID.</summary>
    private byte[] serverId;
    /// <summary>Server's network ID.</summary>
    public byte[] ServerId { get { return serverId; } }

    /// <summary>Challenge that the server sent to the client when starting conversation.</summary>
    private byte[] serverChallenge;

    /// <summary>Challenge that the client sent to the server when starting conversation.</summary>
    private byte[] clientChallenge;

    /// <summary>Cancellation token of the parent component.</summary>
    private CancellationToken shutdownCancellationToken;


    /// <summary>Status of the last response the client received.</summary>
    private Status lastResponseStatus;
    /// <summary>Status of the last response the client received.</summary>
    public Status LastResponseStatus { get { return lastResponseStatus; } }

    /// <summary>Error details from the last response the client received.</summary>
    private string  lastResponseDetails;
    /// <summary>Error details from the last response the client received.</summary>
    public string LastResponseDetails { get { return lastResponseDetails; } }


    /// <summary>Protocol message builder.</summary>
    private PsMessageBuilder messageBuilder;
    /// <summary>Protocol message builder.</summary>
    public PsMessageBuilder MessageBuilder { get { return messageBuilder; } }


    /// <summary>User defined context data.</summary>
    public object Context;


    /// <summary>
    /// Creates the instance for a new outgoing TCP client.
    /// </summary>
    /// <param name="RemoteEndPoint">Target IP address and port this client will be connected to.</param>
    /// <param name="UseTls">true if TLS should be used for this TCP client, false otherwise.</param>
    /// <param name="ShutdownCancellationToken">Cancellation token of the parent component.</param>
    public OutgoingClient(IPEndPoint RemoteEndPoint, bool UseTls, CancellationToken ShutdownCancellationToken):
      base(RemoteEndPoint, UseTls)
    {
      string logPrefix = string.Format("[=>{0}] ", RemoteEndPoint);
      log = new Logger("ProfileServer.Network.OutgoingClient", logPrefix);

      log.Trace("()");

      messageBuilder = new PsMessageBuilder(0, new List<SemVer>() { SemVer.V100 }, Config.Configuration.Keys);
      shutdownCancellationToken = ShutdownCancellationToken;

      log.Trace("(-)");
    }



    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public override IProtocolMessage CreateMessageFromRawData(byte[] Data)
    {
      return PsMessageBuilder.CreateMessageFromRawData(Data);
    }


    /// <summary>
    /// Converts an IoP Profile Server Network protocol message to a binary format.
    /// </summary>
    /// <param name="Data">IoP Profile Server Network protocol message.</param>
    /// <returns>Binary representation of the message to be sent over the network.</returns>
    public override byte[] MessageToByteArray(IProtocolMessage Data)
    {
      return PsMessageBuilder.MessageToByteArray(Data);
    }


    /// <summary>
    /// Reads and parses protocol message from the network stream.
    /// </summary>
    /// <returns>Parsed protocol message or null if the function fails.</returns>
    public async Task<PsProtocolMessage> ReceiveMessageAsync()
    {
      log.Trace("()");

      PsProtocolMessage res = null;

      using (CancellationTokenSource readTimeoutTokenSource = new CancellationTokenSource(60000),
             timeoutShutdownTokenSource = CancellationTokenSource.CreateLinkedTokenSource(readTimeoutTokenSource.Token, shutdownCancellationToken))
      {
        RawMessageReader messageReader = new RawMessageReader(Stream);
        RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(timeoutShutdownTokenSource.Token);
        if (rawMessage.Data != null)
        {
          res = (PsProtocolMessage)CreateMessageFromRawData(rawMessage.Data);
          if (res.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          {
            lastResponseStatus = res.Response.Status;
            lastResponseDetails = res.Response.Details;
          }
        }
      }

      ForceDisconnect = res == null;

      log.Trace("(-):ForceDisconnect={0}", ForceDisconnect, res != null ? "Message" : "null");
      return res;
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public override async Task<bool> SendMessageAsync(IProtocolMessage Message)
    {
      log.Trace("()");

      lastResponseDetails = null;
      lastResponseStatus = Status.Ok;
      bool res = await base.SendMessageAsync(Message);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Generates client's challenge and creates start conversation request with it.
    /// </summary>
    /// <returns>StartConversationRequest message that is ready to be sent to the server.</returns>
    public PsProtocolMessage CreateStartConversationRequest()
    {
      clientChallenge = new byte[PsMessageBuilder.ChallengeDataSize];
      Crypto.Rng.GetBytes(clientChallenge);
      PsProtocolMessage res = MessageBuilder.CreateStartConversationRequest(clientChallenge);
      return res;
    }


    /// <summary>
    /// Checks whether the incoming response is a reponse to a specific request and if the reported status is OK.
    /// <para>This function sets last error</para>
    /// </summary>
    /// <param name="RequestMessage">Request message for which the response was received.</param>
    /// <param name="ResponseMessage">Response message received.</param>
    /// <returns>true if the response is valid and its status is OK.</returns>
    public bool CheckResponseMessage(PsProtocolMessage RequestMessage, PsProtocolMessage ResponseMessage)
    {
      log.Trace("()");

      bool res = false;

      if (ResponseMessage != null)
      {
        if (ResponseMessage.Id == RequestMessage.Id)
        {
          if (ResponseMessage.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          {
            Response response = ResponseMessage.Response;
            if (response.Status == Status.Ok)
            {
              switch (response.ConversationTypeCase)
              {
                case Response.ConversationTypeOneofCase.SingleResponse:
                  if (RequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
                  {
                    SingleRequest.RequestTypeOneofCase requestType = RequestMessage.Request.SingleRequest.RequestTypeCase;
                    if (response.SingleResponse.ResponseTypeCase.ToString() == requestType.ToString())
                    {
                      res = true;
                    }
                    else log.Debug("Single response type {0} does not match single request type {1}.", response.SingleResponse.ResponseTypeCase, requestType);
                  }
                  else log.Debug("Response message conversation type {0} does not match request message conversation type {1}.", response.ConversationTypeCase, RequestMessage.Request.ConversationTypeCase);
                  break;

                case Response.ConversationTypeOneofCase.ConversationResponse:
                  if (RequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
                  {
                    ConversationRequest.RequestTypeOneofCase requestType = RequestMessage.Request.ConversationRequest.RequestTypeCase;
                    if (response.ConversationResponse.ResponseTypeCase.ToString() == requestType.ToString())
                    {
                      res = true;
                    }
                    else log.Debug("Conversation response type {0} does not match conversation request type {1}.", response.ConversationResponse.ResponseTypeCase, requestType);
                  }
                  else log.Debug("Response message conversation type {0} does not match request message conversation type {1}.", response.ConversationTypeCase, RequestMessage.Request.ConversationTypeCase);
                  break;

                default:
                  log.Error("Invalid response conversation type {0}.", ResponseMessage.Response.ConversationTypeCase);
                  break;
              }
            }
            else log.Debug("Response message status is {0}.", ResponseMessage.Response.Status);
          }
          else log.Debug("Received message is not response, its message type is {0}.", ResponseMessage.MessageTypeCase);
        }
        else log.Debug("Response message ID {0} does not match request message ID {1}.", ResponseMessage.Id, RequestMessage.Id);
      }
      else log.Debug("Response message is null.");

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Starts conversation with the server the client is connected to and checks whether the server response contains expected values.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> StartConversationAsync()
    {
      log.Trace("()");

      bool res = false;

      PsProtocolMessage requestMessage = CreateStartConversationRequest();
      if (await SendMessageAsync(requestMessage))
      {
        PsProtocolMessage responseMessage = await ReceiveMessageAsync();
        if (CheckResponseMessage(requestMessage, responseMessage))
        {
          try
          {
            SemVer receivedVersion = new SemVer(responseMessage.Response.ConversationResponse.Start.Version);
            bool versionOk = receivedVersion.Equals(new SemVer(MessageBuilder.Version));

            bool pubKeyLenOk = responseMessage.Response.ConversationResponse.Start.PublicKey.Length == IdentityBase.IdentifierLength;
            bool challengeOk = responseMessage.Response.ConversationResponse.Start.Challenge.Length == PsMessageBuilder.ChallengeDataSize;

            serverKey = responseMessage.Response.ConversationResponse.Start.PublicKey.ToByteArray();
            serverId = Crypto.Sha256(serverKey);
            serverChallenge = responseMessage.Response.ConversationResponse.Start.Challenge.ToByteArray();
            bool challengeVerifyOk = VerifyServerChallengeSignature(responseMessage);

            res = versionOk && pubKeyLenOk && challengeOk && challengeVerifyOk;
          }
          catch
          {
            log.Warn("Received unexpected or invalid message.");
          }
        }
        else log.Warn("Received unexpected or invalid message.");
      }
      else log.Warn("Unable to send message.");

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs an identity verification process for the client's identity using the already opened connection to the server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> VerifyIdentityAsync()
    {
      log.Trace("()");

      bool res = false;
      if (await StartConversationAsync())
      {
        PsProtocolMessage requestMessage = MessageBuilder.CreateVerifyIdentityRequest(serverChallenge);
        if (await SendMessageAsync(requestMessage))
        {
          PsProtocolMessage responseMessage = await ReceiveMessageAsync();
          if (CheckResponseMessage(requestMessage, responseMessage))
          {
            res = true;
          }
          else log.Warn("Received unexpected or invalid message.");
        }
        else log.Warn("Unable to send message.");
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Connects to the target server and performs an identity verification process.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ConnectAndVerifyIdentityAsync()
    {
      log.Trace("()");

      bool res = false;
      if (await ConnectAsync())
      {
        res = await VerifyIdentityAsync();
      }
      else
      {
        lastResponseDetails = LastResponseDetailsConnectionFailed;
        log.Debug("Unable to connect to {0}, setting lastResponseDetails to '{1}'.", RemoteEndPoint, lastResponseDetails);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Verifies whether the server successfully signed the correct start conversation challenge.
    /// </summary>
    /// <param name="StartConversationResponse">StartConversationResponse received from the server.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool VerifyServerChallengeSignature(PsProtocolMessage StartConversationResponse)
    {
      log.Trace("()");

      byte[] receivedChallenge = StartConversationResponse.Response.ConversationResponse.Start.ClientChallenge.ToByteArray();
      bool res = StructuralEqualityComparer<byte[]>.Default.Equals(receivedChallenge, clientChallenge)
        && MessageBuilder.VerifySignedConversationResponseBodyPart(StartConversationResponse, receivedChallenge, serverKey);

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Checks whether the verified server ID matches the expected identifier.
    /// </summary>
    /// <param name="Id">Network identifier that the caller expects.</param>
    /// <returns>true if the server ID matches the expected identifier, false otherwise.</returns>
    public bool MatchServerId(byte[] Id)
    {
      log.Trace("(Id:'{0}',serverId:'{1}')", Id.ToHex(), serverId.ToHex());

      bool res = StructuralEqualityComparer<byte[]>.Default.Equals(Id, serverId);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends StartNeighborhoodInitializationRequest to the server and reads a response.
    /// </summary>
    /// <param name="PrimaryPort">Primary interface port of the requesting profile server.</param>
    /// <param name="SrNeighborPort">Neighbors interface port of the requesting profile server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> StartNeighborhoodInitializationAsync(uint PrimaryPort, uint SrNeighborPort)
    {
      log.Trace("()");

      bool res = false;
      PsProtocolMessage requestMessage = MessageBuilder.CreateStartNeighborhoodInitializationRequest(PrimaryPort, SrNeighborPort);
      if (await SendMessageAsync(requestMessage))
      {
        PsProtocolMessage responseMessage = await ReceiveMessageAsync();
        if (CheckResponseMessage(requestMessage, responseMessage))
        {
          res = true;
        }
        else
        {
          if (lastResponseStatus != Status.ErrorBusy)
            log.Warn("Received unexpected or invalid message.");
        }
      }
      else log.Warn("Unable to send message.");

        log.Trace("(-):{0}", res);
      return res;
    }


    
    /// <summary>
    /// Sends NeighborhoodSharedProfileUpdateRequest to the server and reads a response.
    /// </summary>
    /// <param name="RequestMessage">Request message to send.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SendNeighborhoodSharedProfileUpdate(PsProtocolMessage RequestMessage)
    {
      log.Trace("()");

      bool res = false;
      PsProtocolMessage requestMessage = RequestMessage;
      if (await SendMessageAsync(requestMessage))
      {
        PsProtocolMessage responseMessage = await ReceiveMessageAsync();
        if (CheckResponseMessage(requestMessage, responseMessage))
        {
          res = true;
        }
        else
        {
          if (lastResponseStatus != Status.ErrorRejected)
            log.Warn("Received unexpected or invalid message.");
        }
      }
      else log.Warn("Unable to send message.");

      log.Trace("(-):{0}", res);
      return res;
    }




    /// <summary>
    /// Sends StopNeighborhoodUpdatesRequest to the server and reads a response.
    /// </summary>
    /// <param name="RequestMessage">Request message to send.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SendStopNeighborhoodUpdates(PsProtocolMessage RequestMessage)
    {
      log.Trace("()");

      bool res = false;
      PsProtocolMessage requestMessage = RequestMessage;
      if (await SendMessageAsync(requestMessage))
      {
        PsProtocolMessage responseMessage = await ReceiveMessageAsync();
        if (CheckResponseMessage(requestMessage, responseMessage))
        {
          res = true;
        }
        else
        {
          if (lastResponseStatus != Status.ErrorNotFound)
            log.Warn("Received unexpected or invalid message.");
        }
      }
      else log.Warn("Unable to send message.");

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends ListRolesRequest to the server and reads a response.
    /// </summary>
    /// <returns>ListRolesResponse message if the function succeeds, null otherwise.</returns>
    public async Task<ListRolesResponse> SendListRolesRequest()
    {
      log.Trace("()");

      ListRolesResponse res = null;
      PsProtocolMessage requestMessage = MessageBuilder.CreateListRolesRequest();
      if (await SendMessageAsync(requestMessage))
      {
        PsProtocolMessage responseMessage = await ReceiveMessageAsync();
        if (CheckResponseMessage(requestMessage, responseMessage))
        {
          res = responseMessage.Response.SingleResponse.ListRoles;
        }
        else log.Warn("Received unexpected or invalid message.");
      }
      else log.Warn("Unable to send message.");

      log.Trace("(-):{0}", res);
      return res;
    }

  }
}
