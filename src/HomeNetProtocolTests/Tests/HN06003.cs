using Google.Protobuf;
using HomeNetCrypto;
using HomeNetProtocol;
using Iop.Homenode;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// HN06003 - Profile Search - Mass Location Search
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn06003---profile-search---mass-location-search
  /// </summary>
  public class HN06003 : ProtocolTest
  {
    public const string TestName = "HN06003";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
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


        bool profileInitializationOk = true;

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

              string name = string.Format("{0:00}-{1:00}-{2:00} [{3:US}]", locIndex, idIndex, radIndex, location);
              ProfileNames.Add(name);

              ProtocolClient profileClient = new ProtocolClient();

              await profileClient.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
              bool establishHomeNodeOk = await profileClient.EstablishHomeNodeAsync("test");
              profileClient.CloseConnection();

              await profileClient.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
              bool checkInOk = await profileClient.CheckInAsync();

              bool initializeProfileOk = await profileClient.InitializeProfileAsync(name, null, location, null);

              profileInitializationOk = establishHomeNodeOk && checkInOk && initializeProfileOk;
              profileClient.Dispose();

              if (!profileInitializationOk) break;
            }
          }
        }


        log.Trace("Generated profile names:");
        for (int i = 0; i < ProfileNames.Count; i++)
          log.Trace(" {0}", ProfileNames[i]);


        // Step 1 Acceptance
        bool step1Ok = listPortsOk && profileInitializationOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");
        
          
        // Step 2
        log.Trace("Step 2");

        // Start conversation.
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        // Search all profiles.
        Message requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 1000, 1000, false, false);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;


        bool totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == ProfileNames.Count;
        bool maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 1000;
        bool profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == ProfileNames.Count;

        bool queryRespOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;
        bool resultsOk = CompareResults(ProfileNames, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

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

            List<string> expectedNames = GetProfileNamesInLocation(targetLocation, radius);
            totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == expectedNames.Count;
            maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 1000;
            profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == expectedNames.Count;

            queryRespOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;
            resultsOk = CompareResults(expectedNames, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles);

            queriesOk = queryRespOk && resultsOk;
            if (!queriesOk)
            {
              log.Trace("Search query location {0} with radius {1} should produce {2} profiles, but produced {3} profiles.", targetLocation, radius, expectedNames.Count, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count);
              log.Trace("Expected names list:");
              foreach (string name in expectedNames)
                log.Trace("  {0}", name);

              List<string> resultNames = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Select(r => r.Name).OrderBy(r => r).ToList();
              log.Trace("Query result names list:");
              foreach (string name in resultNames)
                log.Trace("  {0}", name);
              break;
            }

            log.Trace("Search query location {0} with radius {1} produced {2} correct profiles.", targetLocation, radius, expectedNames.Count);
          }

          if (!queriesOk) break;
        }

        // Step 2 Acceptance
        bool step2Ok = startConversationOk && query1Ok && queriesOk;

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
    /// Checks search results against expected set of profile names.
    /// </summary>
    /// <param name="ExpectedNames">List of profile names that should be returned as a result of a search query.</param>
    /// <param name="Results">Actual search query results.</param>
    /// <returns>true if the results match expected values, false otherwise.</returns>
    public static bool CompareResults(List<string> ExpectedNames, IEnumerable<IdentityNetworkProfileInformation> Results)
    {
      List<string> resultNames = Results.Select(r => r.Name).ToList();
      return Enumerable.SequenceEqual(ExpectedNames.OrderBy(t => t), resultNames.OrderBy(t => t));
    }

    /// <summary>
    /// Retrieves a list of profile names that should be found within specific area.
    /// </summary>
    /// <param name="Location">GPS location of the centre of the target area.</param>
    /// <param name="Radius">Radius of the target area in metres.</param>
    /// <returns>List of matchin profile names.</returns>
    public static List<string> GetProfileNamesInLocation(GpsLocation Location, uint Radius)
    {
      List<string> res = new List<string>();

      for (int i = 0; i < ProfileNames.Count; i++)
      {
        string name = ProfileNames[i];
        GpsLocation profileLocation = ProfileLocations[i];

        double distance = GpsLocation.DistanceBetween(Location, profileLocation);
        if (distance < (double)Radius)
          res.Add(name);
      }

      return res;
    }
  }
}
