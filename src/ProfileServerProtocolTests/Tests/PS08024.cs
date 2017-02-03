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
  /// PS08024 - Neighborhood Update - Invalid Requests
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08024---neighborhood-update---invalid-requests
  /// </summary>
  public class PS08024 : ProtocolTest
  {
    public const string TestName = "PS08024";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

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
      Path.Combine("images", "PS08024.png"),
      Path.Combine("images", "PS08024.png"),
      null,
      Path.Combine("images", "PS08024.png"),
      null,
      Path.Combine("images", "PS08024.png"),
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
        List<SharedProfileUpdateItem> originalAddUpdateItems = new List<SharedProfileUpdateItem>();
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

          originalAddUpdateItems.Add(updateItem);
        }


        
        // Neighborhood initialization process.

        // Announce new neighbor.
        Iop.Locnet.NeighbourhoodChange change = new Iop.Locnet.NeighbourhoodChange()
        {
          AddedNodeInfo = profileServer.GetNodeInfo()
        };

        bool addNeighborOk = await locServer.SendChangeNotification(change);


        // Wait for start of neighborhood initialization process.
        IncomingServerMessage incomingServerMessage = await profileServer.WaitForConversationRequest(ServerRole.ServerNeighbor, ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization);

        List<SharedProfileUpdateItem> updateItems = new List<SharedProfileUpdateItem>();
        updateItems.Add(originalAddUpdateItems[0]);
        updateItems.Add(originalAddUpdateItems[1]);
        updateItems.Add(originalAddUpdateItems[5]);
        updateItems.Add(originalAddUpdateItems[6]);

        SharedProfileUpdateItem changeItem0 = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(originalAddUpdateItems[0].Add.IdentityPublicKey.ToByteArray())),
          }
        };

        SharedProfileUpdateItem changeItem1 = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(originalAddUpdateItems[1].Add.IdentityPublicKey.ToByteArray())),
            SetName = true,
            Name = "X"
          }
        };

        List<SharedProfileUpdateItem> originalUpdateItems = new List<SharedProfileUpdateItem>();
        originalUpdateItems.Add(changeItem1);
        originalUpdateItems.Add(originalAddUpdateItems[2]);
        originalUpdateItems.Add(originalAddUpdateItems[3]);
        originalUpdateItems.Add(originalAddUpdateItems[4]);

        Message requestMessage = await profileServer.SendNeighborhoodSharedProfileUpdateRequest(incomingServerMessage.Client, updateItems);
        incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, requestMessage);

        bool statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.Ok;
        bool updateOk = statusOk;


        // Finish neighborhood initialization process.
        Message finishRequest = await profileServer.SendFinishNeighborhoodInitializationRequest(incomingServerMessage.Client);

        incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, finishRequest);
        statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.Ok;
        bool finishOk = (finishRequest != null) && statusOk;

        bool step2Ok = addNeighborOk && updateOk && finishOk;
        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client.VerifyIdentityAsync();

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Version = ProtocolHelper.ByteArrayToByteString(new byte[] { 1, 0 });

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.version");

        bool step3Ok = verifyIdentityOk && updateOk;
        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        // Step 4
        log.Trace("Step 4");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Version = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 0, 0 });

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.version");

        bool step4Ok = verifyIdentityOk && updateOk;
        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");


        // Step 5
        log.Trace("Step 5");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(Encoding.UTF8.GetBytes(new string ('a', 300)));

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.identityPublicKey");

        bool step5Ok = updateOk;
        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");


        // Step 6
        log.Trace("Step 6");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.IdentityPublicKey = originalAddUpdateItems[0].Add.IdentityPublicKey;

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.identityPublicKey");

        bool step6Ok = updateOk;
        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Name = new string('a', 70);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.name");

        bool step7Ok = updateOk;
        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        // Step 8
        log.Trace("Step 8");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Name = new string('ɐ', 50);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.name");

        bool step8Ok = updateOk;
        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");


        // Step 9
        log.Trace("Step 9");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Type = new string('a', 70);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.type");

        bool step9Ok = updateOk;
        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");


        // Step 10
        log.Trace("Step 10");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Type = new string('ɐ', 50);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.type");

        bool step10Ok = updateOk;
        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");


        // Step 11
        log.Trace("Step 11");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Type = "";

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.type");

        bool step11Ok = updateOk;
        log.Trace("Step 11: {0}", step11Ok ? "PASSED" : "FAILED");


        // Step 12
        log.Trace("Step 12");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.SetThumbnailImage = true;
        updateItems[1].Add.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[0]);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.thumbnailImage");

        bool step12Ok = updateOk;
        log.Trace("Step 12: {0}", step12Ok ? "PASSED" : "FAILED");


        // Step 13
        log.Trace("Step 13");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.SetThumbnailImage = true;
        updateItems[1].Add.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 1, 2 });

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.thumbnailImage");

        bool step13Ok = updateOk;
        log.Trace("Step 13: {0}", step13Ok ? "PASSED" : "FAILED");


        // Step 14
        log.Trace("Step 14");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Latitude = 987654321;

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.latitude");

        bool step14Ok = updateOk;
        log.Trace("Step 14: {0}", step14Ok ? "PASSED" : "FAILED");


        // Step 15
        log.Trace("Step 15");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Longitude = 987654321;

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.longitude");

        bool step15Ok = updateOk;
        log.Trace("Step 15: {0}", step15Ok ? "PASSED" : "FAILED");


        // Step 16
        log.Trace("Step 16");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.ExtraData = new string('a', 270);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.extraData");

        bool step16Ok = updateOk;
        log.Trace("Step 16: {0}", step16Ok ? "PASSED" : "FAILED");


        // Step 17
        log.Trace("Step 17");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.ExtraData = new string('ɐ', 150);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.extraData");

        bool step17Ok = updateOk;
        log.Trace("Step 17: {0}", step17Ok ? "PASSED" : "FAILED");


        // Step 18
        log.Trace("Step 18");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(originalAddUpdateItems[0].Add.IdentityPublicKey.ToByteArray())),
          }
        };

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.set*");

        bool step18Ok = updateOk;
        log.Trace("Step 18: {0}", step18Ok ? "PASSED" : "FAILED");



        // Step 19
        log.Trace("Step 19");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 1, 2 }),
            SetName = true,
            Name = "X"
          }
        };

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.identityNetworkId");

        bool step19Ok = updateOk;
        log.Trace("Step 19: {0}", step19Ok ? "PASSED" : "FAILED");


        // Step 20
        log.Trace("Step 20");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[0] = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(originalAddUpdateItems[0].Add.IdentityPublicKey.ToByteArray())),
            SetName = true,
            Name = "X"
          }
        };
        updateItems[1] = new SharedProfileUpdateItem()
        {
          Delete = new SharedProfileDeleteItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(originalAddUpdateItems[0].Add.IdentityPublicKey.ToByteArray())),
          }
        };

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.delete.identityNetworkId");

        bool step20Ok = updateOk;
        log.Trace("Step 20: {0}", step20Ok ? "PASSED" : "FAILED");


        // Step 21
        log.Trace("Step 21");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[0] = new SharedProfileUpdateItem(originalAddUpdateItems[2]);
        updateItems[1] = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(originalAddUpdateItems[2].Add.IdentityPublicKey.ToByteArray())),
            SetName = true,
            Name = "X"
          }
        };

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.identityNetworkId");

        bool step21Ok = updateOk;
        log.Trace("Step 21: {0}", step21Ok ? "PASSED" : "FAILED");


        // Step 22
        log.Trace("Step 22");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[0] = new SharedProfileUpdateItem()
        {
          Change = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(Crypto.Sha256(originalAddUpdateItems[2].Add.IdentityPublicKey.ToByteArray())),
            SetName = true,
            Name = "X"
          }
        };
        updateItems[1] = new SharedProfileUpdateItem(originalAddUpdateItems[2]);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.identityPublicKey");

        bool step22Ok = updateOk;
        log.Trace("Step 22: {0}", step22Ok ? "PASSED" : "FAILED");


        // Step 23
        log.Trace("Step 23");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetVersion = true;
        updateItems[1].Change.Version = ProtocolHelper.ByteArrayToByteString(new byte[] { 1, 0 });

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.version");

        bool step23Ok = updateOk;
        log.Trace("Step 23: {0}", step23Ok ? "PASSED" : "FAILED");


        // Step 24
        log.Trace("Step 24");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetVersion = true;
        updateItems[1].Change.Version = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 0, 0 });

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.version");

        bool step24Ok = updateOk;
        log.Trace("Step 24: {0}", step24Ok ? "PASSED" : "FAILED");


        // Step 25
        log.Trace("Step 25");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetName = true;
        updateItems[1].Change.Name = new string('a', 70);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.name");

        bool step25Ok = updateOk;
        log.Trace("Step 25: {0}", step25Ok ? "PASSED" : "FAILED");


        // Step 26
        log.Trace("Step 26");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetName = true;
        updateItems[1].Change.Name = new string('ɐ', 50);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.name");

        bool step26Ok = updateOk;
        log.Trace("Step 26: {0}", step26Ok ? "PASSED" : "FAILED");


        // Step 27
        log.Trace("Step 27");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetThumbnailImage = true;
        updateItems[1].Change.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(Encoding.UTF8.GetBytes(new string((char)0x40, 6000)));

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.thumbnailImage");

        bool step27Ok = updateOk;
        log.Trace("Step 27: {0}", step27Ok ? "PASSED" : "FAILED");


        // Step 28
        log.Trace("Step 28");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetThumbnailImage = true;
        updateItems[1].Change.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 1, 2 });

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.thumbnailImage");

        bool step28Ok = updateOk;
        log.Trace("Step 28: {0}", step28Ok ? "PASSED" : "FAILED");


        // Step 29
        log.Trace("Step 29");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetLocation = true;
        updateItems[1].Change.Latitude = 987654321;
        updateItems[1].Change.Longitude = 0;

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.latitude");

        bool step29Ok = updateOk;
        log.Trace("Step 29: {0}", step29Ok ? "PASSED" : "FAILED");


        // Step 30
        log.Trace("Step 30");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetLocation = true;
        updateItems[1].Change.Latitude = 0;
        updateItems[1].Change.Longitude = 987654321;

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.longitude");

        bool step30Ok = updateOk;
        log.Trace("Step 30: {0}", step30Ok ? "PASSED" : "FAILED");


        // Step 31
        log.Trace("Step 31");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetExtraData = true;
        updateItems[1].Change.ExtraData = new string('a', 270);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.extraData");

        bool step31Ok = updateOk;
        log.Trace("Step 31: {0}", step31Ok ? "PASSED" : "FAILED");


        // Step 32
        log.Trace("Step 32");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetExtraData = true;
        updateItems[1].Change.ExtraData = new string('ɐ', 150);

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.extraData");

        bool step32Ok = updateOk;
        log.Trace("Step 32: {0}", step32Ok ? "PASSED" : "FAILED");


        // Step 33
        log.Trace("Step 33");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem()
        {
          Delete = new SharedProfileDeleteItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(new byte[] { 0, 1, 2 })
          }
        };

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.delete.identityNetworkId");

        bool step33Ok = updateOk;
        log.Trace("Step 33: {0}", step33Ok ? "PASSED" : "FAILED");


        // Step 34
        log.Trace("Step 34");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(updateItems[1]);
        updateItems[1].Add.Name = "";

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.add.name");

        bool step34Ok = updateOk;
        log.Trace("Step 34: {0}", step34Ok ? "PASSED" : "FAILED");


        // Step 35
        log.Trace("Step 35");

        updateItems = new List<SharedProfileUpdateItem>(originalUpdateItems);
        updateItems[1] = new SharedProfileUpdateItem(changeItem0);
        updateItems[1].Change.SetName = true;
        updateItems[1].Change.Name = "";

        updateOk = await PerformNeighborhoodUpdateAsync(updateItems, client, "1.change.name");

        bool step35Ok = updateOk;
        log.Trace("Step 35: {0}", step35Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok && step10Ok
          && step11Ok && step12Ok && step13Ok && step14Ok && step15Ok && step16Ok && step17Ok && step18Ok && step19Ok && step20Ok
          && step21Ok && step22Ok && step23Ok && step24Ok && step25Ok && step26Ok && step27Ok && step28Ok && step29Ok && step30Ok
          && step31Ok && step32Ok && step33Ok && step34Ok && step35Ok;

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
    /// Sends neighborhood update to the target profile server.
    /// </summary>
    /// <param name="UpdateItems">Update items to send as an update to the target profile server.</param>
    /// <param name="Client">Test client representing its primary identity.</param>
    /// <param name="ErrorDetails">Expected error details returned as a response to update request.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> PerformNeighborhoodUpdateAsync(List<SharedProfileUpdateItem> UpdateItems, ProtocolClient Client, string ErrorDetails)
    {
      log.Trace("()");

      Message requestMessage = Client.MessageBuilder.CreateNeighborhoodSharedProfileUpdateRequest(UpdateItems);
      await Client.SendMessageAsync(requestMessage);

      Message responseMessage = await Client.ReceiveMessageAsync();
      bool idOk = requestMessage.Id == responseMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
      bool detailsOk = responseMessage.Response.Details == ErrorDetails;

      bool res = idOk && statusOk && detailsOk;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
