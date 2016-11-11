using Google.Protobuf;
using HomeNetCrypto;
using HomeNetProtocol;
using Iop.Homenode;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// HN06001 - Profile Search - Simple Search 1
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn06001---profile-search---simple-search-1
  /// </summary>
  public class HN06001 : ProtocolTest
  {
    public const string TestName = "HN06001";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Test identities profile types.</summary>
    private static List<string> profileTypes = new List<string>()
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
    private static List<string> profileNames = new List<string>()
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
    private static List<GpsLocation> profileLocations = new List<GpsLocation>()
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
    private static List<string> profileExtraData = new List<string>()
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
    private static List<string> profileImages = new List<string>()
    {
      "images/HN06001.jpg",
      "images/HN06001.jpg",
      null,
      "images/HN06001.jpg",
      null,
      "images/HN06001.jpg",
      null      
    };



    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress NodeIp = (IPAddress)ArgumentValues["Node IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      log.Trace("(NodeIp:'{0}',PrimaryPort:{1})", NodeIp, PrimaryPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(NodeIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListNodePorts(rolePorts);
        client.CloseConnection();

        List<byte[]> profilePublicKeys = new List<byte[]>();

        bool profileInitializationOk = true;
        for (int i = 0; i < profileNames.Count; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profilePublicKeys.Add(profileClient.GetIdentityKeys().PublicKey);

          await profileClient.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
          bool establishHomeNodeOk = await profileClient.EstablishHomeNodeAsync(profileTypes[i]);
          profileClient.CloseConnection();


          await profileClient.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
          bool checkInOk = await profileClient.CheckInAsync();

          byte[] imageData = profileImages[i] != null ? File.ReadAllBytes(profileImages[i]) : null;
          bool initializeProfileOk = await profileClient.InitializeProfileAsync(profileNames[i], imageData, profileLocations[i], profileExtraData[i]);

          profileInitializationOk = establishHomeNodeOk && checkInOk && initializeProfileOk;
          profileClient.Dispose();

          if (!profileInitializationOk) break;
        }

        bool step1Ok = listPortsOk && profileInitializationOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        Message requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;


        bool totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == profileNames.Count;
        bool maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        bool profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == profileNames.Count;

        bool error = false;
        bool[] profilesOk = new bool[profileNames.Count];
        foreach (IdentityNetworkProfileInformation profileInfo in responseMessage.Response.ConversationResponse.ProfileSearch.Profiles)
        {
          byte[] pubKey = profileInfo.IdentityPublicKey.ToByteArray();

          int profileIndex = -1;
          for (int i = 0; i < profilePublicKeys.Count; i++)
          {
            if (StructuralComparisons.StructuralComparer.Compare(profilePublicKeys[i], pubKey) == 0)
            {
              profileIndex = i;
              break;
            }
          }


          if (profileIndex != -1)
          {
            bool piIsHostedOk = profileInfo.IsHosted == true;
            bool piIsOnlineOk = profileInfo.IsOnline == false;
            bool piTypeOk = profileInfo.Type == profileTypes[profileIndex];
            bool piNameOk = profileInfo.Name == profileNames[profileIndex];
            bool piLatitudeOk = profileInfo.Latitude == profileLocations[profileIndex].GetLocationTypeLatitude();
            bool piLongitudeOk = profileInfo.Longitude == profileLocations[profileIndex].GetLocationTypeLongitude();
            bool piExtraDataOk = (string.IsNullOrEmpty(profileInfo.ExtraData) && string.IsNullOrEmpty(profileExtraData[profileIndex])) || (profileInfo.ExtraData == profileExtraData[profileIndex]);
            bool piImageOk = profileImages[profileIndex] != null ? profileInfo.ThumbnailImage.Length > 0 : profileInfo.ThumbnailImage.Length == 0;

            bool profileOk = piIsHostedOk && piIsOnlineOk && piTypeOk && piNameOk && piLatitudeOk && piLongitudeOk && piExtraDataOk && piImageOk;
            if (!profileOk)
            {
              log.Trace("Profile index {0} is corrupted.", profileIndex);
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

        for (int i = 0; i < profilesOk.Length; i++)
        {
          if (!profilesOk[i])
          {
            log.Trace("Profile index {0} not retrieved.", i);
            error = true;
            break;
          }
        }

        // Step 2 Acceptance
        bool step2Ok = startConversationOk && idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && !error;

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
  }
}
