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
  /// PS08020 - New Neighbor - Large Set
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08020---new-neighbor---large-set
  /// </summary>
  public class PS08020 : ProtocolTest
  {
    public const string TestName = "PS08020";
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


    /// <summary>Total number of locations used in the test.</summary>
    public const int LocationCount = 20;

    /// <summary>Number of identities to generate around each location and radius.</summary>
    public const int RadiusIdentityCount = 10;

    /// <summary>Generated GPS locations.</summary>
    public static List<GpsLocation> GeneratedLocations = new List<GpsLocation>();

    /// <summary>Predefined points near which some generated locations will be created.</summary>
    public static List<GpsLocation> PredefinedLocations = new List<GpsLocation>()
    {
      new GpsLocation(  0.0m,    0.0m),
      new GpsLocation( 89.9m,    0.0m),
      new GpsLocation(-89.9m,    0.0m),
      new GpsLocation( 40.0m,    0.0m),
      new GpsLocation(  0.0m,  179.9m),
      new GpsLocation( 89.9m,  179.9m),
      new GpsLocation(-89.9m,  179.9m),
      new GpsLocation( 40.0m,  179.9m),
      new GpsLocation(  0.0m, -179.9m),
      new GpsLocation( 89.9m, -179.9m),
      new GpsLocation(-89.9m, -179.9m),
      new GpsLocation( 40.0m, -179.9m),
      new GpsLocation(  0.0m,   50.0m),
      new GpsLocation( 89.9m,   50.0m),
      new GpsLocation(-89.9m,   50.0m),
    };


    /// <summary>Smallest radius in metres.</summary>
    public static uint R1;

    /// <summary>Medium radius in metres.</summary>
    public static uint R2;

    /// <summary>Largest radius in metres.</summary>
    public static uint R3;

    /// <summary>Random number generator.</summary>
    public static Random Rng = new Random();


    /// <summary>Minimal value of R1 radius.</summary>
    public const int R1Min = 5000;

    /// <summary>Maximal ratio of R1/R2.</summary>
    public const double R1R2RatioMax = 0.8;

    /// <summary>Minimal value of R2 radius.</summary>
    public const int R2Min = 20000;

    /// <summary>Exclusive upper bound of R2 radius.</summary>
    public const int R2Max = 150000;

    /// <summary>Maximal ratio of R3/R2.</summary>
    public const double R3R2RatioMax = 1.5;

    /// <summary>Exclusive upper bound of R3 radius.</summary>
    public const int R3Max = 500000;

    /// <summary>Generated profile names.</summary>
    public static List<string> ProfileNames = new List<string>();


    /// <summary>Generated profile locations.</summary>
    public static List<GpsLocation> ProfileLocations = new List<GpsLocation>();


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


        // Radius generation.
        R2 = (uint)Rng.Next(R2Min, R2Max);
        R1 = (uint)Rng.Next(R1Min, (int)(R1R2RatioMax * R2));
        R3 = (uint)Rng.Next((int)(R3R2RatioMax * R2), R3Max);
        log.Trace("R1: {0,8} m", R1);
        log.Trace("R2: {0,8} m", R2);
        log.Trace("R3: {0,8} m", R3);

        // Location generation
        for (int i = 0; i < LocationCount; i++)
        {
          if (PredefinedLocations.Count > i)
          {
            GeneratedLocations.Add(GenerateLocation(PredefinedLocations[i], R2));
          }
          else
          {
            int lat = Rng.Next((int)(GpsLocation.LatitudeMin * GpsLocation.LocationTypeFactor), (int)(GpsLocation.LatitudeMax * GpsLocation.LocationTypeFactor) + 1);
            int lon = Rng.Next((int)(GpsLocation.LongitudeMin * GpsLocation.LocationTypeFactor) + 1, (int)(GpsLocation.LongitudeMax * GpsLocation.LocationTypeFactor) + 1);
            GeneratedLocations.Add(new GpsLocation(lat, lon));
          }
        }

        log.Trace("Generated locations:");
        for (int i = 0; i < LocationCount; i++)
          log.Trace(" #{0:00}: {1:US}", i, GeneratedLocations[i]);


        // Create identities.
        int profileNumber = 0;
        byte[] imageData = File.ReadAllBytes(Path.Combine("images", TestName + ".png"));

        uint[] rads = new uint[] { R1, R2, R3 };
        for (int locIndex = 0; locIndex < LocationCount; locIndex++)
        {
          for (uint radIndex = 0; radIndex < rads.Length; radIndex++)
          {
            for (int idIndex = 0; idIndex < RadiusIdentityCount; idIndex++)
            {
              GpsLocation basePoint = GeneratedLocations[locIndex];
              uint radius = rads[radIndex];
              GpsLocation location = GenerateLocation(basePoint, radius);
              ProfileLocations.Add(location);

              ProtocolClient profileClient = new ProtocolClient();
              profileClient.InitializeRandomProfile(profileNumber, imageData);
              profileNumber++;
              profileClient.Profile.Location = location;

              TestProfiles.Add(profileClient.Profile.Name, profileClient);
              ProfileNames.Add(profileClient.Profile.Name);
            }
          }
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
          AddedNodeInfo = profileServer.GetNodeInfo(LocPort)
        };
        
        bool changeNotificationOk = await locServer.SendChangeNotification(change);

        // Wait for start of neighborhood initialization process.
        IncomingServerMessage incomingServerMessage = await profileServer.WaitForConversationRequest(ServerRole.ServerNeighbor, ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization);


        // Send update.
        bool statusOk = false;
        bool updateOk = true;
        List<ProtocolClient> profilesToSend = new List<ProtocolClient>(TestProfiles.Values);
        while (profilesToSend.Count > 0)
        {
          int batchSize = Rng.Next(1, Math.Min(100, profilesToSend.Count) + 1);

          List<SharedProfileUpdateItem> updateItems = new List<SharedProfileUpdateItem>();
          foreach (ProtocolClient pc in profilesToSend.GetRange(0, batchSize))
            updateItems.Add(pc.GetSharedProfileUpdateAddItem());

          profilesToSend.RemoveRange(0, batchSize);

          Message updateRequest = await profileServer.SendNeighborhoodSharedProfileUpdateRequest(incomingServerMessage.Client, updateItems);
          incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, updateRequest);
          statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.Ok;
          bool batchOk = (updateRequest != null) && statusOk;
          if (!batchOk)
          {
            updateOk = false;
            break;
          }
        }


        // Finish neighborhood initialization process.
        Message finishRequest = await profileServer.SendFinishNeighborhoodInitializationRequest(incomingServerMessage.Client);

        incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, finishRequest);
        statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.Ok;
        bool finishOk = (finishRequest != null) && statusOk;


        bool step2Ok = changeNotificationOk && updateOk && finishOk;
        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");

        // Start conversation.
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        HashSet<byte[]> expectedCoveredServers = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default) { client.GetIdentityId(), Crypto.Sha256(client.ServerKey) };

        // Search all profiles.
        Message requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 1000, 1000, false, false);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        bool totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == ProfileNames.Count;
        bool maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 1000;
        bool profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == ProfileNames.Count;

        HashSet<byte[]> realCoveredServers = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);
        foreach (ByteString csId in responseMessage.Response.ConversationResponse.ProfileSearch.CoveredServers)
          realCoveredServers.Add(csId.ToByteArray());
        bool coveredServersOk = expectedCoveredServers.SetEquals(realCoveredServers);


        bool queryRespOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;
        bool resultsOk = client.CheckProfileListMatchSearchResultItems(TestProfiles, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.ToList(), false, false, client.GetIdentityId(), false);

        bool query1Ok = queryRespOk && resultsOk;

        bool queriesOk = true;
        // Search queries around target locations.
        for (int locIndex = 0; locIndex < LocationCount; locIndex++)
        {
          for (uint radIndex = 0; radIndex < rads.Length + 1; radIndex++)
          {
            uint radius = radIndex < rads.Length ? rads[radIndex] : (uint)Rng.Next(1000000, 10000000);
            GpsLocation targetLocation = GeneratedLocations[locIndex];
            requestMessage = mb.CreateProfileSearchRequest(null, null, null, targetLocation, radius, 1000, 1000, false, false);
            await client.SendMessageAsync(requestMessage);

            responseMessage = await client.ReceiveMessageAsync();
            idOk = responseMessage.Id == requestMessage.Id;
            statusOk = responseMessage.Response.Status == Status.Ok;

            Dictionary<string, ProtocolClient> expectedClients = GetClientsInLocation(targetLocation, radius);
            totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == expectedClients.Count;
            maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 1000;
            profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == expectedClients.Count;

            queryRespOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;
            resultsOk = client.CheckProfileListMatchSearchResultItems(expectedClients, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.ToList(), false, false, client.GetIdentityId(), false);

            queriesOk = queryRespOk && resultsOk;
            if (!queriesOk)
            {
              log.Trace("Search query location {0} with radius {1} should produce {2} profiles, but produced {3} profiles.", targetLocation, radius, expectedClients.Count, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count);
              log.Trace("Expected names list:");
              foreach (string name in expectedClients.Keys)
                log.Trace("  {0}", name);

              List<string> resultNames = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Select(r => r.Name).OrderBy(r => r).ToList();
              log.Trace("Query result names list:");
              foreach (string name in resultNames)
                log.Trace("  {0}", name);
              break;
            }

            log.Trace("Search query location {0} with radius {1} produced {2} correct profiles.", targetLocation, radius, expectedClients.Count);
          }

          if (!queriesOk) break;
        }

        // Step 3 Acceptance
        bool step3Ok = startConversationOk && query1Ok && queriesOk;

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


    /// <summary>
    /// Generates a random GPS location in a target area.
    /// </summary>
    /// <param name="BasePoint">Centre of the target area.</param>
    /// <param name="Radius">Radius of the target area.</param>
    /// <returns>GPS location within the target area.</returns>
    public static GpsLocation GenerateLocation(GpsLocation BasePoint, uint Radius)
    {
      double bearing = Rng.NextDouble() * 360.0;
      double distance = Rng.NextDouble() * (double)Radius;
      return BasePoint.GoVector(bearing, distance);
    }

    /// <summary>
    /// Retrieves a list of clients that should be found within specific area.
    /// </summary>
    /// <param name="Location">GPS location of the centre of the target area.</param>
    /// <param name="Radius">Radius of the target area in metres.</param>
    /// <returns>List of matchin profile names.</returns>
    public static Dictionary<string, ProtocolClient> GetClientsInLocation(GpsLocation Location, uint Radius)
    {
      Dictionary<string, ProtocolClient> res = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);

      for (int i = 0; i < ProfileNames.Count; i++)
      {
        string name = ProfileNames[i];
        ProtocolClient client = TestProfiles[name];
        GpsLocation profileLocation = ProfileLocations[i];

        double distance = GpsLocation.DistanceBetween(Location, profileLocation);
        if (distance < (double)Radius)
          res.Add(name, client);
      }

      return res;
    }
  }
}
