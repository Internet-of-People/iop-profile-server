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
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS08003 - Neighborhood Initialization Process - Small Set
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08003---neighborhood-initialization-process---small-set
  /// </summary>
  public class PS08003 : ProtocolTest
  {
    public const string TestName = "PS08003";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Test identities profile types.</summary>
    public static List<string> ProfileTypes = new List<string>()
    {
      "Profile Type A",
      "Profile Type A",
      "Profile Type A",
      "Profile Type B",
      "Profile Type B",
      "Profile Type C",
      "Profile Type C",
    };


    /// <summary>Test identities profile names.</summary>
    public static List<string> ProfileNames = new List<string>()
    {
      "Shanghai 1",
      "Mumbai 1",
      "Karachi",
      "Buenos Aires",
      "Shanghai 2",
      "Mumbai 2",
      "Mumbai 3",
    };


    /// <summary>Test identities profile locations.</summary>
    public static List<GpsLocation> ProfileLocations = new List<GpsLocation>()
    {
      new GpsLocation(31.23m, 121.47m),
      new GpsLocation(18.96m, 72.82m),
      new GpsLocation(24.86m, 67.01m),
      new GpsLocation(-34.61m, -58.37m),
      new GpsLocation(31.231m, 121.47m),
      new GpsLocation(18.961m, 72.82m),
      new GpsLocation(18.961m, 72.82m),
    };


    /// <summary>Test identities extra data information.</summary>
    public static List<string> ProfileExtraData = new List<string>()
    {
      null,
      "t=running,Cycling,ice hockey,water polo",
      "l=Karachi,PK;a=iop://185f8db32271fe25f561a6fc938b2e264306ec304eda518007d1764826381969;t=traveling,cycling,running",
      null,
      "running",
      "MTg1ZjhkYjMyMjcxZmUyNWY1NjFhNmZjOTM4YjJlMjY0MzA2ZWMzMDRlZGE1MTgwMDdkMTc2NDgyNjM4MTk2OQ==",
      "t=running;l=Mumbai,IN",
    };


    /// <summary>Test identities profile image file names.</summary>
    public static List<string> ProfileImages = new List<string>()
    {
      Path.Combine("images", "PS08003.jpg"),
      Path.Combine("images", "PS08003.jpg"),
      null,
      Path.Combine("images", "PS08003.jpg"),
      null,
      Path.Combine("images", "PS08003.jpg"),
      null
    };

    /// <summary>Test identities public keys.</summary>
    public static List<byte[]> ProfilePublicKeys;

    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress ServerIp = (IPAddress)ArgumentValues["Server IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1})", ServerIp, PrimaryPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListServerPorts(rolePorts);
        client.CloseConnection();


        ProfilePublicKeys = new List<byte[]>();

        bool profileInitializationOk = true;
        for (int i = 0; i < ProfileNames.Count; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          ProfilePublicKeys.Add(profileClient.GetIdentityKeys().PublicKey);

          await profileClient.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
          bool establishHostingOk = await profileClient.EstablishHostingAsync(ProfileTypes[i]);
          profileClient.CloseConnection();


          await profileClient.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
          bool checkInOk = await profileClient.CheckInAsync();

          byte[] imageData = ProfileImages[i] != null ? File.ReadAllBytes(ProfileImages[i]) : null;
          bool initializeProfileOk = await profileClient.InitializeProfileAsync(ProfileNames[i], imageData, ProfileLocations[i], ProfileExtraData[i]);

          profileInitializationOk = establishHostingOk && checkInOk && initializeProfileOk;
          profileClient.Dispose();

          if (!profileInitializationOk) break;
        }

        bool step1Ok = listPortsOk && profileInitializationOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client.VerifyIdentityAsync();

        // Start neighborhood initialization process.
        Message requestMessage = mb.CreateStartNeighborhoodInitializationRequest(1, 1, ServerIp);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool startNeighborhoodInitializationOk = idOk && statusOk;

        // Wait for update request.
        Message serverRequestMessage = await client.ReceiveMessageAsync();
        bool typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

        bool listMatch = CheckProfileList(serverRequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items);
        bool startNeighborhoodInitializationResponseOk = typeOk && listMatch;

        Message clientResponseMessage = mb.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
        await client.SendMessageAsync(clientResponseMessage);


        // Wait for finish request.
        serverRequestMessage = await client.ReceiveMessageAsync();
        typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

        bool finishNeighborhoodInitializationResponseOk = typeOk;

        clientResponseMessage = mb.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client.SendMessageAsync(clientResponseMessage);

        // Step 2 Acceptance
        bool step2Ok = verifyIdentityOk && startNeighborhoodInitializationOk && startNeighborhoodInitializationResponseOk && finishNeighborhoodInitializationResponseOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Verifies contents of list of profiles sent by the profile server in an update message.
    /// </summary>
    /// <param name="ProfileList">Profile list returned by the profile server.</param>
    /// <returns>true if the <paramref name="ProfileList"/> contains all existing test profiles.</returns>
    public bool CheckProfileList(IEnumerable<SharedProfileUpdateItem> ProfileList)
    {
      log.Trace("()");
      bool error = false;
      bool[] profilesOk = new bool[ProfileNames.Count];
      foreach (SharedProfileUpdateItem updateItem in ProfileList)
      {
        if (updateItem.ActionTypeCase != SharedProfileUpdateItem.ActionTypeOneofCase.Add)
        {
          log.Trace("Invalid update item action type '{0}' detected.", updateItem.ActionTypeCase);
          error = true;
          break;
        }


        SharedProfileAddItem addItem = updateItem.Add;
        byte[] pubKey = addItem.IdentityPublicKey.ToByteArray();

        int profileIndex = -1;
        for (int i = 0; i < ProfilePublicKeys.Count; i++)
        {
          if (StructuralComparisons.StructuralComparer.Compare(ProfilePublicKeys[i], pubKey) == 0)
          {
            profileIndex = i;
            break;
          }
        }

        if (profileIndex != -1)
        {
          bool piVersionOk = new SemVer(addItem.Version).Equals(SemVer.V100);
          bool piTypeOk = addItem.Type == ProfileTypes[profileIndex];
          bool piNameOk = addItem.Name == ProfileNames[profileIndex];
          bool piLatitudeOk = addItem.Latitude == ProfileLocations[profileIndex].GetLocationTypeLatitude();
          bool piLongitudeOk = addItem.Longitude == ProfileLocations[profileIndex].GetLocationTypeLongitude();
          bool piExtraDataOk = (string.IsNullOrEmpty(addItem.ExtraData) && string.IsNullOrEmpty(ProfileExtraData[profileIndex])) || (addItem.ExtraData == ProfileExtraData[profileIndex]);

          bool piImageOk = true;
          piImageOk = ProfileImages[profileIndex] != null ? addItem.SetThumbnailImage && (addItem.ThumbnailImage.Length > 0) : !addItem.SetThumbnailImage && (addItem.ThumbnailImage.Length == 0);

          bool profileOk = piVersionOk && piTypeOk && piNameOk && piLatitudeOk && piLongitudeOk && piExtraDataOk && piImageOk;
          if (!profileOk)
          {
            log.Trace("Profile index {0} is corrupted.", profileIndex + 1);
            error = true;
            break;
          }

          profilesOk[profileIndex] = true;
        }
        else
        {
          log.Trace("Profile pub key {0} not recognized.", Crypto.ToHex(pubKey));
          error = true;
          break;
        }
      }

      for (int index = 0; index < ProfileNames.Count; index++)
      {
        if (!profilesOk[index])
        {
          log.Trace("Profile index {0} not retrieved.", index + 1);
          error = true;
          break;
        }
      }

      bool res = !error;
      log.Trace("(-):{0}", res);
      return res;
    }

  }
}
