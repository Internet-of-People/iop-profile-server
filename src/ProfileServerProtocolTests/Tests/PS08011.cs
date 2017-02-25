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
  /// PS08011 - Neighborhood Initialization Process - Updates Before Initialization Completes
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08011---neighborhood-initialization-process---updates-before-initialization-completes
  /// </summary>
  public class PS08011 : ProtocolTest
  {
    public const string TestName = "PS08011";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

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
        MessageBuilder mb = client.MessageBuilder;

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

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        profileServer = new ProfileServer("TestServer", ServerIp, BasePort, client.GetIdentityKeys());
        bool serverStartOk = profileServer.Start();

        bool step1Ok = listPortsOk && profileInitializationOk && serverStartOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client.VerifyIdentityAsync();

        // Start neighborhood initialization process.
        Message requestMessage = mb.CreateStartNeighborhoodInitializationRequest((uint)profileServer.PrimaryPort, (uint)profileServer.ServerNeighborPort);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool startNeighborhoodInitializationOk = idOk && statusOk;

        bool step2Ok = verifyIdentityOk && startNeighborhoodInitializationOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");


        // Step 3
        log.Trace("Step 3");

        profileInitializationOk = true;
        profileCount = 5;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        bool step3Ok = profileInitializationOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        // Step 4
        log.Trace("Step 4");

        List<SharedProfileAddItem> remoteProfiles = new List<SharedProfileAddItem>();

        // Wait for update request.
        Message serverRequestMessage = null;
        Message clientResponseMessage = null;
        bool typeOk = false;

        List<SharedProfileAddItem> receivedItems = new List<SharedProfileAddItem>();

        bool error = false;
        while (receivedItems.Count < 10)
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
        bool receivedProfilesOk = !error;
        remoteProfiles.AddRange(receivedItems);

        bool step4Ok = receivedProfilesOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");


        // Step 5
        log.Trace("Step 5");

        profileInitializationOk = true;
        profileCount = 5;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }

        bool step5Ok = profileInitializationOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");



        // Step 6
        log.Trace("Step 6");

        // Wait for finish request.
        serverRequestMessage = await client.ReceiveMessageAsync();
        typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

        bool finishNeighborhoodInitializationResponseOk = typeOk;

        clientResponseMessage = mb.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client.SendMessageAsync(clientResponseMessage);
        client.CloseConnection();

        bool step6Ok = finishNeighborhoodInitializationResponseOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");



        // Step 7
        log.Trace("Step 7");

        profileInitializationOk = true;
        profileCount = 5;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (!await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }

          TestProfiles.Add(profileClient.Profile.Name, profileClient);
        }


        await Task.Delay(20000);


        // Meanwhile we expect updates to arrive on our simulated profile server.
        error = false;
        List<IncomingServerMessage> psMessages = profileServer.GetMessageList();
        List<SharedProfileAddItem> addUpdates = new List<SharedProfileAddItem>();
        foreach (IncomingServerMessage ism in psMessages)
        {
          if (ism.Role != ServerRole.ServerNeighbor) continue;
          Message message = ism.IncomingMessage;

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

        remoteProfiles.AddRange(addUpdates);
        bool profilesOk = client.CheckProfileListMatchAddItems(TestProfiles, remoteProfiles);

        bool step7Ok = profileInitializationOk && receivedUpdatesOk && profilesOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok;

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
