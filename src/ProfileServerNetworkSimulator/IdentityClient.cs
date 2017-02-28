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
using Google.Protobuf;

namespace ProfileServerNetworkSimulator
{
  /// <summary>
  /// Represents a single identity in the network.
  /// </summary>
  public class IdentityClient
  {
    private PrefixLogger log;

    /// <summary>Identity name.</summary>
    private string name;
    /// <summary>Identity name.</summary>
    public string Name { get { return name; } }

    /// <summary>Identity Type.</summary>
    private string type;

    /// <summary>Initial GPS location.</summary>
    private GpsLocation location;

    /// <summary>Profile image file name or null if the identity has no profile image.</summary>
    private string imageFileName;

    /// <summary>Profile image data or null if the identity has no profile image.</summary>
    private byte[] profileImage;
    /// <summary>Profile image data or null if the identity has no profile image.</summary>
    public byte[] ProfileImage { get { return profileImage; } }

    /// <summary>Thumbnail image data or null if the identity has no thumbnail image.</summary>
    private byte[] thumbnailImage;
    /// <summary>Thumbnail image data or null if the identity has no thumbnail image.</summary>
    public byte[] ThumbnailImage { get { return thumbnailImage; } }

    /// <summary>Profile extra data information.</summary>
    private string extraData;

    /// <summary>Profile version.</summary>
    private SemVer version;

    /// <summary>Profile server hosting the identity profile.</summary>
    private ProfileServer profileServer;
    /// <summary>Profile server hosting the identity profile.</summary>
    public ProfileServer ProfileServer { get { return profileServer; } }

    /// <summary>TCP client for communication with the server.</summary>
    private TcpClient client;

    /// <summary>
    /// Normal or TLS stream for sending and receiving data over TCP client. 
    /// In case of the TLS stream, the underlaying stream is going to be closed automatically.
    /// </summary>
    private Stream stream;

    /// <summary>Message builder for easy creation of protocol message.</summary>
    private MessageBuilder messageBuilder;

    /// <summary>Cryptographic Keys that represent the client's identity.</summary>
    private KeysEd25519 keys;

    /// <summary>Network identifier of the client's identity.</summary>
    private byte[] identityId;

    /// <summary>Profile server's public key received when starting conversation.</summary>
    private byte[] profileServerKey;

    /// <summary>Challenge that the profile server sent to the client when starting conversation.</summary>
    private byte[] challenge;

    /// <summary>Challenge that the client sent to the profile server when starting conversation.</summary>
    private byte[] clientChallenge;

    /// <summary>true if the client initialized its profile on the profile server, false otherwise.</summary>
    private bool profileInitialized;

    /// <summary>true if the client has an active hosting agreement with the profile server, false otherwise.</summary>
    private bool hostingActive;

    /// <summary>
    /// Empty constructor for manual construction of the instance when loading simulation for snapshot.
    /// </summary>
    public IdentityClient()
    {
    }


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
      log = new PrefixLogger("ProfileServerSimulator.IdentityClient", "[" + Name + "] ");
      log.Trace("(Name:'{0}',Type:'{1}',Location:{2},ImageMask:'{3}',ImageChance:{4})", Name, Type, Location, ImageMask, ImageChance);

      name = Name;
      type = Type;
      location = Location;
      extraData = null;

      bool hasImage = Helpers.Rng.NextDouble() < (double)ImageChance / 100;
      if (hasImage)
      {
        imageFileName = GetImageFileByMask(ImageMask);
        profileImage = imageFileName != null ? File.ReadAllBytes(imageFileName) : null;
      }

      version = SemVer.V100;
      keys = Ed25519.GenerateKeys();
      identityId = Crypto.Sha256(keys.PublicKey);
      messageBuilder = new MessageBuilder(0, new List<SemVer>() { SemVer.V100 }, keys);

      profileInitialized = false;
      hostingActive = false;

      log.Trace("(-)");
    }


    /// <summary>
    /// Frees resources used by identity client.
    /// </summary>
    public void Shutdown()
    {
      CloseTcpClient();
    }


    /// <summary>
    /// Obtains a file name of a profile image from a group of image file names.
    /// </summary>
    /// <param name="Mask">File name mask in images folder.</param>
    /// <returns>Profile image file name.</returns>
    public string GetImageFileByMask(string Mask)
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

      profileServer = Server;
      InitializeTcpClient();

      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientNonCustomerInterfacePort, true);

        if (await EstablishProfileHostingAsync(type))
        {
          hostingActive = true;
          CloseTcpClient();

          InitializeTcpClient();
          await ConnectAsync(Server.IpAddress, Server.ClientCustomerInterfacePort, true);
          if (await CheckInAsync())
          {
            if (await InitializeProfileAsync(name, profileImage, location, null))
            {
              profileInitialized = true;
              if (profileImage != null)
              {
                if (await GetProfileThumbnailImage())
                {
                  res = true;
                }
                else log.Error("Unable to obtain identity's thumbnail image from profile server '{0}'.", Server.Name);
              }
              else
              {
                res = true;
              }
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
    /// Cancels a hosting agreement with hosting profile server.
    /// </summary>
    /// <returns>true if the function succeeded, false otherwise.</returns>
    public async Task<bool> CancelProfileHosting()
    {
      log.Trace("()");
      bool res = false;

      InitializeTcpClient();

      try
      {
        await ConnectAsync(profileServer.IpAddress, profileServer.ClientCustomerInterfacePort, true);
        if (await CheckInAsync())
        {
          if (await CancelHostingAgreementAsync())
          {
            hostingActive = false;
            res = true;
          }
          else log.Error("Unable to cancel hosting agreement on profile server '{0}'.", profileServer.Name);
        }
        else log.Error("Unable to check-in to profile server '{0}'.", profileServer.Name);
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

      client = new TcpClient();
      client.NoDelay = true;
      client.LingerState = new LingerOption(true, 0);
      messageBuilder.ResetId();

      log.Trace("(-)");
    }

    /// <summary>
    /// Closes an open connection and reinitialize the TCP client so that it can be used again.
    /// </summary>
    public void CloseTcpClient()
    {
      log.Trace("()");

      if (stream != null) stream.Dispose();
      if (client != null) client.Dispose();

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

      Message requestMessage = messageBuilder.CreateRegisterHostingRequest(contract);
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
      clientChallenge = new byte[ProtocolHelper.ChallengeDataSize];
      Crypto.Rng.GetBytes(clientChallenge);
      Message res = messageBuilder.CreateStartConversationRequest(clientChallenge);
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
      bool versionOk = receivedVersion.Equals(new SemVer(messageBuilder.Version));

      bool pubKeyLenOk = responseMessage.Response.ConversationResponse.Start.PublicKey.Length == 32;
      bool challengeOk = responseMessage.Response.ConversationResponse.Start.Challenge.Length == 32;

      profileServerKey = responseMessage.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      challenge = responseMessage.Response.ConversationResponse.Start.Challenge.ToByteArray();

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
    /// Verifies whether the profile server successfully signed the correct start conversation challenge.
    /// </summary>
    /// <param name="StartConversationResponse">StartConversationResponse received from the profile server.</param>
    /// <returns>true if the signature is valid, false otherwise.</returns>
    public bool VerifyProfileServerChallengeSignature(Message StartConversationResponse)
    {
      log.Trace("()");

      byte[] receivedChallenge = StartConversationResponse.Response.ConversationResponse.Start.ClientChallenge.ToByteArray();
      byte[] profileServerPublicKey = StartConversationResponse.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      bool res = (StructuralComparisons.StructuralComparer.Compare(receivedChallenge, clientChallenge) == 0)
        && messageBuilder.VerifySignedConversationResponseBodyPart(StartConversationResponse, receivedChallenge, profileServerPublicKey);

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

      Message requestMessage = messageBuilder.CreateUpdateProfileRequest(SemVer.V100, Name, Image, Location, ExtraData);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool res = idOk && statusOk;

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Obtains its own thumbnail picture from the profile server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> GetProfileThumbnailImage()
    {
      log.Trace("()");

      Message requestMessage = messageBuilder.CreateGetIdentityInformationRequest(identityId, false, true, false);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;
      thumbnailImage = responseMessage.Response.SingleResponse.GetIdentityInformation.ThumbnailImage.ToByteArray();
      if (thumbnailImage.Length == 0) thumbnailImage = null;

      bool res = idOk && statusOk && (thumbnailImage != null);

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

      Message requestMessage = messageBuilder.CreateCheckInRequest(challenge);
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
    /// Cancels a agreement with the profile server, to which there already is an opened connection.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> CancelHostingAgreementAsync()
    {
      log.Trace("()");

      Message requestMessage = messageBuilder.CreateCancelHostingAgreementRequest(null);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool res = idOk && statusOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    public class SearchQueryInfo
    {
      /// <summary>Search results - list of found profiles.</summary>
      public List<IdentityNetworkProfileInformation> Results;

      /// <summary>List of covered servers returned by the queried profile server.</summary>
      public List<byte[]> CoveredServers;
    }


    /// <summary>
    /// Connects to a profile server and performs a search query on it and downloads all possible results.
    /// </summary>
    /// <param name="Server">Profile server to query.</param>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="IncludeHostedOnly">If set to true, the search results should only include profiles hosted on the queried profile server.</param>
    /// <param name="IncludeImages">If set to true, the search results should include images.</param>
    /// <returns>List of results or null if the function fails.</returns>
    public async Task<SearchQueryInfo> SearchQueryAsync(ProfileServer Server, string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius, bool IncludeHostedOnly, bool IncludeImages)
    {
      log.Trace("()");

      SearchQueryInfo res = null;
      bool connected = false;
      try
      {
        await ConnectAsync(Server.IpAddress, Server.ClientNonCustomerInterfacePort, true);
        connected = true;
        if (await StartConversationAsync())
        {
          uint maxResults = (uint)(IncludeImages ? 1000 : 10000);
          uint maxResponseResults = (uint)(IncludeImages ? 100 : 1000);
          Message requestMessage = messageBuilder.CreateProfileSearchRequest(TypeFilter, NameFilter, null, LocationFilter, (uint)Radius, maxResponseResults, maxResults, IncludeHostedOnly, IncludeImages);
          await SendMessageAsync(requestMessage);
          Message responseMessage = await ReceiveMessageAsync();

          bool idOk = responseMessage.Id == requestMessage.Id;
          bool statusOk = responseMessage.Response.Status == Status.Ok;

          bool searchRequestOk = idOk && statusOk;
          if (searchRequestOk)
          {
            int totalResultCount = (int)responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount;
            List<byte[]> coveredServers = new List<byte[]>();
            foreach (ByteString coveredServerId in responseMessage.Response.ConversationResponse.ProfileSearch.CoveredServers)
              coveredServers.Add(coveredServerId.ToByteArray());

            List<IdentityNetworkProfileInformation> results = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.ToList();
            while (results.Count < totalResultCount)
            {
              int remaining = Math.Min((int)maxResponseResults, totalResultCount - results.Count);
              requestMessage = messageBuilder.CreateProfileSearchPartRequest((uint)results.Count, (uint)remaining);
              await SendMessageAsync(requestMessage);
              responseMessage = await ReceiveMessageAsync();

              idOk = responseMessage.Id == requestMessage.Id;
              statusOk = responseMessage.Response.Status == Status.Ok;

              searchRequestOk = idOk && statusOk;
              if (!searchRequestOk) break;

              results.AddRange(responseMessage.Response.ConversationResponse.ProfileSearchPart.Profiles.ToList());
            }

            res = new SearchQueryInfo();
            res.CoveredServers = coveredServers;
            res.Results = results;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (connected) CloseTcpClient();

      if (res != null) log.Trace("(-):*.Results.Count={0},*.CoveredServers.Count={1}", res.Results.Count, res.CoveredServers.Count);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Checks whether the client's identity matches specific search query.
    /// </summary>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius in metres of the target area.</param>
    /// <returns>true if the identity matches the query, false otherwise.</returns>
    public bool MatchesSearchQuery(string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius)
    {
      log.Trace("(NameFilter:'{0}',TypeFilter:'{1}',LocationFilter:'{2}',Radius:{3})", NameFilter, TypeFilter, LocationFilter, Radius);

      bool res = false;
      // Do not include if the profile is unintialized or hosting cancelled.
      if (profileInitialized && hostingActive)
      {
        bool matchType = false;
        bool useTypeFilter = !string.IsNullOrEmpty(TypeFilter) && (TypeFilter != "*") && (TypeFilter != "**");
        if (useTypeFilter)
        {
          string value = type.ToLowerInvariant();
          string filterValue = TypeFilter.ToLowerInvariant();
          matchType = value == filterValue;

          bool valueStartsWith = TypeFilter.EndsWith("*");
          bool valueEndsWith = TypeFilter.StartsWith("*");
          bool valueContains = valueStartsWith && valueEndsWith;

          if (valueContains)
          {
            filterValue = filterValue.Substring(1, filterValue.Length - 2);
            matchType = value.Contains(filterValue);
          }
          else if (valueStartsWith)
          {
            filterValue = filterValue.Substring(0, filterValue.Length - 1);
            matchType = value.StartsWith(filterValue);
          }
          else if (valueEndsWith)
          {
            filterValue = filterValue.Substring(1);
            matchType = value.EndsWith(filterValue);
          }
        }
        else matchType = true;

        bool matchName = false;
        bool useNameFilter = !string.IsNullOrEmpty(NameFilter) && (NameFilter != "*") && (NameFilter != "**");
        if (useNameFilter)
        {
          string value = name.ToLowerInvariant();
          string filterValue = NameFilter.ToLowerInvariant();
          matchName = value == filterValue;

          bool valueStartsWith = NameFilter.EndsWith("*");
          bool valueEndsWith = NameFilter.StartsWith("*");
          bool valueContains = valueStartsWith && valueEndsWith;

          if (valueContains)
          {
            filterValue = filterValue.Substring(1, filterValue.Length - 2);
            matchName = value.Contains(filterValue);
          }
          else if (valueStartsWith)
          {
            filterValue = filterValue.Substring(0, filterValue.Length - 1);
            matchName = value.StartsWith(filterValue);
          }
          else if (valueEndsWith)
          {
            filterValue = filterValue.Substring(1);
            matchName = value.EndsWith(filterValue);
          }
        }
        else matchName = true;

        if (matchType && matchName)
        {
          bool matchLocation = false;
          if (LocationFilter != null)
          {
            double distance = GpsLocation.DistanceBetween(LocationFilter, location);
            matchLocation = distance <= (double)Radius;
          }
          else matchLocation = true;

          res = matchLocation;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Returns network profile information about the client's identity.
    /// </summary>
    /// <param name="IncludeThumbnailImage">If true, the returned profile information will include thumbnail image.</param>
    /// <returns>network profile information about the client's identity.</returns>
    public IdentityNetworkProfileInformation GetIdentityNetworkProfileInformation(bool IncludeThumbnailImage)
    {
      log.Trace("(IncludeThumbnailImage:{0})", IncludeThumbnailImage);

      IdentityNetworkProfileInformation res = new IdentityNetworkProfileInformation()
      {
        IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(keys.PublicKey),
        IsHosted = false,
        IsOnline = false,
        Latitude = location.GetLocationTypeLatitude(),
        Longitude = location.GetLocationTypeLongitude(),
        Name = name != null ? name : "",
        Type = type != null ? type : "",
        Version = version.ToByteString(),
        ExtraData = extraData != null ? extraData : ""
      };

      if (IncludeThumbnailImage && (thumbnailImage != null)) res.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(thumbnailImage);
      else res.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[0]);

      log.Trace("(-)");
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


    /// <summary>
    /// Creates identity client snapshot.
    /// </summary>
    /// <returns>Identity client snapshot.</returns>
    public IdentitySnapshot CreateSnapshot()
    {
      IdentitySnapshot res = new IdentitySnapshot()
      {
        Challenge = Crypto.ToHex(this.challenge),
        ClientChallenge = Crypto.ToHex(this.clientChallenge),
        ExpandedPrivateKeyHex = this.keys.ExpandedPrivateKeyHex,
        ExtraData = this.extraData,
        HostingActive = this.hostingActive,
        IdentityId = Crypto.ToHex(this.identityId),
        ImageFileName = Path.GetFileName(imageFileName),
        LocationLatitude = this.location.Latitude,
        LocationLongitude = this.location.Longitude,
        Name = this.name,
        PrivateKeyHex = this.keys.PrivateKeyHex,
        ProfileInitialized = this.profileInitialized,
        ProfileImageHash = null,
        ProfileServerKey = Crypto.ToHex(this.profileServerKey),
        ProfileServerName = this.profileServer.Name,
        PublicKeyHex = this.keys.PublicKeyHex,
        ThumbnailImageHash = null,
        Type = this.type,
        Version = this.version
      };


      if (this.profileImage != null)
      {
        byte[] profileImageHash = Crypto.Sha256(profileImage);
        string profileImageHashHex = Crypto.ToHex(profileImageHash);
        res.ProfileImageHash = profileImageHashHex;
      }

      if (this.thumbnailImage != null)
      {
        byte[] thumbnailImageHash = Crypto.Sha256(thumbnailImage);
        string thumbnailImageHashHex = Crypto.ToHex(thumbnailImageHash);
        res.ThumbnailImageHash = thumbnailImageHashHex;
      }

      return res;
    }


    /// <summary>
    /// Creates instance of identity client from snapshot.
    /// </summary>
    /// <param name="Snapshot">Identity client snapshot.</param>
    /// <param name="Images">Hexadecimal image data mapping to SHA256 hash.</param>
    /// <param name="ProfileServer">Profile server that hosts identity's profile.</param>
    /// <returns>New identity client instance.</returns>
    public static IdentityClient CreateFromSnapshot(IdentitySnapshot Snapshot, Dictionary<string, string> Images, ProfileServer ProfileServer)
    {
      IdentityClient res = new IdentityClient();

      res.challenge = Crypto.FromHex(Snapshot.Challenge);
      res.clientChallenge = Crypto.FromHex(Snapshot.ClientChallenge);

      res.keys = new KeysEd25519();
      res.keys.ExpandedPrivateKeyHex = Snapshot.ExpandedPrivateKeyHex;
      res.keys.PublicKeyHex = Snapshot.PublicKeyHex;
      res.keys.PrivateKeyHex = Snapshot.PrivateKeyHex;
      res.keys.ExpandedPrivateKey = Crypto.FromHex(res.keys.ExpandedPrivateKeyHex);
      res.keys.PublicKey = Crypto.FromHex(res.keys.PublicKeyHex);
      res.keys.PrivateKey = Crypto.FromHex(res.keys.PrivateKeyHex);

      res.extraData = Snapshot.ExtraData;
      res.hostingActive = Snapshot.HostingActive;
      res.identityId = Crypto.FromHex(Snapshot.IdentityId);
      res.imageFileName = Snapshot.ImageFileName != null ? Path.Combine(CommandProcessor.ImagesDirectory, Snapshot.ImageFileName) : null;
      res.location = new GpsLocation(Snapshot.LocationLatitude, Snapshot.LocationLongitude);
      res.name = Snapshot.Name;
      res.profileInitialized = Snapshot.ProfileInitialized;

      res.profileImage = Snapshot.ProfileImageHash != null ? Crypto.FromHex(Images[Snapshot.ProfileImageHash]) : null;
      res.thumbnailImage = Snapshot.ThumbnailImageHash != null ? Crypto.FromHex(Images[Snapshot.ThumbnailImageHash]) : null;

      res.profileServerKey = Crypto.FromHex(Snapshot.ProfileServerKey);
      res.type = Snapshot.Type;
      res.version = Snapshot.Version;

      res.profileServer = ProfileServer;
      res.log = new PrefixLogger("ProfileServerSimulator.IdentityClient", "[" + res.Name + "] ");
      res.messageBuilder = new MessageBuilder(0, new List<SemVer>() { SemVer.V100 }, res.keys);
      res.InitializeTcpClient();

      return res;
    }

  }
}
