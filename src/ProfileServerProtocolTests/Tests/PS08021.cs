using IopCommon;
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
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS08021 - New Neighbor - Too Many Profiles
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08021---new-neighbor---too-many-profiles
  /// </summary>
  public class PS08021 : ProtocolTest
  {
    public const string TestName = "PS08021";
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests." + TestName);

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


    /// <summary>Generated test profiles mapped by their name.</summary>
    public static Dictionary<string, ProtocolClient> TestProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);

    /// <summary>Random number generator.</summary>
    public static Random Rng = new Random();

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
        PsMessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");

        // Get port list.
        await client.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListServerPorts(rolePorts);
        client.CloseConnection();

        // Create identities.
        int profileNumber = 0;
        byte[] imageData = File.ReadAllBytes(Path.Combine("images", TestName + ".png"));

        for (int i = 0; i < 20500; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileNumber, imageData);
          profileNumber++;
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
          AddedNodeInfo = profileServer.GetNodeInfo(LocPort)
        };
        
        bool changeNotificationOk = await locServer.SendChangeNotification(change);

        // Wait for start of neighborhood initialization process.
        IncomingServerMessage incomingServerMessage = await profileServer.WaitForConversationRequest(ServerRole.ServerNeighbor, ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization);


        // Send update.
        bool statusOk = false;
        bool updateOk = true;
        int profilesSent = 0;
        List<ProtocolClient> profilesToSend = new List<ProtocolClient>(TestProfiles.Values);
        while (profilesToSend.Count > 0)
        {
          int batchSize = Math.Min(Rng.Next(100, 150), profilesToSend.Count);

          List<SharedProfileUpdateItem> updateItems = new List<SharedProfileUpdateItem>();
          foreach (ProtocolClient pc in profilesToSend.GetRange(0, batchSize))
            updateItems.Add(pc.GetSharedProfileUpdateAddItem());

          profilesToSend.RemoveRange(0, batchSize);

          PsProtocolMessage updateRequest = await profileServer.SendNeighborhoodSharedProfileUpdateRequest(incomingServerMessage.Client, updateItems);
          profilesSent += batchSize;
          incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, updateRequest);

          if (profilesSent <= 20000)
          {
            statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.Ok;
            bool batchOk = (updateRequest != null) && statusOk;
            if (!batchOk)
            {
              updateOk = false;
              break;
            }
          }
          else
          {
            statusOk = incomingServerMessage.IncomingMessage.Response.Status == Status.ErrorInvalidValue;
            int badIndex = 20000 - (profilesSent - batchSize);
            string expectedDetails = badIndex.ToString() + ".add";
            log.Trace("Expected details are '{0}'.", expectedDetails);

            bool detailsOk = incomingServerMessage.IncomingMessage.Response.Details == expectedDetails;
            bool batchOk = (updateRequest != null) && statusOk && detailsOk;

            if (!batchOk)
              updateOk = false;

            break;
          }
        }


        bool step2Ok = changeNotificationOk && updateOk;
        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");

        // Start conversation.
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        HashSet<byte[]> expectedCoveredServers = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default) { client.GetIdentityId(), Crypto.Sha256(client.ServerKey) };

        // Search all profiles.
        PsProtocolMessage requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        PsProtocolMessage responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        bool totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 0;
        bool maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        bool profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 0;

        bool queryOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;

        // Step 3 Acceptance
        bool step3Ok = startConversationOk && queryOk;

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
