using Google.Protobuf;
using IopCrypto;
using IopProtocol;
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
using System.Text;
using System.Net.Http;
using System.Collections.Specialized;
using Newtonsoft.Json;
using IopCommon;
using IopCommon.Multiformats;
using Iop.Can;

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Implements a simple TCP IoP client with TLS support.
  /// </summary>
  public class ProtocolClient : IDisposable
  {
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests.ProtocolClient");

    /// <summary>TCP client for communication with the server.</summary>
    private TcpClient client;

    /// <summary>
    /// Normal or TLS stream for sending and receiving data over TCP client. 
    /// In case of the TLS stream, the underlaying stream is going to be closed automatically.
    /// </summary>
    private Stream stream;

    /// <summary>Message builder for easy creation of protocol message.</summary>
    public PsMessageBuilder MessageBuilder;

    /// <summary>Client's identity.</summary>
    private KeysEd25519 keys;

    /// <summary>Profile server's public key received when starting conversation.</summary>
    public byte[] ServerKey;

    /// <summary>Challenge that the profile server sent to the client when starting conversation.</summary>
    public byte[] Challenge;

    /// <summary>Challenge that the client sent to the profile server when starting conversation.</summary>
    public byte[] ClientChallenge;

    /// <summary>Information about client's profile.</summary>
    public ClientProfile Profile;

    /// <summary>Random number generator.</summary>
    public static Random Rng = new Random();


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
      MessageBuilder = new PsMessageBuilder(IdBase, new List<SemVer>() { ProtocolVersion }, keys);
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
    public async Task SendMessageAsync(PsProtocolMessage Data)
    {
      string dataStr = Data.ToString();
      log.Trace("()\n{0}", dataStr.Substring(0, Math.Min(dataStr.Length, 512)));

      byte[] rawData = PsMessageBuilder.MessageToByteArray(Data);
      await stream.WriteAsync(rawData, 0, rawData.Length);

      log.Trace("(-)");
    }


    /// <summary>
    /// Reads and parses protocol message from the network stream.
    /// </summary>
    /// <returns>Parsed protocol message or null if the function fails.</returns>
    public async Task<PsProtocolMessage> ReceiveMessageAsync()
    {
      log.Trace("()");

      PsProtocolMessage res = null;

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

      res = new PsProtocolMessage(MessageWithHeader.Parser.ParseFrom(messageBytes).Body);

      string resStr = res.ToString();
      log.Trace("(-):\n{0}", resStr.Substring(0, Math.Min(resStr.Length, 512)));
      return res;
    }


    /// <summary>
    /// Generates client's challenge and creates start conversation request with it.
    /// </summary>
    /// <returns>StartConversationRequest message that is ready to be sent to the profile server.</returns>
    public PsProtocolMessage CreateStartConversationRequest()
    {
      ClientChallenge = new byte[PsMessageBuilder.ChallengeDataSize];
      Crypto.Rng.GetBytes(ClientChallenge);
      PsProtocolMessage res = MessageBuilder.CreateStartConversationRequest(ClientChallenge);
      return res;
    }

    /// <summary>
    /// Starts conversation with the server the client is connected to and checks whether the server response contains expected values.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> StartConversationAsync()
    {
      log.Trace("()");      

      PsProtocolMessage requestMessage = CreateStartConversationRequest();
      await SendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

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
    /// Establishes a hosting agreement for the client's identity with specific identity type using the already opened connection to the profile server.
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

      PsProtocolMessage requestMessage = MessageBuilder.CreateRegisterHostingRequest(contract);
      await SendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool registerHostingOk = idOk && statusOk;

      bool res = startConversationOk && registerHostingOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Connects to a profile server and cancels hosting agreement for the client's identity.
    /// </summary>
    /// <param name="CustomerPort">IP address of the profile server.</param>
    /// <param name="ServerIp">Profile server's customer port interface.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> CancelHostingAgreementAsync(IPAddress ServerIp, int CustomerPort)
    {
      log.Trace("()");

      await ConnectAsync(ServerIp, CustomerPort, true);
      bool checkInOk = await CheckInAsync();

      PsProtocolMessage requestMessage = MessageBuilder.CreateCancelHostingAgreementRequest(null);
      await SendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool cancelHostingOk = idOk && statusOk;

      bool res = checkInOk && cancelHostingOk;

      CloseConnection();

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs a check-in process for the client's identity using the already opened connection to the profile server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> CheckInAsync()
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      PsProtocolMessage requestMessage = MessageBuilder.CreateCheckInRequest(Challenge);
      await SendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool checkInOk = idOk && statusOk;

      bool res = startConversationOk && checkInOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs an identity verification process for the client's identity using the already opened connection to the profile server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> VerifyIdentityAsync()
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      PsProtocolMessage requestMessage = MessageBuilder.CreateVerifyIdentityRequest(Challenge);
      await SendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool verifyIdentityOk = idOk && statusOk;

      bool res = startConversationOk && verifyIdentityOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Obtains list of profile server's service ports.
    /// </summary>
    /// <param name="RolePorts">An empty dictionary that will be filled with mapping of server roles to network ports if the function succeeds.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ListServerPorts(Dictionary<ServerRoleType, uint> RolePorts)
    {
      log.Trace("()");

      PsProtocolMessage requestMessage = MessageBuilder.CreateListRolesRequest();
      await SendMessageAsync(requestMessage);

      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      foreach (Iop.Profileserver.ServerRole serverRole in responseMessage.Response.SingleResponse.ListRoles.Roles)
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
    /// Initializes a new identity profile on the profile server.
    /// </summary>
    /// <param name="Name">Name of the profile.</param>
    /// <param name="Image">Optionally, a profile image data.</param>
    /// <param name="Location">GPS location of the identity.</param>
    /// <param name="ExtraData">Optionally, identity's extra data.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> InitializeProfileAsync(string Name, byte[] Image, GpsLocation Location, string ExtraData)
    {
      log.Trace("()");

      PsProtocolMessage requestMessage = MessageBuilder.CreateUpdateProfileRequest(SemVer.V100, Name, Image, Location, ExtraData);
      await SendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

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

      PsProtocolMessage requestMessage = MessageBuilder.CreateApplicationServiceAddRequest(ServiceNames);
      await SendMessageAsync(requestMessage);
      PsProtocolMessage responseMessage = await ReceiveMessageAsync();

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
    /// <param name="StartConversationResponse">StartConversationResponse received from the profile server.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool VerifyServerChallengeSignature(PsProtocolMessage StartConversationResponse)
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
    /// Creates IPFS path to the object of a given hash.
    /// </summary>
    /// <param name="Hash">Hash of the object.</param>
    /// <returns>IPFS path to the object of the given hash.</returns>
    public string CreateIpfsPathFromHash(byte[] Hash)
    {
      return "/ipfs/" + Base58Encoding.Encoder.EncodeRaw(Hash);
    }


    /// <summary>
    /// Creates IPNS path to the object of a given hash.
    /// </summary>
    /// <param name="Hash">Hash of the object.</param>
    /// <returns>IPNS path to the object of the given hash.</returns>
    public string CreateIpnsPathFromHash(byte[] Hash)
    {
      return "/ipns/" + Base58Encoding.Encoder.EncodeRaw(Hash);
    }


    /// <summary>
    /// Converts public key to CAN ID format.
    /// </summary>
    /// <param name="PublicKey">Ed25519 public key.</param>
    /// <returns>CAN ID that corresponds to the the public.</returns>
    public byte[] CanPublicKeyToId(byte[] PublicKey)
    {
      CanCryptoKey key = new CanCryptoKey()
      {
        Type = CanCryptoKey.Types.KeyType.Ed25519,
        Data = ProtocolHelper.ByteArrayToByteString(PublicKey)
      };

      byte[] hash = Crypto.Sha256(key.ToByteArray());

      byte[] res = new byte[2 + hash.Length];
      res[0] = 0x12; // SHA256 hash prefix
      res[1] = (byte)hash.Length;
      Array.Copy(hash, 0, res, 2, hash.Length);
      return res;
    }



    /// <summary>
    /// Calculates a signature of IPNS record.
    /// </summary>
    /// <param name="Record">IPNS record to calculate signature for.</param>
    /// <returns>Signature of the IPNS record.</returns>
    public byte[] CreateIpnsRecordSignature(CanIpnsEntry Record)
    {
      string validityTypeString = Record.ValidityType.ToString().ToUpperInvariant();
      byte[] validityTypeBytes = Encoding.UTF8.GetBytes(validityTypeString);
      byte[] dataToSign = new byte[Record.Value.Length + Record.Validity.Length + validityTypeBytes.Length];

      int offset = 0;
      Array.Copy(Record.Value.ToByteArray(), 0, dataToSign, offset, Record.Value.Length);
      offset += Record.Value.Length;

      Array.Copy(Record.Validity.ToByteArray(), 0, dataToSign, offset, Record.Validity.Length);
      offset += Record.Validity.Length;

      Array.Copy(validityTypeBytes, 0, dataToSign, offset, validityTypeBytes.Length);
      offset += validityTypeBytes.Length;

      byte[] res = Ed25519.Sign(dataToSign, keys.ExpandedPrivateKey);

      return res;
    }



    /// <summary>
    /// Resolves IPNS name.
    /// </summary>
    /// <param name="CanEndPoint">Address and port of CAN server.</param>
    /// <param name="IpnsPath">CAN path to IPNS.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    public async Task<CanIpnsResolveResult> CanIpnsResolve(IPEndPoint CanEndPoint, string IpnsPath)
    {
      log.Trace("(IpnsPath:'{0}')", IpnsPath);

      NameValueCollection args = new NameValueCollection();
      args.Add("arg", IpnsPath);
      CanApiResult apiResult = await CanSendRequest(CanEndPoint, "name/resolve", args);
      CanIpnsResolveResult res = CanIpnsResolveResult.FromApiResult(apiResult);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Downloads CAN object.
    /// </summary>
    /// <param name="CanEndPoint">Address and port of CAN server.</param>
    /// <param name="IpfsPath">CAN path to object.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    public async Task<CanCatResult> CanGetObject(IPEndPoint CanEndPoint, string IpfsPath)
    {
      log.Trace("(IpfsPath:'{0}')", IpfsPath);

      NameValueCollection args = new NameValueCollection();
      args.Add("arg", IpfsPath);
      CanApiResult apiResult = await CanSendRequest(CanEndPoint, "cat", args);
      CanCatResult res = CanCatResult.FromApiResult(apiResult);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends HTTP POST request to CAN server.
    /// </summary>
    /// <param name="CanEndPoint">Address and port of CAN server.</param>
    /// <param name="Action">Specifies the API function to call.</param>
    /// <param name="Params">List of parameters and their values.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    private async Task<CanApiResult> CanSendRequest(IPEndPoint CanEndPoint, string Action, NameValueCollection Params)
    {
      log.Trace("(CanEndPoint:'{0}',Action:'{1}')", CanEndPoint, Action);

      CanApiResult res = new CanApiResult();

      string query = "";
      foreach (string key in Params)
        query += string.Format("{0}{1}={2}", query.Length > 0 ? "&" : "", WebUtility.HtmlEncode(key), WebUtility.HtmlEncode(Params[key]));

      string apiUrl = string.Format("http://{0}/api/v0/", CanEndPoint);
      string url = string.Format("{0}{1}{2}{3}", apiUrl, Action, query.Length > 0 ? "?" : "", query);
      log.Debug("CAN API URL is '{0}'.", url);

      try
      {
        using (HttpClient client = new HttpClient())
        {
          client.Timeout = TimeSpan.FromSeconds(15);

          bool done = false;
          int attempt = 1;
          while (!done)
          {
            try
            {
              using (HttpResponseMessage message = await client.PostAsync(url, null))
              {
                res.Success = message.IsSuccessStatusCode;
                byte[] data = await message.Content.ReadAsByteArrayAsync();
                string dataStr = null;
                try
                {
                  dataStr = Encoding.UTF8.GetString(data);
                }
                catch
                {
                }

                if (res.Success)
                {
                  res.Data = data;
                  res.DataStr = dataStr;
                }
                else
                {
                  try
                  {
                    CanErrorResponse cer = JsonConvert.DeserializeObject<CanErrorResponse>(dataStr);
                    res.Message = cer.Message;
                  }
                  catch
                  {
                    res.Message = dataStr != null ? dataStr : "Invalid response.";
                  }
                  res.IsCanError = true;
                }
              }
              done = true;
            }
            catch (TaskCanceledException)
            {
              if (attempt < 3)
              {
                log.Debug("Task cancelled, trying again.");
              }
              else
              {
                log.Debug("Task cancelled 3 times in a row, giving up.");
                done = true;
              }
            }

            attempt++;
          }
        }
      }
      catch (Exception e)
      {
        log.Warn("Exception occurred: {0}", e.ToString());
      }

      if (res.Success) log.Trace("(-):*.Success={0},*.Data:\n{1}", res.Success, res.DataStr != null ? res.DataStr.Substring(0, Math.Min(256, res.DataStr.Length)) : "");
      else log.Trace("(-):*.Success={0},*.IsCanError={1},*.Message:\n{2}", res.Success, res.IsCanError, res.Message != null ? res.Message : "");
      return res;
    }


    /// <summary>
    /// Deletes CAN object from CAN server.
    /// </summary>
    /// <param name="CanEndPoint">Address and port of CAN server.</param>
    /// <param name="ObjectPath">CAN path to the object.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    public async Task<CanDeleteResult> CanDeleteObject(IPEndPoint CanEndPoint, string ObjectPath)
    {
      log.Trace("(CanEndPoint:'{0}',ObjectPath:'{1}')", CanEndPoint, ObjectPath);

      NameValueCollection args = new NameValueCollection();
      args.Add("arg", ObjectPath);
      CanApiResult apiResult = await CanSendRequest(CanEndPoint, "pin/rm", args);
      CanDeleteResult res = CanDeleteResult.FromApiResult(apiResult);

      log.Trace("(-):{0}", res);
      return res;
    }






    /// <summary>
    /// Result of CanIpnsResolve function.
    /// </summary>
    public class CanIpnsResolveResult : CanApiResult
    {
      private static Logger log = new Logger("ProfileServerProtocolTests.Tests.ProtocolClient.CanIpnsResolveResult");

      /// <summary>
      /// Structure of the JSON response of CAN '/api/v0/name/resolve' call.
      /// </summary>
      public class CanNameResolveResponse
      {
        /// <summary>Path to which IPNS resolved.</summary>
        public string Path;
      }


      /// <summary>
      /// Creates delete result from generic API result.
      /// </summary>
      /// <param name="ApiResult">Existing instance to copy.</param>
      public CanIpnsResolveResult(CanApiResult ApiResult) :
        base(ApiResult)
      {
      }


      /// <summary>Path to which IPNS resolved.</summary>
      public string Path;

      /// <summary>
      /// Creates a new object based on a result from CAN API including validation checks.
      /// </summary>
      /// <param name="ApiResult">CAN API result object to copy values from.</param>
      /// <returns>Structure describing result of CAN upload operation.</returns>
      public static CanIpnsResolveResult FromApiResult(CanApiResult ApiResult)
      {
        log.Trace("()");

        CanIpnsResolveResult res = new CanIpnsResolveResult(ApiResult);
        if (res.Success)
        {
          bool error = false;
          try
          {
            CanNameResolveResponse response = JsonConvert.DeserializeObject<CanNameResolveResponse>(res.DataStr);
            res.Path = response.Path;
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
            error = true;
          }

          if (error)
          {
            res.Success = false;
            res.Message = "Invalid CAN response.";
            res.IsCanError = false;
          }
        }

        log.Trace("(-)");
        return res;
      }
    }



    /// <summary>
    /// Result of CanDeleteObject function.
    /// </summary>
    public class CanDeleteResult : CanApiResult
    {
      private static Logger log = new Logger("ProfileServerProtocolTests.Tests.ProtocolClient.CanDeleteResult");

      /// <summary>
      /// Structure of the JSON response of CAN '/api/v0/pin/rm' call.
      /// </summary>
      public class CanDeleteObjectResponse
      {
        /// <summary>List of removed pins.</summary>
        public string[] Pins;
      }


      /// <summary>
      /// Creates delete result from generic API result.
      /// </summary>
      /// <param name="ApiResult">Existing instance to copy.</param>
      public CanDeleteResult(CanApiResult ApiResult) :
        base(ApiResult)
      {
      }


      /// <summary>List of removed pins.</summary>
      public string[] Pins;

      /// <summary>
      /// Creates a new object based on a result from CAN API including validation checks.
      /// </summary>
      /// <param name="ApiResult">CAN API result object to copy values from.</param>
      /// <returns>Structure describing result of CAN upload operation.</returns>
      public static CanDeleteResult FromApiResult(CanApiResult ApiResult)
      {
        log.Trace("()");

        CanDeleteResult res = new CanDeleteResult(ApiResult);
        if (res.Success)
        {
          bool error = false;
          try
          {
            CanDeleteObjectResponse response = JsonConvert.DeserializeObject<CanDeleteObjectResponse>(res.DataStr);
            res.Pins = response.Pins;

            // If the object was deleted previously, we might have empty Pins in response.
            // We are thus OK if we receive success response and no more validation is done.
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
            error = true;
          }

          if (error)
          {
            res.Success = false;
            res.Message = "Invalid CAN response.";
            res.IsCanError = false;
          }
        }
        else if (res.Message.ToLowerInvariant() == "not pinned")
        {
          res.Success = true;
          res.Pins = null;
        }

        log.Trace("(-)");
        return res;
      }
    }

    /// <summary>
    /// Result of CanIpnsResolve function.
    /// </summary>
    public class CanCatResult : CanApiResult
    {
      private static Logger log = new Logger("ProfileServerProtocolTests.Tests.ProtocolClient.CanCanResult");

      /// <summary>
      /// Structure of the JSON response of CAN '/api/v0/name/resolve' call.
      /// </summary>
      public class CanCatResponse
      {
        /// <summary>Path to which IPNS resolved.</summary>
        public string Path;
      }


      /// <summary>
      /// Creates delete result from generic API result.
      /// </summary>
      /// <param name="ApiResult">Existing instance to copy.</param>
      public CanCatResult(CanApiResult ApiResult) :
        base(ApiResult)
      {
      }


      /// <summary>
      /// Creates a new object based on a result from CAN API including validation checks.
      /// </summary>
      /// <param name="ApiResult">CAN API result object to copy values from.</param>
      /// <returns>Structure describing result of CAN upload operation.</returns>
      public static CanCatResult FromApiResult(CanApiResult ApiResult)
      {
        log.Trace("()");

        CanCatResult res = new CanCatResult(ApiResult);
      
        log.Trace("(-)");
        return res;
      }
    }


    /// <summary>
    /// Result of CAN API call.
    /// </summary>
    public class CanApiResult
    {
      /// <summary>true if the function succeeds, false otherwise.</summary>
      public bool Success;

      /// <summary>If Success is true, this contains response data.</summary>
      public byte[] Data;

      /// <summary>String representation of Data, or null if Data does not hold a string.</summary>
      public string DataStr;

      /// <summary>
      /// If Success is false and IsCanError is true, this is an error message from CAN server.
      /// If Success is false and IsCanError is false, this is an error message from our code.
      /// </summary>
      public string Message;

      /// <summary>If Success is false, this is true if the error was reported by CAN server, and this is false if the error comes from our code.</summary>
      public bool IsCanError;

      /// <summary>
      /// Creates a default instance of the object.
      /// </summary>
      public CanApiResult()
      {
        Success = false;
        Data = null;
        DataStr = null;           
        Message = "Internal error.";
        IsCanError = false;
      }

      /// <summary>
      /// Creates an instance of the object as a copy of another existing instance.
      /// </summary>
      /// <param name="ApiResult">Existing instance to copy.</param>
      public CanApiResult(CanApiResult ApiResult)
      {
        Success = ApiResult.Success;
        Data = ApiResult.Data;
        DataStr = ApiResult.DataStr;
        Message = ApiResult.Message;
        IsCanError = ApiResult.IsCanError;
      }
    }

    /// <summary>
    /// Structure of the CAN error JSON response.
    /// </summary>
    public class CanErrorResponse
    {
      /// <summary>Error message.</summary>
      public string Message;

      /// <summary>Error code.</summary>
      public int Code;
    }


    /// <summary>
    /// Initializes client's profile by generating random meaningful data.
    /// </summary>
    /// <param name="Index">Index of the profile which will be reflected in profile's name.</param>
    /// <param name="ImageData">Profile image data in case the profile will be generated with an image.</param>
    public void InitializeRandomProfile(int Index, byte[] ImageData)
    {
      decimal latitude = (decimal)(Rng.NextDouble() * ((double)GpsLocation.LatitudeMax - (double)GpsLocation.LatitudeMin)) + GpsLocation.LatitudeMin;
      decimal longitude = (decimal)(Rng.NextDouble() * ((double)GpsLocation.LongitudeMax - (double)GpsLocation.LongitudeMin)) + GpsLocation.LongitudeMin;
      if (longitude == GpsLocation.LongitudeMin) longitude = GpsLocation.LongitudeMax;
      GpsLocation location = new GpsLocation(latitude, longitude);

      string name = string.Format("Identity#{0:0000}", Index);

      string type = "test";

      bool hasImage = Rng.NextDouble() < 0.80;
      byte[] imageData = hasImage ? ImageData : null;

      bool hasExtraData = Rng.NextDouble() < 0.60;
      int extraDataLen = Rng.Next(1, 120);
      byte[] extraData = hasExtraData ? new byte[extraDataLen] : null;
      if (hasExtraData) Crypto.Rng.GetBytes(extraData);
      string extraDataStr = extraData != null ? Convert.ToBase64String(extraData) : null;

      Profile = new ClientProfile()
      {
        Version = SemVer.V100,
        Name = name,
        Type = type,
        ProfileImage = imageData,
        PublicKey = keys.PublicKey,
        ThumbnailImage = null,
        Location = location,
        ExtraData = extraDataStr
      };
    }


    /// <summary>
    /// Establishes a hosting agreement with the profile, initializes a profile and retrieves thumbnail image to the client's profile.
    /// </summary>
    /// <param name="ServerIp">IP address of the profile server.</param>
    /// <param name="NonCustomerPort">Profile server's clNonCustomer port.</param>
    /// <param name="CustomerPort">Profile server's clCustomer port.</param>
    /// <returns>true, if the function succeeds, false otherwise.</returns>
    /// <remarks>The function requires client's Profile to be initialized.</remarks>
    public async Task<bool> RegisterAndInitializeProfileAsync(IPAddress ServerIp, int NonCustomerPort, int CustomerPort)
    {
      log.Trace("()");

      bool res = false;

      await ConnectAsync(ServerIp, NonCustomerPort, true);
      bool hostingEstablished = await EstablishHostingAsync(Profile.Type);
      CloseConnection();

      if (hostingEstablished)
      {
        await ConnectAsync(ServerIp, CustomerPort, true);
        bool checkInOk = await CheckInAsync();
        bool initializeProfileOk = await InitializeProfileAsync(Profile.Name, Profile.ProfileImage, Profile.Location, Profile.ExtraData);

        bool imageOk = false;
        if (Profile.ProfileImage != null)
        {
          PsProtocolMessage requestMessage = MessageBuilder.CreateGetIdentityInformationRequest(GetIdentityId(), false, true, false);
          await SendMessageAsync(requestMessage);

          PsProtocolMessage responseMessage = await ReceiveMessageAsync();
          Profile.ThumbnailImage = responseMessage.Response.SingleResponse.GetIdentityInformation.ThumbnailImage.ToByteArray();
          imageOk = Profile.ThumbnailImage.Length > 0;
        }
        else imageOk = true;

        CloseConnection();
        
        res = checkInOk && initializeProfileOk && imageOk;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Converts client's profile to SharedProfileAddItem structure.
    /// </summary>
    /// <returns>SharedProfileAddItem representing the client's profile.</returns>
    public SharedProfileAddItem GetSharedProfileAddItem()
    {
      SharedProfileAddItem res = new SharedProfileAddItem()
      {
        Version = Profile.Version.ToByteString(),
        Name = Profile.Name != null ? Profile.Name : "",
        Type = Profile.Type,
        ExtraData = Profile.ExtraData != null ? Profile.ExtraData : "",
        Latitude = Profile.Location.GetLocationTypeLatitude(),
        Longitude = Profile.Location.GetLocationTypeLongitude(),
        IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(Profile.PublicKey),
        SetThumbnailImage = Profile.ThumbnailImage != null,
        ThumbnailImage = ProtocolHelper.ByteArrayToByteString(Profile.ThumbnailImage != null ? Profile.ThumbnailImage : new byte[0])
      };
      return res;
    }


    /// <summary>
    /// Converts client's profile to SharedProfileUpdateItem structure with filled in Add member.
    /// </summary>
    /// <returns>SharedProfileUpdateItem representing the client's profile.</returns>
    public SharedProfileUpdateItem GetSharedProfileUpdateAddItem()
    {
      SharedProfileUpdateItem res = new SharedProfileUpdateItem()
      {
        Add = GetSharedProfileAddItem()
      };
      return res;
    }

    /// <summary>
    /// Converts client's profile to SharedProfileDeleteItem structure.
    /// </summary>
    /// <returns>SharedProfileDeleteItem representing the client's profile.</returns>
    public SharedProfileDeleteItem GetSharedProfileDeleteItem()
    {
      SharedProfileDeleteItem res = new SharedProfileDeleteItem()
      {
        IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(Profile.PublicKey))
      };
      return res;
    }

    /// <summary>
    /// Converts client's profile to SharedProfileUpdateItem structure with filled in Delete member.
    /// </summary>
    /// <returns>SharedProfileUpdateItem representing the client's profile.</returns>
    public SharedProfileUpdateItem GetSharedProfileUpdateDeleteItem()
    {
      SharedProfileUpdateItem res = new SharedProfileUpdateItem()
      {
        Delete = GetSharedProfileDeleteItem()
      };
      return res;
    }



    /// <summary>
    /// Converts client's profile to IdentityNetworkProfileInformation structure.
    /// </summary>
    /// <param name="IsHosted">Value for IdentityNetworkProfileInformation.IsHosted field.</param>
    /// <param name="IsOnline">Value for IdentityNetworkProfileInformation.IsOnline field.</param>
    /// <param name="HostingProfileServerId">Value for IdentityNetworkProfileInformation.HostingServerNetworkId field.</param>
    /// <returns>IdentityNetworkProfileInformation representing the client's profile.</returns>
    public IdentityNetworkProfileInformation GetIdentityNetworkProfileInformation(bool IsHosted, bool IsOnline, byte[] HostingProfileServerId)
    {
      IdentityNetworkProfileInformation res = new IdentityNetworkProfileInformation()
      {
        IsHosted = IsHosted,
        IsOnline = IsOnline,
        Version = Profile.Version.ToByteString(),
        Name = Profile.Name != null ? Profile.Name : "",
        Type = Profile.Type,
        ExtraData = Profile.ExtraData != null ? Profile.ExtraData : "",
        Latitude = Profile.Location.GetLocationTypeLatitude(),
        Longitude = Profile.Location.GetLocationTypeLongitude(),
        IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(Profile.PublicKey),
        ThumbnailImage = ProtocolHelper.ByteArrayToByteString(Profile.ThumbnailImage != null ? Profile.ThumbnailImage : new byte[0]),
        HostingServerNetworkId = ProtocolHelper.ByteArrayToByteString(HostingProfileServerId != null ? HostingProfileServerId : new byte[0])        
      };

      return res;
    }




    /// <summary>
    /// Performs an identity verification followed by a neighborhood initialization process with the profile server to which the client is already connected to.
    /// </summary>
    /// <param name="PrimaryPort">Primary port of the client's simulated profile server.</param>
    /// <param name="SrNeighborPort">Server neighbor port of the client's simulated profile server.</param>
    /// <param name="ServerIp">Server's IP address.</param>
    /// <param name="ClientList">List of clients with initialized profiles that the client is expected to receive from the server duting the neighborhood initialization process.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> NeighborhoodInitializationProcessAsync(int PrimaryPort, int SrNeighborPort, IPAddress ServerIp, Dictionary<string, ProtocolClient> ClientList)
    {
      log.Trace("(PrimaryPort:{0},SrNeighborPort:{1},ServerIp:{2},ClientList.Count:{3})", PrimaryPort, SrNeighborPort, ServerIp, ClientList.Count);

      bool verifyIdentityOk = await VerifyIdentityAsync();

      // Start neighborhood initialization process.
      PsProtocolMessage requestMessage = MessageBuilder.CreateStartNeighborhoodInitializationRequest((uint)PrimaryPort, (uint)SrNeighborPort, ServerIp);
      await SendMessageAsync(requestMessage);

      PsProtocolMessage responseMessage = await ReceiveMessageAsync();
      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;
      bool startNeighborhoodInitializationOk = idOk && statusOk;


      // Wait for update request.
      PsProtocolMessage serverRequestMessage = null;
      PsProtocolMessage clientResponseMessage = null;
      bool typeOk = false;

      List<SharedProfileAddItem> receivedItems = new List<SharedProfileAddItem>();

      bool error = false;
      while (receivedItems.Count < ClientList.Count)
      {
        serverRequestMessage = await ReceiveMessageAsync();
        typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

        clientResponseMessage = MessageBuilder.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
        await SendMessageAsync(clientResponseMessage);


        if (!typeOk) break;

        foreach (SharedProfileUpdateItem updateItem in serverRequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
        {
          if (updateItem.ActionTypeCase != SharedProfileUpdateItem.ActionTypeOneofCase.Add)
          {
            log.Trace("Received invalid update item action type '{0}'.", updateItem.ActionTypeCase);
            error = true;
            break;
          }

          receivedItems.Add(updateItem.Add);
        }

        if (error) break;
      }

      log.Trace("Received {0} profiles from target profile server.", receivedItems.Count);
      bool receivedProfilesOk = !error && CheckProfileListMatchAddItems(ClientList, receivedItems);

      // Wait for finish request.
      serverRequestMessage = await ReceiveMessageAsync();
      typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
        && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
        && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

      bool finishNeighborhoodInitializationResponseOk = typeOk;

      clientResponseMessage = MessageBuilder.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
      await SendMessageAsync(clientResponseMessage);

      bool res = verifyIdentityOk && startNeighborhoodInitializationOk && receivedProfilesOk && finishNeighborhoodInitializationResponseOk;


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether a list of expected clients matches the list received from a neighbor server. This function works with incoming list of added clients.
    /// </summary>
    /// <param name="ExpectedClientList">List of clients we are expecting to be updated about, mapped by their profile names.</param>
    /// <param name="RealClientList">List of real clients in form of neighborhood add updates items.</param>
    /// <returns>true if the lists are equal, false otherwise.</returns>
    public bool CheckProfileListMatchAddItems(Dictionary<string, ProtocolClient> ExpectedClientList, List<SharedProfileAddItem> RealClientList)
    {
      log.Trace("()");

      bool error = false;
      Dictionary<string, ProtocolClient> clientList = new Dictionary<string, ProtocolClient>(ExpectedClientList, StringComparer.Ordinal);
      foreach (SharedProfileAddItem receivedItem in RealClientList)
      {
        byte[] receivedItemBytes = receivedItem.ToByteArray();
        ProtocolClient client;
        if (!clientList.TryGetValue(receivedItem.Name, out client))
        {
          log.Trace("Received item name '{0}' not found among expected items.", receivedItem.Name);
          error = true;
          break;
        }

        SharedProfileAddItem profileInfo = client.GetSharedProfileAddItem();
        byte[] profileInfoBytes = profileInfo.ToByteArray();
        if (StructuralComparisons.StructuralComparer.Compare(receivedItemBytes, profileInfoBytes) != 0)
        {
          log.Trace("Data of profile name '{0}' do not match.", receivedItem.Name);
          error = true;
          break;
        }

        clientList.Remove(receivedItem.Name);
      }

      bool res = !error && (clientList.Count == 0);

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Checks whether a list of expected clients matches the list received from a neighbor server. This function works with incoming list of deleted clients.
    /// </summary>
    /// <param name="ExpectedClientList">List of network identifiers of clients we are expecting to be updated about.</param>
    /// <param name="RealClientList">List of real clients in form of neighborhood delete updates items.</param>
    /// <returns>true if the lists are equal, false otherwise.</returns>
    public bool CheckProfileListMatchDeleteItems(HashSet<byte[]> ExpectedClientList, List<SharedProfileDeleteItem> RealClientList)
    {
      log.Trace("()");

      bool error = false;
      HashSet<byte[]> clientList = new HashSet<byte[]>(ExpectedClientList, StructuralEqualityComparer<byte[]>.Default);
      foreach (SharedProfileDeleteItem receivedItem in RealClientList)
      {
        byte[] receivedItemId = receivedItem.IdentityNetworkId.ToByteArray();
        if (!clientList.Contains(receivedItemId))
        {
          log.Trace("Received item ID '{0}' not found among expected items.", receivedItemId.ToHex());
          error = true;
          break;
        }

        clientList.Remove(receivedItemId);
      }

      bool res = !error && (clientList.Count == 0); 

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether a list of expected clients matches the list received from a neighbor server. This function works with incoming list of clients with updated profiles.
    /// </summary>
    /// <param name="ExpectedClientList">List of neighborhood change update items for clients we are expecting to be updated about, mapped by their network identifier.</param>
    /// <param name="RealClientList">List of real clients in form of neighborhood change updates items.</param>
    /// <returns>true if the lists are equal, false otherwise.</returns>
    public bool CheckProfileListMatchChangeItems(Dictionary<byte[], SharedProfileChangeItem> ExpectedClientList, List<SharedProfileChangeItem> RealClientList)
    {
      log.Trace("()");

      bool error = false;
      Dictionary<byte[], SharedProfileChangeItem> clientList = new Dictionary<byte[], SharedProfileChangeItem>(ExpectedClientList, StructuralEqualityComparer<byte[]>.Default);
      foreach (SharedProfileChangeItem receivedItem in RealClientList)
      {
        byte[] receivedItemId = receivedItem.IdentityNetworkId.ToByteArray();
        SharedProfileChangeItem expectedItem;
        if (!clientList.TryGetValue(receivedItemId, out expectedItem))
        {
          log.Trace("Received item ID '{0}' not found among expected items.", receivedItemId.ToHex());
          error = true;
          break;
        }

        byte[] expectedItemBytes = expectedItem.ToByteArray();
        byte[] receivedItemBytes = receivedItem.ToByteArray();
        if (StructuralComparisons.StructuralComparer.Compare(receivedItemBytes, expectedItemBytes) != 0)
        {
          log.Trace("Data of item ID '{0}' do not match.", receivedItemId.ToHex());
          error = true;
          break;
        }

        clientList.Remove(receivedItemId);
      }

      bool res = !error && (clientList.Count == 0);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether a list of expected clients matches the list received from a neighbor server. This function works with search result list format.
    /// </summary>
    /// <param name="ExpectedClientList">List of clients we are expecting to be updated about, mapped by their profile names.</param>
    /// <param name="RealClientList">List of real clients in form of neighborhood add updates items.</param>
    /// <param name="IsHosted">true if the expected clients are hosted on the server that was quieried, false otherwise.</param>
    /// <param name="IsOnline">If <paramref name="IsHosted"/> is true, this value indicates whether the clients are online on the server being queried.</param>
    /// <param name="HostingProfileServerId">If <paramref name="IsHosted"/> is false, this is network ID of the profile server who hosts the expected clients.</param>
    /// <param name="IncludeImages">If set to true, images are included in the search results.</param>
    /// <returns>true if the lists are equal, false otherwise.</returns>
    public bool CheckProfileListMatchSearchResultItems(Dictionary<string, ProtocolClient> ExpectedClientList, List<IdentityNetworkProfileInformation> RealClientList, bool IsHosted, bool IsOnline, byte[] HostingProfileServerId, bool IncludeImages)
    {
      log.Trace("(ExpectedClientList.Count:{0},RealClientList.Count:{1})", ExpectedClientList.Count, RealClientList.Count);

      bool error = false;
      Dictionary<string, ProtocolClient> clientList = new Dictionary<string, ProtocolClient>(ExpectedClientList, StringComparer.Ordinal);
      foreach (IdentityNetworkProfileInformation receivedItem in RealClientList)
      {
        byte[] receivedItemBytes = receivedItem.ToByteArray();
        ProtocolClient client;
        if (!clientList.TryGetValue(receivedItem.Name, out client))
        {
          log.Trace("Received item name '{0}' not found among expected items.", receivedItem.Name);
          error = true;
          break;
        }

        IdentityNetworkProfileInformation profileInfo = client.GetIdentityNetworkProfileInformation(IsHosted, IsOnline, HostingProfileServerId);
        byte[] profileInfoBytes = profileInfo.ToByteArray();
        if (StructuralComparisons.StructuralComparer.Compare(receivedItemBytes, profileInfoBytes) != 0)
        {
          log.Trace("Data of profile name '{0}' do not match.", receivedItem.Name);
          error = true;
          break;
        }

        clientList.Remove(receivedItem.Name);
      }

      bool res = !error && (clientList.Count == 0);

      log.Trace("(-):{0}", res);
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
