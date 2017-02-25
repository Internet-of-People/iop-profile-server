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
  /// PS08023 - New Neighbor - Invalid Requests
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08023---new-neighbor---invalid-requests
  /// </summary>
  public class PS08023 : ProtocolTest
  {
    public const string TestName = "PS08023";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("Base Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("LOC Port", ProtocolTestArgumentType.Port),
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
      Path.Combine("images", "PS08023.png"),
      Path.Combine("images", "PS08023.png"),
      null,
      Path.Combine("images", "PS08023.png"),
      null,
      Path.Combine("images", "PS08023.png"),
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
      int BasePort = (int)ArgumentValues["Base Port"];
      int LocPort = (int)ArgumentValues["LOC Port"];
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1},BasePort:{2},LocPort:{3})", ServerIp, PrimaryPort, BasePort, LocPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      ProfileServer profileServer = null;
      LocServer locServer = null;
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

        // Create identities.
        ProfilePublicKeys = new List<byte[]>();
        for (int i = 0; i < ProfileNames.Count; i++)
        {
          ProtocolClient protocolClient = new ProtocolClient();
          ProfilePublicKeys.Add(protocolClient.GetIdentityKeys().PublicKey);
          protocolClient.Dispose();
        }

        
        // Start simulated profile server.
        profileServer = new ProfileServer("TestProfileServer", ServerIp, BasePort, client.GetIdentityKeys(), new GpsLocation(1, 2));
        bool profileServerStartOk = profileServer.Start();

        // Start simulated LOC server.
        locServer = new LocServer("TestLocServer", ServerIp, LocPort);
        bool locServerStartOk = locServer.Start();

        await locServer.WaitForProfileServerConnectionAsync();

        bool step1Ok = profileServerStartOk && locServerStartOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");


        // Step 2
        log.Trace("Step 2");

        // Initialize the original set of update messages update.
        List<SharedProfileUpdateItem> originalUpdateItems = new List<SharedProfileUpdateItem>();
        for (int i = 0; i < ProfileNames.Count; i++)
        {
          SharedProfileUpdateItem updateItem = new SharedProfileUpdateItem()
          {
            Add = new SharedProfileAddItem()
            {
              Version = SemVer.V100.ToByteString(),
              Name = ProfileNames[i],
              Type = ProfileTypes[i],
              ExtraData = ProfileExtraData[i] != null ? ProfileExtraData[i] : "",
              Latitude = ProfileLocations[i].GetLocationTypeLatitude(),
              Longitude = ProfileLocations[i].GetLocationTypeLongitude(),
              IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(ProfilePublicKeys[i]),
              SetThumbnailImage = ProfileImages[i] != null,
              ThumbnailImage = ProtocolHelper.ByteArrayToByteString(ProfileImages[i] != null ? File.ReadAllBytes(ProfileImages[i]) : new byte[0])
            }
          };

          originalUpdateItems.Add(updateItem);
        }


        List<SharedProfileUpdateItem> updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Version = ProtocolHelper.ByteArrayToByteString(new byte[] { 1, 0 });

        bool initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.version");

        bool step2Ok = initOk;
        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Version = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 0, 0 });

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.version");

        bool step3Ok = initOk;
        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        // Step 4
        log.Trace("Step 4");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(Encoding.UTF8.GetBytes(new string ('a', 300)));

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.identityPublicKey");

        bool step4Ok = initOk;
        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");


        // Step 5
        log.Trace("Step 5");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.IdentityPublicKey = updateItems[0].Add.IdentityPublicKey;

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.identityPublicKey");

        bool step5Ok = initOk;
        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");


        // Step 6
        log.Trace("Step 6");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Name = new string('a', 70);

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.name");

        bool step6Ok = initOk;
        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Name = new string('ɐ', 50);

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.name");

        bool step7Ok = initOk;
        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        // Step 8
        log.Trace("Step 8");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Type = new string('a', 70);

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.type");

        bool step8Ok = initOk;
        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");


        // Step 9
        log.Trace("Step 9");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Type = new string('ɐ', 50);

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.type");

        bool step9Ok = initOk;
        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");


        // Step 10
        log.Trace("Step 10");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Type = "";

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.type");

        bool step10Ok = initOk;
        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");


        // Step 11
        log.Trace("Step 11");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.SetThumbnailImage = true;
        updateItems[2].Add.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[0]);

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.thumbnailImage");

        bool step11Ok = initOk;
        log.Trace("Step 11: {0}", step11Ok ? "PASSED" : "FAILED");


        // Step 12
        log.Trace("Step 12");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.SetThumbnailImage = true;
        updateItems[2].Add.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 1, 2 });

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.thumbnailImage");

        bool step12Ok = initOk;
        log.Trace("Step 12: {0}", step12Ok ? "PASSED" : "FAILED");


        // Step 13
        log.Trace("Step 13");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Latitude = 987654321;

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.latitude");

        bool step13Ok = initOk;
        log.Trace("Step 13: {0}", step13Ok ? "PASSED" : "FAILED");


        // Step 14
        log.Trace("Step 14");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Longitude = 987654321;

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.longitude");

        bool step14Ok = initOk;
        log.Trace("Step 14: {0}", step14Ok ? "PASSED" : "FAILED");


        // Step 15
        log.Trace("Step 15");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.ExtraData = new string('a', 270);

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.extraData");

        bool step15Ok = initOk;
        log.Trace("Step 15: {0}", step15Ok ? "PASSED" : "FAILED");


        // Step 16
        log.Trace("Step 16");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.ExtraData = new string('ɐ', 150);

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.extraData");

        bool step16Ok = initOk;
        log.Trace("Step 16: {0}", step16Ok ? "PASSED" : "FAILED");


        // Step 17
        log.Trace("Step 17");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(updateItems[0].Add.IdentityPublicKey.ToByteArray()))
          }
        };

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.actionType");

        bool step17Ok = initOk;
        log.Trace("Step 17: {0}", step17Ok ? "PASSED" : "FAILED");


        // Step 18
        log.Trace("Step 18");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem()
        {
          Delete = new SharedProfileDeleteItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(updateItems[0].Add.IdentityPublicKey.ToByteArray()))
          }
        };

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.actionType");

        bool step18Ok = initOk;
        log.Trace("Step 18: {0}", step18Ok ? "PASSED" : "FAILED");


        // Step 19
        log.Trace("Step 19");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[2] = new SharedProfileUpdateItem(updateItems[2]);
        updateItems[2].Add.Name = "";

        initOk = await PerformInitializationProcessWithUpdateItemsAsync(updateItems, profileServer, locServer, "2.add.name");

        bool step19Ok = initOk;
        log.Trace("Step 19: {0}", step19Ok ? "PASSED" : "FAILED");



        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok && step10Ok
          && step11Ok && step12Ok && step13Ok && step14Ok && step15Ok && step16Ok && step17Ok && step18Ok && step19Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();

      if (profileServer != null) profileServer.Shutdown();
      if (locServer != null) locServer.Shutdown();

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs a single round of initialization process with the target profile server.
    /// </summary>
    /// <param name="UpdateItems">Update items to send during the initialization process to the target profile server.</param>
    /// <param name="ProfileServer">Simulated profile server instance.</param>
    /// <param name="LocServer">Simulated LOC server instance.</param>
    /// <param name="ErrorDetails">Expected error details returned as a response to update request.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> PerformInitializationProcessWithUpdateItemsAsync(List<SharedProfileUpdateItem> UpdateItems, ProfileServer ProfileServer, LocServer LocServer, string ErrorDetails)
    {
      log.Trace("()");

      // Announce new neighbor.
      Iop.Locnet.NeighbourhoodChange change = new Iop.Locnet.NeighbourhoodChange()
      {
        AddedNodeInfo = ProfileServer.GetNodeInfo(LocServer.Port)
      };

      bool addNeighborOk = await LocServer.SendChangeNotification(change);

      // Wait for start of neighborhood initialization process.
      IncomingServerMessage incomingServerMessage = await ProfileServer.WaitForConversationRequest(ServerRole.ServerNeighbor, ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization);

      Message requestMessage = await ProfileServer.SendNeighborhoodSharedProfileUpdateRequest(incomingServerMessage.Client, UpdateItems);
      incomingServerMessage = await ProfileServer.WaitForResponse(ServerRole.ServerNeighbor, requestMessage);

      bool statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.ErrorInvalidValue;
      bool detailsOk = incomingServerMessage.IncomingMessage.Response.Details == ErrorDetails;
      bool updateOk = statusOk && detailsOk;

      // Announce new neighbor.
      change = new Iop.Locnet.NeighbourhoodChange()
      {
        RemovedNodeId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(ProfileServer.Keys.PublicKey))
      };

      bool deleteNeighborOk = await LocServer.SendChangeNotification(change);

      bool res = addNeighborOk && updateOk && deleteNeighborOk;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
