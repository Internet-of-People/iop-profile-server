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
using IopCommon;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS08005 - Neighborhood Updates
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08005---neighborhood-updates
  /// </summary>
  public class PS08005 : ProtocolTest
  {
    public const string TestName = "PS08005";
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests."+ TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("Base Port", ProtocolTestArgumentType.Port),
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
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1},BasePort:{2})", ServerIp, PrimaryPort, BasePort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      ProfileServer profileServer = null;
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


        bool profileInitializationOk = true;
        byte[] testImageData = File.ReadAllBytes(Path.Combine("images", TestName + ".jpg"));
        byte[] testImageData2 = File.ReadAllBytes(Path.Combine("images", TestName + "b.png"));

        int profileIndex = 1;
        int profileCount = 50;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            TestProfiles.Add(profileClient.Profile.Name, profileClient);
          }
          else
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }
        }


        profileServer = new ProfileServer("TestServer", ServerIp, BasePort, client.GetIdentityKeys());
        bool serverStartOk = profileServer.Start();

        bool step1Ok = listPortsOk && profileInitializationOk && serverStartOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool neighborhoodInitializationProcessOk = await client.NeighborhoodInitializationProcessAsync(profileServer.PrimaryPort, profileServer.ServerNeighborPort, ServerIp, TestProfiles);

        client.CloseConnection();

        bool step2Ok = neighborhoodInitializationProcessOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");


        // Step 3
        log.Trace("Step 3");

        // Add 15 more identities.
        Dictionary<string, ProtocolClient> newProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);
        profileInitializationOk = true;
        profileCount = 15;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            TestProfiles.Add(profileClient.Profile.Name, profileClient);
            newProfiles.Add(profileClient.Profile.Name, profileClient);
          }
          else
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }
        }

        log.Trace("Waiting 20 seconds ...");
        await Task.Delay(20000);

        // Meanwhile we expect updates to arrive on our simulated profile server.
        bool error = false;
        List<IncomingServerMessage> psMessages = profileServer.GetMessageList();
        List<SharedProfileAddItem> addUpdates = new List<SharedProfileAddItem>();
        foreach (IncomingServerMessage ism in psMessages)
        {
          if (ism.Role != ServerRole.ServerNeighbor) continue;
          PsProtocolMessage message = ism.IncomingMessage;

          if ((message.MessageTypeCase == Message.MessageTypeOneofCase.Request)
            && (message.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
            && (message.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate))
          {
            foreach (SharedProfileUpdateItem updateItem in message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
            {
              if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add)
              {
                SharedProfileAddItem addItem = updateItem.Add;
                addUpdates.Add(addItem);
              }
              else
              {
                log.Trace("Received invalid update action type {0}.", updateItem.ActionTypeCase);
                error = true;
                break;
              }
            }
          }

          if (error) break;
        }

        bool receivedUpdatesOk = !error;

        bool updatesOk = client.CheckProfileListMatchAddItems(newProfiles, addUpdates);

        bool step3Ok = profileInitializationOk && receivedUpdatesOk && updatesOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        // Step 4
        log.Trace("Step 4");

        // Cancel 15 identities.
        HashSet<byte[]> cancelledProfiles = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);
        bool profileCancelingOk = true;
        profileCount = 15;
        for (int i = 0; i < profileCount; i++)
        {
          int index = Rng.Next(TestProfiles.Count);
          List<string> keys = TestProfiles.Keys.ToList();
          ProtocolClient profileClient = TestProfiles[keys[index]];

          if (await profileClient.CancelHostingAgreementAsync(ServerIp, (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            TestProfiles.Remove(keys[index]);
            cancelledProfiles.Add(profileClient.GetIdentityId());
          }
          else
          {
            profileCancelingOk = false;
            break;
          }
        }

        await Task.Delay(20000);

        // Meanwhile we expect updates to arrive on our simulated profile server.
        psMessages = profileServer.GetMessageList();
        List<SharedProfileDeleteItem> deleteUpdates = new List<SharedProfileDeleteItem>();
        foreach (IncomingServerMessage ism in psMessages)
        {
          if (ism.Role != ServerRole.ServerNeighbor) continue;
          PsProtocolMessage message = ism.IncomingMessage;

          if ((message.MessageTypeCase == Message.MessageTypeOneofCase.Request)
            && (message.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
            && (message.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate))
          {
            foreach (SharedProfileUpdateItem updateItem in message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
            {
              if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Delete)
              {
                SharedProfileDeleteItem deleteItem = updateItem.Delete;
                deleteUpdates.Add(deleteItem);
              }
              else
              {
                log.Trace("Received invalid update action type {0}.", updateItem.ActionTypeCase);
                error = true;
                break;
              }
            }
          }

          if (error) break;
        }

        receivedUpdatesOk = !error;

        updatesOk = client.CheckProfileListMatchDeleteItems(cancelledProfiles, deleteUpdates);

        bool step4Ok = profileCancelingOk && receivedUpdatesOk && updatesOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");



        // Step 5
        log.Trace("Step 5");

        // Change 25 profiles.
        Dictionary<byte[], SharedProfileChangeItem> changedProfiles = new Dictionary<byte[], SharedProfileChangeItem>(StructuralEqualityComparer<byte[]>.Default);
        bool profileChangingOk = true;
        profileCount = 25;
        for (int i = 0; i < profileCount; i++)
        {
          int index = Rng.Next(TestProfiles.Count);
          List<string> keys = TestProfiles.Keys.ToList();
          ProtocolClient profileClient = TestProfiles[keys[index]];
          if (changedProfiles.ContainsKey(profileClient.GetIdentityId()))
          {
            // Do not take one client twice.
            i--;
            continue;
          }

          SharedProfileChangeItem changeItem = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(profileClient.GetIdentityId()),
            SetName = Rng.NextDouble() < 0.20,
            SetLocation = Rng.NextDouble() < 0.20,
            SetExtraData = Rng.NextDouble() < 0.20,
            SetThumbnailImage = Rng.NextDouble() < 0.20,
            SetVersion = false,
          };

          // Make sure we change at least one thing.
          if (!changeItem.SetName && !changeItem.SetLocation && !changeItem.SetThumbnailImage) changeItem.SetExtraData = true;

          if (changeItem.SetName)
          {
            log.Trace("Changing name of identity name '{0}'.", profileClient.Profile.Name);
            TestProfiles.Remove(profileClient.Profile.Name);

            profileClient.Profile.Name += "-change";
            changeItem.Name = profileClient.Profile.Name;
            TestProfiles.Add(profileClient.Profile.Name, profileClient);
          }

          if (changeItem.SetLocation)
          {
            log.Trace("Changing location of identity name '{0}'.", profileClient.Profile.Name);
            if (Rng.NextDouble() < 0.30) profileClient.Profile.Location.Latitude = profileClient.Profile.Location.Latitude / 2 + 1;
            else if (Rng.NextDouble() < 0.30) profileClient.Profile.Location.Longitude = profileClient.Profile.Location.Latitude / 2 + 1;
            else
            {
              profileClient.Profile.Location.Latitude = profileClient.Profile.Location.Latitude / 2 + 1;
              profileClient.Profile.Location.Longitude = profileClient.Profile.Location.Latitude / 2 + 1;
            }
            changeItem.Latitude = profileClient.Profile.Location.GetLocationTypeLatitude();
            changeItem.Longitude = profileClient.Profile.Location.GetLocationTypeLongitude();
          }

          if (changeItem.SetExtraData)
          {
            log.Trace("Changing extra data of identity name '{0}'.", profileClient.Profile.Name);
            if (!string.IsNullOrEmpty(profileClient.Profile.ExtraData)) profileClient.Profile.ExtraData = profileClient.Profile.ExtraData.Substring(0, Math.Min(10, profileClient.Profile.ExtraData.Length)) + "-change";
            else profileClient.Profile.ExtraData = "new value";
            changeItem.ExtraData = profileClient.Profile.ExtraData;
          }

          if (changeItem.SetThumbnailImage)
          {
            log.Trace("Changing profile image of identity name '{0}'.", profileClient.Profile.Name);
            profileClient.Profile.ProfileImage = testImageData2;
            profileClient.Profile.ThumbnailImage = testImageData2;
            changeItem.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(testImageData2);
          }

          changedProfiles.Add(profileClient.GetIdentityId(), changeItem);

          await profileClient.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
          bool ccheckInOk = await profileClient.CheckInAsync();

          PsProtocolMessage clientRequest = profileClient.MessageBuilder.CreateUpdateProfileRequest(null, changeItem.SetName ? changeItem.Name : null, changeItem.SetThumbnailImage ? profileClient.Profile.ProfileImage : null, changeItem.SetLocation ? profileClient.Profile.Location : null, changeItem.SetExtraData ? changeItem.ExtraData : null);
          await profileClient.SendMessageAsync(clientRequest);
          PsProtocolMessage clientResponse = await profileClient.ReceiveMessageAsync();

          bool cidOk = clientResponse.Id == clientRequest.Id;
          bool cstatusOk = clientResponse.Response.Status == Status.Ok;

          profileClient.CloseConnection();

          bool changeOk = ccheckInOk && cidOk && cstatusOk;

          if (!changeOk)
          { 
            profileChangingOk = false;
            break;
          }
        }

        await Task.Delay(20000);

        // Meanwhile we expect updates to arrive on our simulated profile server.
        psMessages = profileServer.GetMessageList();
        List<SharedProfileChangeItem> changeUpdates = new List<SharedProfileChangeItem>();
        foreach (IncomingServerMessage ism in psMessages)
        {
          if (ism.Role != ServerRole.ServerNeighbor) continue;
          PsProtocolMessage message = ism.IncomingMessage;

          if ((message.MessageTypeCase == Message.MessageTypeOneofCase.Request)
            && (message.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
            && (message.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate))
          {
            foreach (SharedProfileUpdateItem updateItem in message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
            {
              if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Change)
              {
                SharedProfileChangeItem changeItem = updateItem.Change;
                changeUpdates.Add(changeItem);
              }
              else
              {
                log.Trace("Received invalid update action type {0}.", updateItem.ActionTypeCase);
                error = true;
                break;
              }
            }
          }

          if (error) break;
        }

        receivedUpdatesOk = !error;

        updatesOk = client.CheckProfileListMatchChangeItems(changedProfiles, changeUpdates);

        bool step5Ok = profileChangingOk && receivedUpdatesOk && updatesOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");




        // Step 6
        log.Trace("Step 6");

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client.VerifyIdentityAsync();
        PsProtocolMessage request = mb.CreateStopNeighborhoodUpdatesRequest();
        await client.SendMessageAsync(request);
        PsProtocolMessage response = await client.ReceiveMessageAsync();

        bool idOk = response.Id == request.Id;
        bool statusOk = response.Response.Status == Status.Ok;
        bool stopUpdatesOk = idOk && statusOk;

        client.CloseConnection();

        bool step6Ok = verifyIdentityOk && stopUpdatesOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");





        // Step 7
        log.Trace("Step 7");

        // Add 5 more identities.
        profileCount = 5;
        error = false;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            log.Trace("Failed to register and initialize client #{0}.", i);
            profileClient.Dispose();
            error = true;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        bool addProfilesOk = !error;


        // Cancel 5 identities.
        error = false;
        profileCount = 5;
        for (int i = 0; i < profileCount; i++)
        {
          int index = Rng.Next(TestProfiles.Count);
          List<string> keys = TestProfiles.Keys.ToList();
          ProtocolClient profileClient = TestProfiles[keys[index]];

          if (!await profileClient.CancelHostingAgreementAsync(ServerIp, (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            log.Trace("Failed to cancel hosting agreement of client name '{0}'.", profileClient.Profile.Name);
            error = true;
            break;
          }

          TestProfiles.Remove(keys[index]);
        }

        bool cancelProfilesOk = !error;



        // Change 5 profiles.
        error = false;
        profileCount = 5;
        for (int i = 0; i < profileCount; i++)
        {
          int index = Rng.Next(TestProfiles.Count);
          List<string> keys = TestProfiles.Keys.ToList();
          ProtocolClient profileClient = TestProfiles[keys[index]];

          SharedProfileChangeItem changeItem = new SharedProfileChangeItem()
          {
            IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(profileClient.GetIdentityId()),
            SetExtraData = true,
            ExtraData = "last change",
            SetVersion = false,
            SetLocation = false,
            SetName = false,
            SetThumbnailImage = false,
          };

          profileClient.Profile.ExtraData = changeItem.ExtraData;

          await profileClient.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
          bool checkInOk = await profileClient.CheckInAsync();

          PsProtocolMessage clientRequest = profileClient.MessageBuilder.CreateUpdateProfileRequest(null, null, null, null, changeItem.ExtraData);
          await profileClient.SendMessageAsync(clientRequest);
          PsProtocolMessage clientResponse = await profileClient.ReceiveMessageAsync();

          bool cidOk = clientResponse.Id == clientRequest.Id;
          bool cstatusOk = clientResponse.Response.Status == Status.Ok;

          profileClient.CloseConnection();

          bool changeOk = checkInOk && cidOk && cstatusOk;

          if (!changeOk)
          {
            log.Trace("Failed to change profile of client name '{0}'.", profileClient.Profile.Name);
            error = true;
            break;
          }
        }

        bool changeProfileOk = !error;

        await Task.Delay(20000);

        // Meanwhile we expect NO update messages to arrive on our simulated profile server.
        error = false;
        psMessages = profileServer.GetMessageList();
        foreach (IncomingServerMessage ism in psMessages)
        {
          if (ism.Role != ServerRole.ServerNeighbor) continue;
          PsProtocolMessage message = ism.IncomingMessage;

          if ((message.MessageTypeCase == Message.MessageTypeOneofCase.Request)
            && (message.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
            && (message.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate))
          {
            error = true;
            break;
          }
        }
        bool noNewUpdatesOk = !error;


        bool step7Ok = addProfilesOk && cancelProfilesOk && changeProfileOk && noNewUpdatesOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");




        // Step 8
        log.Trace("Step 8");

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        verifyIdentityOk = await client.VerifyIdentityAsync();
        request = mb.CreateStartNeighborhoodInitializationRequest((uint)profileServer.PrimaryPort, (uint)profileServer.ServerNeighborPort, ServerIp);
        await client.SendMessageAsync(request);
        response = await client.ReceiveMessageAsync();

        idOk = response.Id == request.Id;
        statusOk = response.Response.Status == Status.Ok;
        bool startNeighborhoodInitializationOk = idOk && statusOk;

        bool step8Ok = verifyIdentityOk && startNeighborhoodInitializationOk;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");



        // Step 9
        log.Trace("Step 9");

        // Add 5 more identities.
        // These identities may or may not be included in the current initialization process in progress.
        List<ProtocolClient> newClients = new List<ProtocolClient>();
        profileCount = 5;
        error = false;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            log.Trace("Failed to register and initialize client #{0}.", i);
            profileClient.Dispose();
            error = true;
            break;
          }

          newClients.Add(profileClient);
        }

        addProfilesOk = !error;

        bool step9Ok = addProfilesOk;

        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");



        // Step 10
        log.Trace("Step 10");


        // Wait for update request.
        PsProtocolMessage serverRequestMessage = null;
        PsProtocolMessage clientResponseMessage = null;
        bool typeOk = false;

        List<SharedProfileAddItem> receivedItems = new List<SharedProfileAddItem>();

        error = false;
        while (receivedItems.Count < TestProfiles.Count)
        {
          serverRequestMessage = await client.ReceiveMessageAsync();
          typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

          clientResponseMessage = mb.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
          await client.SendMessageAsync(clientResponseMessage);


          if (!typeOk) break;

          foreach (SharedProfileUpdateItem updateItem in serverRequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
          {
            if (updateItem.ActionTypeCase != SharedProfileUpdateItem.ActionTypeOneofCase.Add)
            {
              log.Trace("Received invalid update item action type '{0}'.", updateItem.ActionTypeCase);
              error = true;
              break;
            }

            receivedItems.Add(updateItem.Add);
          }

          if (error) break;
        }

        log.Trace("Received {0} profiles from target profile server.", receivedItems.Count);

        bool receivedProfilesOk = false;
        if (!error)
        {
          // As we do not know if new identities made it to the initialization process, we just try all possible combinations.
          // First possibility is that no new clients were present in the process.
          if (client.CheckProfileListMatchAddItems(TestProfiles, receivedItems))
            receivedProfilesOk = true;

          for (int i = 0; i < newClients.Count; i++)
          {
            ProtocolClient newClient = newClients[i];
            TestProfiles.Add(newClient.Profile.Name, newClient);

            // Other possibilities are that one or more clients were present in the process.
            if (!receivedProfilesOk)
            {
              if (client.CheckProfileListMatchAddItems(TestProfiles, receivedItems))
                receivedProfilesOk = true;
            }
          }
        }

        bool step10Ok = receivedProfilesOk;

        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");



        // Step 11
        log.Trace("Step 11");

        // Add 5 more identities.
        profileCount = 5;
        error = false;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            log.Trace("Failed to register and initialize client #{0}.", i);
            profileClient.Dispose();
            error = true;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        addProfilesOk = !error;

        bool step11Ok = addProfilesOk;

        log.Trace("Step 11: {0}", step11Ok ? "PASSED" : "FAILED");




        // Step 12
        log.Trace("Step 12");

        client.CloseConnection();
        bool step12Ok = true;

        log.Trace("Step 12: {0}", step12Ok ? "PASSED" : "FAILED");



        // Step 13
        log.Trace("Step 13");

        // Add 5 more identities.
        profileCount = 5;
        error = false;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            log.Trace("Failed to register and initialize client #{0}.", i);
            profileClient.Dispose();
            error = true;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        addProfilesOk = !error;

        await Task.Delay(20000);

        // Meanwhile we expect NO update messages to arrive on our simulated profile server.
        error = false;
        psMessages = profileServer.GetMessageList();
        foreach (IncomingServerMessage ism in psMessages)
        {
          if (ism.Role != ServerRole.ServerNeighbor) continue;
          PsProtocolMessage message = ism.IncomingMessage;

          if ((message.MessageTypeCase == Message.MessageTypeOneofCase.Request)
            && (message.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
            && (message.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate))
          {
            error = true;
            break;
          }
        }
        noNewUpdatesOk = !error;

        bool step13Ok = addProfilesOk && noNewUpdatesOk;

        log.Trace("Step 13: {0}", step13Ok ? "PASSED" : "FAILED");




        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok && step10Ok && step11Ok && step12Ok && step13Ok;

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

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
