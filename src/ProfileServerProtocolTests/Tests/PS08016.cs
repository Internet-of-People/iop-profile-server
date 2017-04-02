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
  /// PS08016 - Neighborhood Updates - Rejected
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08016---neighborhood-updates---rejected
  /// </summary>
  public class PS08016 : ProtocolTest
  {
    public const string TestName = "PS08016";
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests." + TestName);

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

        int profileIndex = 1;
        int profileCount = 10;
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

        // Add 5 more identities.
        Dictionary<string, ProtocolClient> newProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);
        profileInitializationOk = true;
        profileCount = 5;
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

        // Set profile server to reject updates.
        profileServer.RejectNeighborhoodSharedProfileUpdate = true;

        // Add 1 more identity.
        newProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);
        profileInitializationOk = true;
        profileCount = 1;
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

        await Task.Delay(20000);

        // Set profile server to accept updates again.
        profileServer.RejectNeighborhoodSharedProfileUpdate = false;

        // Meanwhile we expect the update to arrive on our simulated profile server.
        error = false;
        psMessages = profileServer.GetMessageList();
        addUpdates = new List<SharedProfileAddItem>();
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

        receivedUpdatesOk = !error && (addUpdates.Count == 1);

        bool step4Ok = profileInitializationOk && receivedUpdatesOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");



        // Step 5
        log.Trace("Step 5");

        // Add 5 more identities.
        newProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);
        profileInitializationOk = true;
        profileCount = 5;
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
        error = false;
        psMessages = profileServer.GetMessageList();
        addUpdates = new List<SharedProfileAddItem>();
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

        receivedUpdatesOk = !error;

        updatesOk = addUpdates.Count == 0;

        bool step5Ok = profileInitializationOk && receivedUpdatesOk && updatesOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");
        

        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok;

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
