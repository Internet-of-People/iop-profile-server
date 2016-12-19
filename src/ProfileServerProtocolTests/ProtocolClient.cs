using Google.Protobuf;
using ProfileServerCrypto;
using ProfileServerProtocol;
using Iop.Profileserver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Implements a simple TCP IoP client with TLS support.
  /// </summary>
  public class ProtocolClient : IDisposable
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test.ProtocolClient");

    /// <summary>TCP client for communication with the server.</summary>
    private TcpClient client;

    /// <summary>
    /// Normal or TLS stream for sending and receiving data over TCP client. 
    /// In case of the TLS stream, the underlaying stream is going to be closed automatically.
    /// </summary>
    private Stream stream;

    /// <summary>Message builder for easy creation of protocol message.</summary>
    public MessageBuilder MessageBuilder;

    /// <summary>Client's identity.</summary>
    private KeysEd25519 keys;

    /// <summary>Node's public key received when starting conversation.</summary>
    public byte[] ServerKey;

    /// <summary>Challenge that the node sent to the client when starting conversation.</summary>
    public byte[] Challenge;

    /// <summary>Challenge that the client sent to the node when starting conversation.</summary>
    public byte[] ClientChallenge;

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    public ProtocolClient():
      this(0)
    {
    }

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    /// <param name="IdBase">Base for message numbering.</param>
    public ProtocolClient(uint IdBase):
      this(IdBase, SemVer.V100)
    {
    }

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    /// <param name="IdBase">Base for message numbering.</param>
    /// <param name="ProtocolVersion">Protocol version.</param>
    public ProtocolClient(uint IdBase, SemVer ProtocolVersion):
      this(IdBase, ProtocolVersion, Ed25519.GenerateKeys())
    {
    }

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    /// <param name="IdBase">Base for message numbering.</param>
    /// <param name="ProtocolVersion">Protocol version.</param>
    /// <param name="Keys">Keys that represent the client's identity.</param>
    public ProtocolClient(uint IdBase, SemVer ProtocolVersion, KeysEd25519 Keys)
    {
      client = new TcpClient();
      client.NoDelay = true;
      client.LingerState = new LingerOption(true, 0);
      keys = Keys;
      MessageBuilder = new MessageBuilder(IdBase, new List<SemVer>() { ProtocolVersion }, keys);
    }


    /// <summary>
    /// Connects to the target server address on the specific port and optionally performs TLS handshake.
    /// </summary>
    /// <param name="Address">IP address of the target server.</param>
    /// <param name="Port">TCP port to connect to.</param>
    /// <param name="UseTls">If true, the TLS handshake is performed after the connection is established.</param>
    public async Task ConnectAsync(IPAddress Address, int Port, bool UseTls)
    {
      log.Trace("(Address:'{0}',Port:{1},UseTls:{2})", Address, Port, UseTls);

      await client.ConnectAsync(Address, Port);

      stream = client.GetStream();
      if (UseTls)
      {
        SslStream sslStream = new SslStream(stream, false, PeerCertificateValidationCallback);
        await sslStream.AuthenticateAsClientAsync("", null, SslProtocols.Tls12, false);
        stream = sslStream;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Sends raw binary data over the network stream.
    /// </summary>
    /// <param name="Data">Binary data to send.</param>
    public async Task SendRawAsync(byte[] Data)
    {
      log.Trace("(Message.Length:{0})", Data.Length);

      await stream.WriteAsync(Data, 0, Data.Length);

      log.Trace("(-)");
    }

    /// <summary>
    /// Sends IoP protocol message over the network stream.
    /// </summary>
    /// <param name="Data">Message to send.</param>
    public async Task SendMessageAsync(Message Data)
    {
      string dataStr = Data.ToString();
      log.Trace("()\n{0}", dataStr.Substring(0, Math.Min(dataStr.Length, 512)));

      byte[] rawData = ProtocolHelper.GetMessageBytes(Data);
      await stream.WriteAsync(rawData, 0, rawData.Length);

      log.Trace("(-)");
    }


    /// <summary>
    /// Reads and parses protocol message from the network stream.
    /// </summary>
    /// <returns>Parsed protocol message or null if the function fails.</returns>
    public async Task<Message> ReceiveMessageAsync()
    {
      log.Trace("()");

      Message res = null;

      byte[] header = new byte[ProtocolHelper.HeaderSize];
      int headerBytesRead = 0;
      int remain = header.Length;

      bool done = false;
      log.Trace("Reading message header.");
      while (!done && (headerBytesRead < header.Length))
      {
        int readAmount = await stream.ReadAsync(header, headerBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the header.");
          done = true;
          break;
        }

        headerBytesRead += readAmount;
        remain -= readAmount;
      }

      uint messageSize = BitConverter.ToUInt32(header, 1);
      log.Trace("Message body size is {0} bytes.", messageSize);

      byte[] messageBytes = new byte[ProtocolHelper.HeaderSize + messageSize];
      Array.Copy(header, messageBytes, header.Length);

      remain = (int)messageSize;
      int messageBytesRead = 0;
      while (!done && (messageBytesRead < messageSize))
      {
        int readAmount = await stream.ReadAsync(messageBytes, ProtocolHelper.HeaderSize + messageBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the body.");
          done = true;
          break;
        }

        messageBytesRead += readAmount;
        remain -= readAmount;
      }

      res = MessageWithHeader.Parser.ParseFrom(messageBytes).Body;

      string resStr = res.ToString();
      log.Trace("(-):\n{0}", resStr.Substring(0, Math.Min(resStr.Length, 512)));
      return res;
    }


    /// <summary>
    /// Generates client's challenge and creates start conversation request with it.
    /// </summary>
    /// <returns>StartConversationRequest message that is ready to be sent to the node.</returns>
    public Message CreateStartConversationRequest()
    {
      ClientChallenge = new byte[ProtocolHelper.ChallengeDataSize];
      Crypto.Rng.GetBytes(ClientChallenge);
      Message res = MessageBuilder.CreateStartConversationRequest(ClientChallenge);
      return res;
    }

    /// <summary>
    /// Starts conversation with the server the client is connected to and checks whether the server response contains expected values.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> StartConversationAsync()
    {
      log.Trace("()");      

      Message requestMessage = CreateStartConversationRequest();
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      SemVer receivedVersion = new SemVer(responseMessage.Response.ConversationResponse.Start.Version);
      bool versionOk = receivedVersion.Equals(new SemVer(MessageBuilder.Version));

      bool pubKeyLenOk = responseMessage.Response.ConversationResponse.Start.PublicKey.Length == 32;
      bool challengeOk = responseMessage.Response.ConversationResponse.Start.Challenge.Length == 32;

      ServerKey = responseMessage.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      Challenge = responseMessage.Response.ConversationResponse.Start.Challenge.ToByteArray();
      bool challengeVerifyOk = VerifyServerChallengeSignature(responseMessage);

      bool res = idOk && statusOk && versionOk && pubKeyLenOk && challengeOk && challengeVerifyOk;

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Establishes a home node for the client's identity with specific identity type using the already opened connection to the node.
    /// </summary>
    /// <param name="IdentityType">Identity type of the new identity.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> EstablishHostingAsync(string IdentityType = null)
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      HostingPlanContract contract = null;
      if (IdentityType != null)
      {
        contract = new HostingPlanContract();
        contract.IdentityType = IdentityType;
      }

      Message requestMessage = MessageBuilder.CreateRegisterHostingRequest(contract);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool registerHostingOk = idOk && statusOk;

      bool res = startConversationOk && registerHostingOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs a check-in process for the client's identity using the already opened connection to the node.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> CheckInAsync()
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      Message requestMessage = MessageBuilder.CreateCheckInRequest(Challenge);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool checkInOk = idOk && statusOk;

      bool res = startConversationOk && checkInOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs an identity verification process for the client's identity using the already opened connection to the node.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> VerifyIdentityAsync()
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      Message requestMessage = MessageBuilder.CreateVerifyIdentityRequest(Challenge);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool verifyIdentityOk = idOk && statusOk;

      bool res = startConversationOk && verifyIdentityOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Obtains list of node's service ports.
    /// </summary>
    /// <param name="RolePorts">An empty dictionary that will be filled with mapping of server roles to network ports if the function succeeds.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ListNodePorts(Dictionary<ServerRoleType, uint> RolePorts)
    {
      log.Trace("()");

      Message requestMessage = MessageBuilder.CreateListRolesRequest();
      await SendMessageAsync(requestMessage);

      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      foreach (ServerRole serverRole in responseMessage.Response.SingleResponse.ListRoles.Roles)
        RolePorts.Add(serverRole.Role, serverRole.Port);

      bool primaryPortOk = RolePorts.ContainsKey(ServerRoleType.Primary);
      bool srNeighborPortOk = RolePorts.ContainsKey(ServerRoleType.SrNeighbor);
      bool clNonCustomerPortOk = RolePorts.ContainsKey(ServerRoleType.ClNonCustomer);
      bool clCustomerPortOk = RolePorts.ContainsKey(ServerRoleType.ClCustomer);
      bool clAppServicePortOk = RolePorts.ContainsKey(ServerRoleType.ClAppService);

      bool portsOk = primaryPortOk && srNeighborPortOk && clNonCustomerPortOk && clCustomerPortOk && clAppServicePortOk;
      bool res = idOk && statusOk && portsOk;

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Initializes a new identity profile on the node.
    /// </summary>
    /// <param name="Name">Name of the profile.</param>
    /// <param name="Image">Optionally, a profile image data.</param>
    /// <param name="Location">GPS location of the identity.</param>
    /// <param name="ExtraData">Optionally, identity's extra data.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> InitializeProfileAsync(string Name, byte[] Image, GpsLocation Location, string ExtraData)
    {
      log.Trace("()");

      Message requestMessage = MessageBuilder.CreateUpdateProfileRequest(SemVer.V100, Name, Image, Location, ExtraData);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool res = idOk && statusOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Adds application service to the current checked-in client's session.
    /// </summary>
    /// <param name="ServiceNames">List of service names to add.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> AddApplicationServicesAsync(List<string> ServiceNames)
    {
      log.Trace("()");

      Message requestMessage = MessageBuilder.CreateApplicationServiceAddRequest(ServiceNames);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool res = idOk && statusOk;

      log.Trace("(-):{0}", res);
      return res;
    }

    

    /// <summary>
    /// Closes an open connection and reinitialize the TCP client so that it can be used again.
    /// </summary>
    public void CloseConnection()
    {
      log.Trace("()");

      if (stream != null) stream.Dispose();
      if (client != null) client.Dispose();
      client = new TcpClient();
      client.NoDelay = true;
      client.LingerState = new LingerOption(true, 0);
      ServerKey = null;
      Challenge = null;
      MessageBuilder.ResetId();

      log.Trace("(-)");
    }


    /// <summary>
    /// Obtains network stream of the client.
    /// </summary>
    /// <returns>Network stream of the client.</returns>
    public Stream GetStream()
    {
      return stream;
    }


    /// <summary>
    /// Obtains network identifier of the client's identity.
    /// </summary>
    /// <returns>Network identifier in its binary form.</returns>
    public byte[] GetIdentityId()
    {
      return Crypto.Sha256(keys.PublicKey);
    }

    /// <summary>
    /// Obtains keys that represent the client's identity.
    /// </summary>
    /// <returns>Keys that represent the client's identity.</returns>
    public KeysEd25519 GetIdentityKeys()
    {
      return keys;
    }


    /// <summary>
    /// Verifies whether the server successfully signed the correct start conversation challenge.
    /// </summary>
    /// <param name="StartConversationResponse">StartConversationResponse received from the node.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool VerifyServerChallengeSignature(Message StartConversationResponse)
    {
      log.Trace("()");

      if (ServerKey == null) ServerKey = StartConversationResponse.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      byte[] receivedChallenge = StartConversationResponse.Response.ConversationResponse.Start.ClientChallenge.ToByteArray();
      bool res = (StructuralComparisons.StructuralComparer.Compare(receivedChallenge, ClientChallenge) == 0) 
        && MessageBuilder.VerifySignedConversationResponseBodyPart(StartConversationResponse, receivedChallenge, ServerKey);

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Issues and signes a relationship card.
    /// </summary>
    /// <param name="RecipientPublicKey">Public key of the card recipient.</param>
    /// <param name="CardType">Type of the card.</param>
    /// <param name="ValidFrom">Time from which the card is valid. It must not be greater than <paramref name="ValidTo"/>.</param>
    /// <param name="ValidTo">Time after which the card is not valid.</param>
    /// <returns>Signed relationship card.</returns>
    public SignedRelationshipCard IssueRelationshipCard(byte[] RecipientPublicKey, string CardType, DateTime ValidFrom, DateTime ValidTo)
    {
      return IssueRelationshipCard(SemVer.V100.ToByteArray(), RecipientPublicKey, CardType, ValidFrom, ValidTo);
    }


    /// <summary>
    /// Issues and signes a relationship card.
    /// </summary>
    /// <param name="Version">Relationship card version.</param>
    /// <param name="RecipientPublicKey">Public key of the card recipient.</param>
    /// <param name="CardType">Type of the card.</param>
    /// <param name="ValidFrom">Time from which the card is valid. It must not be greater than <paramref name="ValidTo"/>.</param>
    /// <param name="ValidTo">Time after which the card is not valid.</param>
    /// <returns>Signed relationship card.</returns>
    public SignedRelationshipCard IssueRelationshipCard(byte[] Version, byte[] RecipientPublicKey, string CardType, DateTime ValidFrom, DateTime ValidTo)
    {
      RelationshipCard card = new RelationshipCard()
      {
        CardId = ProtocolHelper.ByteArrayToByteString(new byte[32]),
        Version = ProtocolHelper.ByteArrayToByteString(Version),
        IssuerPublicKey = ProtocolHelper.ByteArrayToByteString(keys.PublicKey),
        RecipientPublicKey = ProtocolHelper.ByteArrayToByteString(RecipientPublicKey),
        Type = CardType,
        ValidFrom = ProtocolHelper.DateTimeToUnixTimestampMs(ValidFrom),
        ValidTo = ProtocolHelper.DateTimeToUnixTimestampMs(ValidTo)
      };

      return IssueRelationshipCard(card);
    }


    /// <summary>
    /// Issues and signes a relationship card.
    /// </summary>
    /// <param name="Card">Relationship card to hash and sign.</param>
    /// <returns>Signed relationship card.</returns>
    public SignedRelationshipCard IssueRelationshipCard(RelationshipCard Card)
    {
      byte[] cardDataToHash = Card.ToByteArray();
      byte[] cardId = Crypto.Sha256(cardDataToHash);
      Card.CardId = ProtocolHelper.ByteArrayToByteString(cardId);

      byte[] signature = Ed25519.Sign(cardId, keys.ExpandedPrivateKey);
      SignedRelationshipCard res = new SignedRelationshipCard()
      {
        Card = Card,
        IssuerSignature = ProtocolHelper.ByteArrayToByteString(signature)
      };

      return res;
    }

    /// <summary>
    /// Creates relationship card application.
    /// </summary>
    /// <param name="ApplicationId">Unique card application ID.</param>
    /// <param name="SignedCard">Signed card for which the application is to be created.</param>
    /// <returns>Card application for a given card.</returns>
    public CardApplicationInformation CreateRelationshipCardApplication(byte[] ApplicationId, SignedRelationshipCard SignedCard)
    {
      CardApplicationInformation res = new CardApplicationInformation()
      {
        ApplicationId = ProtocolHelper.ByteArrayToByteString(ApplicationId),
        CardId = SignedCard.Card.CardId
      };

      return res;
    }


    /// <summary>
    /// Callback routine that validates server TLS certificate.
    /// As we do not perform certificate validation, we just return true.
    /// </summary>
    /// <param name="sender"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="certificate"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="chain"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="sslPolicyErrors"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <returns><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</returns>
    public static bool PeerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
      return true;
    }

    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

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
        if (stream != null) stream.Dispose();
        stream = null;

        if (client != null) client.Dispose();
        client = null;

        disposed = true;
      }
    }
  }
}
