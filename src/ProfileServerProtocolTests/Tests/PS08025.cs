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
  /// PS08025 - Neighborhood Initialization Process - Fails and Updates Before Retring
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08025---neighborhood-initialization-process---fails-and-updates-before-retring
  /// </summary>
  public class PS08025 : ProtocolTest
  {
    public const string TestName = "PS08025";
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
        PsProtocolMessage requestMessage = mb.CreateStartNeighborhoodInitializationRequest((uint)profileServer.PrimaryPort, (uint)profileServer.ServerNeighborPort);
        await client.SendMessageAsync(requestMessage);

        PsProtocolMessage responseMessage = await client.ReceiveMessageAsync();
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

        // Wait for update request.
        PsProtocolMessage serverRequestMessage = null;
        PsProtocolMessage clientResponseMessage = null;

        List<SharedProfileAddItem> receivedItems = new List<SharedProfileAddItem>();
        bool error = false;
        while (!error)
        {
          serverRequestMessage = await client.ReceiveMessageAsync();

          bool isNspUpdate = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

          bool isNspFinish = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

          if (isNspFinish) break;

          if (!isNspUpdate)
          {
            error = true;
            break;
          }

          clientResponseMessage = mb.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
          await client.SendMessageAsync(clientResponseMessage);

          foreach (SharedProfileUpdateItem updateItem in serverRequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
          {
            if (updateItem.ActionTypeCase != SharedProfileUpdateItem.ActionTypeOneofCase.Add)
            {
              log.Trace("Received invalid update item action type '{0}'.", updateItem.ActionTypeCase);
              error = true;
              break;
            }

            log.Trace("Received profile name '{0}'.", updateItem.Add.Name);
            receivedItems.Add(updateItem.Add);
          }

          if (error) break;
        }

        log.Trace("Received {0} profiles from target profile server.", receivedItems.Count);
        bool receivedProfilesOk = !error;

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

        client.CloseConnection();
        await Task.Delay(20000);

        bool step6Ok = true;

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

        // Meanwhile no updates should arrive.
        error = false;
        List<IncomingServerMessage> psMessages = profileServer.GetMessageList();
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


        bool step7Ok = profileInitializationOk && !error;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");





        // Step 8
        log.Trace("Step 8");
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        verifyIdentityOk = await client.VerifyIdentityAsync();

        // Start neighborhood initialization process.
        requestMessage = mb.CreateStartNeighborhoodInitializationRequest((uint)profileServer.PrimaryPort, (uint)profileServer.ServerNeighborPort);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        startNeighborhoodInitializationOk = idOk && statusOk;


        // Wait for update request.
        error = false;
        receivedItems.Clear();
        while (!error)
        {
          serverRequestMessage = await client.ReceiveMessageAsync();

          bool isNspUpdate = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

          bool isNspFinish = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

          if (isNspFinish) break;

          if (!isNspUpdate)
          {
            error = true;
            break;
          }

          clientResponseMessage = mb.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
          await client.SendMessageAsync(clientResponseMessage);

          foreach (SharedProfileUpdateItem updateItem in serverRequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
          {
            if (updateItem.ActionTypeCase != SharedProfileUpdateItem.ActionTypeOneofCase.Add)
            {
              log.Trace("Received invalid update item action type '{0}'.", updateItem.ActionTypeCase);
              error = true;
              break;
            }

            log.Trace("Received profile name '{0}'.", updateItem.Add.Name);
            receivedItems.Add(updateItem.Add);
          }

          if (error) break;
        }

        log.Trace("Received {0} profiles from target profile server.", receivedItems.Count);
        receivedProfilesOk = !error;

        clientResponseMessage = mb.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client.SendMessageAsync(clientResponseMessage);
        client.CloseConnection();


        bool step8Ok = verifyIdentityOk && startNeighborhoodInitializationOk && receivedProfilesOk;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");





        // Step 9
        log.Trace("Step 9");

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

        bool step9Ok = profileInitializationOk;

        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");




        // Step 10
        log.Trace("Step 10");

        // Meanwhile we expect updates to arrive on our simulated profile server.
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
            foreach (SharedProfileUpdateItem updateItem in message.Request.ConversationRequest.NeighborhoodSharedProfileUpdate.Items)
            {
              if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add)
              {
                SharedProfileAddItem addItem = updateItem.Add;
                receivedItems.Add(addItem);
                log.Trace("Received profile name '{0}'.", updateItem.Add.Name);
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
        bool profilesOk = client.CheckProfileListMatchAddItems(TestProfiles, receivedItems);

        bool step10Ok = receivedUpdatesOk && profilesOk;
        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok && step10Ok;

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
