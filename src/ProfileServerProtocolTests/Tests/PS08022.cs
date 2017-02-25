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
  /// PS08022 - New Neighbor - Too Many Profiles - Update
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08022---new-neighbor---too-many-profiles---update
  /// </summary>
  public class PS08022 : ProtocolTest
  {
    public const string TestName = "PS08022";
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
        MessageBuilder mb = client.MessageBuilder;

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

        Dictionary<string, ProtocolClient> expectedLastClients = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);
        List<string> excessClientNames = new List<string>();
        for (int i = 0; i < 20050; i++)
        {
          ProtocolClient protocolClient = new ProtocolClient();
          protocolClient.InitializeRandomProfile(profileNumber, imageData);
          profileNumber++;
          if (i >= 19990)
          {
            protocolClient.Profile.Type = "last";
            if (i < 20000) expectedLastClients.Add(protocolClient.Profile.Name, protocolClient);
            else excessClientNames.Add(protocolClient.Profile.Name);
          }
          TestProfiles.Add(protocolClient.Profile.Name, protocolClient);
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
        List<ProtocolClient> allProfiles = new List<ProtocolClient>(TestProfiles.Values);
        List<ProtocolClient> profilesToSend = allProfiles.GetRange(0, 19990);
        while (profilesToSend.Count > 0)
        {
          int batchSize = Math.Min(Rng.Next(100, 150), profilesToSend.Count);

          List<SharedProfileUpdateItem> updateItems = new List<SharedProfileUpdateItem>();
          foreach (ProtocolClient pc in profilesToSend.GetRange(0, batchSize))
            updateItems.Add(pc.GetSharedProfileUpdateAddItem());

          profilesToSend.RemoveRange(0, batchSize);

          Message updateRequest = await profileServer.SendNeighborhoodSharedProfileUpdateRequest(incomingServerMessage.Client, updateItems);
          profilesSent += batchSize;
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

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client.VerifyIdentityAsync();

        List<SharedProfileUpdateItem> badUpdateItems = new List<SharedProfileUpdateItem>();
        foreach (ProtocolClient pc in allProfiles.GetRange(19990, 60))
          badUpdateItems.Add(pc.GetSharedProfileUpdateAddItem());


        Message requestMessage = mb.CreateNeighborhoodSharedProfileUpdateRequest(badUpdateItems);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "10.add";

        bool badUpdateOk = idOk && statusOk && detailsOk;
        client.CloseConnection();

        // Step 3 Acceptance
        bool step3Ok = verifyIdentityOk && badUpdateOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        // Step 4
        log.Trace("Step 4");

        // Start conversation.
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        HashSet<byte[]> expectedCoveredServers = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default) { client.GetIdentityId(), Crypto.Sha256(client.ServerKey) };

        // Search all profiles with type "last".
        requestMessage = mb.CreateProfileSearchRequest("last", null, null, null, 0, 100, 100);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        bool totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 10;
        bool maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        bool profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 10;
        bool resultsOk = client.CheckProfileListMatchSearchResultItems(expectedLastClients, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.ToList(), false, false, client.GetIdentityId(), true);

        bool queryOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && resultsOk;

        client.CloseConnection();

        // Step 4 Acceptance
        bool step4Ok = startConversationOk && queryOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");



        // Step 5
        log.Trace("Step 5");

        // Make TestProfiles reflect the status on the target profile server. 
        foreach (string excessClientName in excessClientNames)
        {
          TestProfiles[excessClientName].Dispose();
          TestProfiles.Remove(excessClientName);
        }

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        verifyIdentityOk = await client.VerifyIdentityAsync();

        // Select 140 profiles for deletion.
        List<SharedProfileUpdateItem> deleteUpdateItems = new List<SharedProfileUpdateItem>();
        while (deleteUpdateItems.Count < 140)
        {
          int index = Rng.Next(TestProfiles.Count);
          ProtocolClient pc = TestProfiles.ElementAt(index).Value;
          deleteUpdateItems.Add(pc.GetSharedProfileUpdateDeleteItem());

          if (expectedLastClients.ContainsKey(pc.Profile.Name))
            expectedLastClients.Remove(pc.Profile.Name);

          TestProfiles.Remove(pc.Profile.Name);
          pc.Dispose();
        }

        // Send delete update.
        requestMessage = mb.CreateNeighborhoodSharedProfileUpdateRequest(deleteUpdateItems);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool deleteUpdateOk = idOk && statusOk;


        // Generate 160 new identities.
        badUpdateItems.Clear();
        excessClientNames.Clear();
        for (int i = 0; i < 160; i++)
        {
          ProtocolClient protocolClient = new ProtocolClient();
          protocolClient.InitializeRandomProfile(profileNumber, imageData);
          profileNumber++;
          protocolClient.Profile.Type = "last";

          if (TestProfiles.Count < 20000) expectedLastClients.Add(protocolClient.Profile.Name, protocolClient);
          else excessClientNames.Add(protocolClient.Profile.Name);

          TestProfiles.Add(protocolClient.Profile.Name, protocolClient);
          badUpdateItems.Add(protocolClient.GetSharedProfileUpdateAddItem());
        }


        // Add the new profiles to the profile server.
        requestMessage = mb.CreateNeighborhoodSharedProfileUpdateRequest(badUpdateItems);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "140.add";

        badUpdateOk = idOk && statusOk && detailsOk;
        client.CloseConnection();

        // Step 5 Acceptance
        bool step5Ok = verifyIdentityOk && deleteUpdateOk && badUpdateOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");



        // Step 6
        log.Trace("Step 6");

        // Make TestProfiles reflect the status on the target profile server. 
        foreach (string excessClientName in excessClientNames)
        {
          TestProfiles[excessClientName].Dispose();
          TestProfiles.Remove(excessClientName);
        }

        // Start conversation.
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        startConversationOk = await client.StartConversationAsync();

        // Search all profiles with type "last".
        requestMessage = mb.CreateProfileSearchRequest("last", null, null, null, 0, 1000, 1000, false, false);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;


        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == expectedLastClients.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 1000;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == expectedLastClients.Count;
        resultsOk = client.CheckProfileListMatchSearchResultItems(expectedLastClients, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.ToList(), false, false, client.GetIdentityId(), false);

        queryOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && resultsOk;

        client.CloseConnection();

        // Step 6 Acceptance
        bool step6Ok = startConversationOk && queryOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");




        // Step 7
        log.Trace("Step 7");

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        verifyIdentityOk = await client.VerifyIdentityAsync();

        // Select 40 profiles for deletion.
        deleteUpdateItems = new List<SharedProfileUpdateItem>();
        while (deleteUpdateItems.Count < 40)
        {
          int index = Rng.Next(TestProfiles.Count);
          ProtocolClient pc = TestProfiles.ElementAt(index).Value;
          deleteUpdateItems.Add(pc.GetSharedProfileUpdateDeleteItem());

          if (expectedLastClients.ContainsKey(pc.Profile.Name))
            expectedLastClients.Remove(pc.Profile.Name);

          TestProfiles.Remove(pc.Profile.Name);
          pc.Dispose();
        }

        // Select 40 profiles for change, but avoid updating one profile twice in a single update message, which is forbidden.
        HashSet<int> usedIndexes = new HashSet<int>();
        List<SharedProfileUpdateItem> changeUpdateItems = new List<SharedProfileUpdateItem>();
        while (changeUpdateItems.Count < 40)
        {
          int index = Rng.Next(TestProfiles.Count);

          if (usedIndexes.Contains(index)) continue;
          usedIndexes.Add(index);

          ProtocolClient pc = TestProfiles.ElementAt(index).Value;
          pc.Profile.ExtraData = "1234567890";
          SharedProfileUpdateItem changeUpdateItem = new SharedProfileUpdateItem()
          {
            Change = new SharedProfileChangeItem()
            {
              IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(pc.GetIdentityId()),
              SetExtraData = true,
              ExtraData = pc.Profile.ExtraData
            }
          };
          changeUpdateItems.Add(changeUpdateItem);
        }

        // Generate 40 new identities.
        List<SharedProfileUpdateItem> addUpdateItems = new List<SharedProfileUpdateItem>();
        for (int i = 0; i < 40; i++)
        {
          ProtocolClient protocolClient = new ProtocolClient();
          protocolClient.InitializeRandomProfile(profileNumber, imageData);
          profileNumber++;
          protocolClient.Profile.Type = "last";

          expectedLastClients.Add(protocolClient.Profile.Name, protocolClient);

          TestProfiles.Add(protocolClient.Profile.Name, protocolClient);
          addUpdateItems.Add(protocolClient.GetSharedProfileUpdateAddItem());
        }


        // Send all the updates as one.
        List<SharedProfileUpdateItem> newUpdateItems = new List<SharedProfileUpdateItem>();
        newUpdateItems.AddRange(deleteUpdateItems);
        newUpdateItems.AddRange(changeUpdateItems);
        newUpdateItems.AddRange(addUpdateItems);
        requestMessage = mb.CreateNeighborhoodSharedProfileUpdateRequest(newUpdateItems);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool newUpdateOk = idOk && statusOk;
        client.CloseConnection();

        // Step 7 Acceptance
        bool step7Ok = verifyIdentityOk && newUpdateOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");



        // Step 8
        log.Trace("Step 8");

        // Start conversation.
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        startConversationOk = await client.StartConversationAsync();

        // Search all profiles with type "last".
        requestMessage = mb.CreateProfileSearchRequest("last", null, null, null, 0, 1000, 1000, false, false);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == expectedLastClients.Count;
        maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 1000;
        profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == expectedLastClients.Count;
        resultsOk = client.CheckProfileListMatchSearchResultItems(expectedLastClients, responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.ToList(), false, false, client.GetIdentityId(), false);

        queryOk = idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk && resultsOk;

        client.CloseConnection();

        // Step 8 Acceptance
        bool step8Ok = startConversationOk && queryOk;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok;

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
