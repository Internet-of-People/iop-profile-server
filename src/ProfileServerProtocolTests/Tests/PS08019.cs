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
  /// PS08019 - New Neighbor - Small Set
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08019---new-neighbor---small-set
  /// </summary>
  public class PS08019 : ProtocolTest
  {
    public const string TestName = "PS08019";
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
      Path.Combine("images", "PS08019.png"),
      Path.Combine("images", "PS08019.png"),
      null,
      Path.Combine("images", "PS08019.png"),
      null,
      Path.Combine("images", "PS08019.png"),
      null
    };

    /// <summary>Generated test profiles mapped by their name.</summary>
    public static Dictionary<string, ProtocolClient> TestProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);



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
        for (int i = 0; i < ProfileNames.Count; i++)
        {
          byte[] imageData = ProfileImages[i] != null ? File.ReadAllBytes(ProfileImages[i]) : null;

          ProtocolClient profileClient = new ProtocolClient();
          profileClient.Profile = new ClientProfile()
          {
            Version = SemVer.V100,
            Name = ProfileNames[i],
            Type = ProfileTypes[i],
            ProfileImage = imageData,
            ThumbnailImage = imageData,
            Location = ProfileLocations[i],
            ExtraData = ProfileExtraData[i],
            PublicKey = profileClient.GetIdentityKeys().PublicKey
          };
          TestProfiles.Add(profileClient.Profile.Name, profileClient);
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

        // Announce new neighbor.
        Iop.Locnet.NeighbourhoodChange change = new Iop.Locnet.NeighbourhoodChange()
        {
          AddedNodeInfo = profileServer.GetNodeInfo()
        };
        
        bool changeNotificationOk = await locServer.SendChangeNotification(change);

        // Wait for start of neighborhood initialization process.
        IncomingServerMessage incomingServerMessage = await profileServer.WaitForConversationRequest(ServerRole.ServerNeighbor, ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization);


        // Send update.
        List<SharedProfileUpdateItem> updateItems = new List<SharedProfileUpdateItem>();
        foreach (ProtocolClient pc in TestProfiles.Values)
          updateItems.Add(pc.GetSharedProfileUpdateAddItem());
        
        Message updateRequest = await profileServer.SendNeighborhoodSharedProfileUpdateRequest(incomingServerMessage.Client, updateItems);

        incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, updateRequest);
        bool statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.Ok;
        bool updateOk = (updateRequest != null) && statusOk;


        // Finish neighborhood initialization process.
        Message finishRequest = await profileServer.SendFinishNeighborhoodInitializationRequest(incomingServerMessage.Client);

        incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, finishRequest);
        statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.Ok;
        bool finishOk = (finishRequest != null) && statusOk;


        bool step2Ok = changeNotificationOk && updateOk && finishOk;
        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();
        Message requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 100, 100, false, true);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        HashSet<byte[]> expectedCoveredServers = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default) { client.GetIdentityId(), Crypto.Sha256(client.ServerKey) };
        HashSet<byte[]> realCoveredServers = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);
        foreach (ByteString csId in responseMessage.Response.ConversationResponse.ProfileSearch.CoveredServers)
          realCoveredServers.Add(csId.ToByteArray());
        bool coveredServersOk = expectedCoveredServers.SetEquals(realCoveredServers);

        bool profileListOk = client.CheckProfileListMatchSearchResultItems(TestProfiles, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.ToList(), false, false, client.GetIdentityId(), true);

        // Step 3 Acceptance
        bool step3Ok = startConversationOk&& idOk && statusOk && profileListOk && coveredServersOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();

      foreach (ProtocolClient protocolClient in TestProfiles.Values)
        protocolClient.Dispose();

      if (profileServer != null) profileServer.Shutdown();
      if (locServer != null) locServer.Shutdown();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
