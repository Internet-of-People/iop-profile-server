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

namespace ProfileServerSimulator
{
  /// <summary>
  /// Represents a single identity in the network.
  /// </summary>
  public class IdentityClient
  {
    private static PrefixLogger log;

    /// <summary>Identity name.</summary>
    public string Name;

    /// <summary>Identity Type.</summary>
    public string Type;

    /// <summary>Initial GPS location.</summary>
    public GpsLocation Location;

    /// <summary>Profile image file name or null if the identity has no profile image.</summary>
    public string ImageFileName;

    /// <summary>Profile image data or null if the identity has no profile image.</summary>
    public byte[] Image;


    /// <summary>Profile server hosting the identity profile.</summary>
    public ProfileServer ProfileServer;

    /// <summary>TCP client for communication with the server.</summary>
    public TcpClient Client;

    /// <summary>
    /// Normal or TLS stream for sending and receiving data over TCP client. 
    /// In case of the TLS stream, the underlaying stream is going to be closed automatically.
    /// </summary>
    public Stream Stream;

    /// <summary>Message builder for easy creation of protocol message.</summary>
    public MessageBuilder MessageBuilder;

    /// <summary>Cryptographic Keys that represent the client's identity.</summary>
    public KeysEd25519 Keys;

    /// <summary>Profile server's public key received when starting conversation.</summary>
    public byte[] ProfileServerKey;

    /// <summary>Challenge that the profile server sent to the client when starting conversation.</summary>
    public byte[] Challenge;

    /// <summary>Challenge that the client sent to the profile server when starting conversation.</summary>
    public byte[] ClientChallenge;


    /// <summary>
    /// Creates a new identity client.
    /// </summary>
    /// <param name="Name">Identity name.</param>
    /// <param name="Type">Identity type.</param>
    /// <param name="Location">Initial GPS location.</param>
    /// <param name="ImageMask">File name mask in the images folder that define which images can be randomly selected for profile image.</param>
    /// <param name="ImageChance">An integer between 0 and 100 that specifies the chance of each instance to have a profile image set.</param>
    public IdentityClient(string Name, string Type, GpsLocation Location, string ImageMask, int ImageChance)
    {
      log = new PrefixLogger("ProfileServerSimulator.IdentityClient", Name);
      log.Trace("(Name:'{0}',Type:'{1}',Location:{2},ImageMask:'{3}',ImageChance:{4})", Name, Type, Location, ImageMask, ImageChance);

      this.Name = Name;
      this.Type = Type;
      this.Location = Location;

      bool hasImage = Helpers.Rng.NextDouble() < (double)ImageChance / 100;
      if (hasImage)
      {
        ImageFileName = GetImageFileByMask(ImageMask);
        Image = ImageFileName != null ? File.ReadAllBytes(ImageFileName) : null;
      }


      Keys = Ed25519.GenerateKeys();
      MessageBuilder = new MessageBuilder(0, new List<SemVer>() { SemVer.V100 }, Keys);

      log.Trace("(-)");
    }


    /// <summary>
    /// Frees resources used by identity client.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      CloseTcpClient();

      log.Trace("(-)");
    }


    /// <summary>
    /// Obtains a file name of a profile image from a group of image file names.
    /// </summary>
    /// <param name="Mask">File name mask in images folder.</param>
    /// <returns>Profile image file name.</returns>
    public static string GetImageFileByMask(string Mask)
    {
      log.Trace("(Mask:'{0}')", Mask);

      string res = null;
      string path = CommandProcessor.ImagesDirectory;
      string[] files = Directory.GetFiles(path, Mask, SearchOption.TopDirectoryOnly);
      if (files.Length > 0)
      {
        int fileIndex = Helpers.Rng.Next(files.Length);
        res = files[fileIndex];
      }

      log.Trace("(-):{0}", res != null ? "Image" : "null");
      return res;
    }


    /// <summary>
    /// Establishes a hosting agreement with a profile server and initializes a profile.
    /// </summary>
    /// <param name="Server">Profile server to host the identity.</param>
    /// <returns>true if the function succeeded, false otherwise.</returns>
    public async Task<bool> InitializeProfileHosting(ProfileServer Server)
    {
      log.Trace("(Server.Name:'{0}')", Server.Name);
      bool res = false;

      ProfileServer = Server;
      InitializeTcpClient();

      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientNonCustomerInterfacePort, true);

        if (await EstablishProfileHostingAsync(Type))
        {
          CloseTcpClient();

          InitializeTcpClient();
          await ConnectAsync(Server.IpAddress, Server.ClientCustomerInterfacePort, true);
          if (await CheckInAsync())
          {
            if (await InitializeProfileAsync(Name, Image, Location, null))
            {
              res = true;
            }
            else log.Error("Unable to initialize profile on profile server '{0}'.", Server.Name);
          }
          else log.Error("Unable to check-in to profile server '{0}'.", Server.Name);
        }
        else log.Error("Unable to establish profile hosting with server '{0}'.", Server.Name);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      CloseTcpClient();

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Initializes TCP client to be ready to connect to the server.
    /// </summary>
    public void InitializeTcpClient()
    {
      log.Trace("()");

      CloseTcpClient();

      Client = new TcpClient();
      Client.NoDelay = true;
      Client.LingerState = new LingerOption(true, 0);
      MessageBuilder.ResetId();

      log.Trace("(-)");
    }

    /// <summary>
    /// Closes an open connection and reinitialize the TCP client so that it can be used again.
    /// </summary>
    public void CloseTcpClient()
    {
      log.Trace("()");

      if (Stream != null) Stream.Dispose();
      if (Client != null) Client.Dispose();

      log.Trace("(-)");
    }

    /// <summary>
    /// Establishes a hosting agreement for the client's identity with specific identity type using the already opened connection to the profile server.
    /// </summary>
    /// <param name="IdentityType">Identity type of the new identity.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> EstablishProfileHostingAsync(string IdentityType = null)
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
    /// Generates client's challenge and creates start conversation request with it.
    /// </summary>
    /// <returns>StartConversationRequest message that is ready to be sent to the profile server.</returns>
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
      bool challengeVerifyOk = VerifyProfileServerChallengeSignature(responseMessage);

      SemVer receivedVersion = new SemVer(responseMessage.Response.ConversationResponse.Start.Version);
      bool versionOk = receivedVersion.Equals(new SemVer(MessageBuilder.Version));

      bool pubKeyLenOk = responseMessage.Response.ConversationResponse.Start.PublicKey.Length == 32;
      bool challengeOk = responseMessage.Response.ConversationResponse.Start.Challenge.Length == 32;

      ProfileServerKey = responseMessage.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      Challenge = responseMessage.Response.ConversationResponse.Start.Challenge.ToByteArray();

      bool res = idOk && statusOk && challengeVerifyOk && versionOk && pubKeyLenOk && challengeOk;

      log.Trace("(-):{0}", res);
      return res;
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
      await Stream.WriteAsync(rawData, 0, rawData.Length);

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
        int readAmount = await Stream.ReadAsync(header, headerBytesRead, remain);
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
        int readAmount = await Stream.ReadAsync(messageBytes, ProtocolHelper.HeaderSize + messageBytesRead, remain);
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
    /// Verifies whether the profile server successfully signed the correct start conversation challenge.
    /// </summary>
    /// <param name="StartConversationResponse">StartConversationResponse received from the profile server.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool VerifyProfileServerChallengeSignature(Message StartConversationResponse)
    {
      log.Trace("()");

      byte[] receivedChallenge = StartConversationResponse.Response.ConversationResponse.Start.ClientChallenge.ToByteArray();
      byte[] profileServerPublicKey = StartConversationResponse.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      bool res = (StructuralComparisons.StructuralComparer.Compare(receivedChallenge, ClientChallenge) == 0)
        && MessageBuilder.VerifySignedConversationResponseBodyPart(StartConversationResponse, receivedChallenge, profileServerPublicKey);

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
    /// Performs a check-in process for the client's identity using the already opened connection to the profile server.
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
    /// Connects to the target server address on the specific port and optionally performs TLS handshake.
    /// </summary>
    /// <param name="Address">IP address of the target server.</param>
    /// <param name="Port">TCP port to connect to.</param>
    /// <param name="UseTls">If true, the TLS handshake is performed after the connection is established.</param>
    public async Task ConnectAsync(IPAddress Address, int Port, bool UseTls)
    {
      log.Trace("(Address:'{0}',Port:{1},UseTls:{2})", Address, Port, UseTls);

      await Client.ConnectAsync(Address, Port);

      Stream = Client.GetStream();
      if (UseTls)
      {
        SslStream sslStream = new SslStream(Stream, false, PeerCertificateValidationCallback);
        await sslStream.AuthenticateAsClientAsync("", null, SslProtocols.Tls12, false);
        Stream = sslStream;
      }

      log.Trace("(-)");
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

  }
}
