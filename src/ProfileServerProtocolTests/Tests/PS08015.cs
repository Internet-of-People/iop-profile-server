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
  /// PS08015 - Neighborhood Initialization Process - Parallel Processing
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08015---neighborhood-initialization-process---parallel-processing
  /// </summary>
  public class PS08015 : ProtocolTest
  {
    public const string TestName = "PS08015";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
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
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1})", ServerIp, PrimaryPort);

      bool res = false;
      Passed = false;

      ProtocolClient client1 = new ProtocolClient();
      ProtocolClient client2 = new ProtocolClient();
      ProtocolClient client3 = new ProtocolClient();
      try
      {
        MessageBuilder mb1 = client1.MessageBuilder;
        MessageBuilder mb2 = client2.MessageBuilder;
        MessageBuilder mb3 = client3.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client1.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client1.ListServerPorts(rolePorts);
        client1.CloseConnection();


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

        bool step1Ok = listPortsOk && profileInitializationOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");

        await client1.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client1.VerifyIdentityAsync();

        // Start neighborhood initialization process with the first client.
        Message requestMessage = mb1.CreateStartNeighborhoodInitializationRequest(1, 1, ServerIp);
        await client1.SendMessageAsync(requestMessage);

        Message responseMessage = await client1.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool startNeighborhoodInitializationOk = idOk && statusOk;

        bool step2Ok = verifyIdentityOk && startNeighborhoodInitializationOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");

        await client2.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        verifyIdentityOk = await client2.VerifyIdentityAsync();

        // Start neighborhood initialization process with the second client.
        requestMessage = mb2.CreateStartNeighborhoodInitializationRequest(1, 1, ServerIp);
        await client2.SendMessageAsync(requestMessage);

        responseMessage = await client2.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        startNeighborhoodInitializationOk = idOk && statusOk;



        // Wait for update requests for the second client.
        Message serverRequestMessage = null;
        Message clientResponseMessage = null;
        bool typeOk = false;

        List<SharedProfileAddItem> receivedItems = new List<SharedProfileAddItem>();

        bool error = false;
        while (receivedItems.Count < TestProfiles.Count)
        {
          serverRequestMessage = await client2.ReceiveMessageAsync();
          typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

          clientResponseMessage = mb2.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
          await client2.SendMessageAsync(clientResponseMessage);


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
        bool receivedProfilesOk = !error && client2.CheckProfileListMatchAddItems(TestProfiles, receivedItems);

        bool step3Ok = verifyIdentityOk && startNeighborhoodInitializationOk && receivedProfilesOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");





        // Step 4
        log.Trace("Step 4");

        await client3.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        verifyIdentityOk = await client3.VerifyIdentityAsync();

        // Start neighborhood initialization process with the third client.
        requestMessage = mb3.CreateStartNeighborhoodInitializationRequest(1, 1, ServerIp);
        await client3.SendMessageAsync(requestMessage);

        responseMessage = await client3.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        startNeighborhoodInitializationOk = idOk && statusOk;

        bool step4Ok = verifyIdentityOk && startNeighborhoodInitializationOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");



        // Step 5
        log.Trace("Step 5");


        // Wait for update requests for the first client.
        receivedItems = new List<SharedProfileAddItem>();

        error = false;
        while (receivedItems.Count < TestProfiles.Count)
        {
          serverRequestMessage = await client1.ReceiveMessageAsync();
          typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

          clientResponseMessage = mb1.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
          await client1.SendMessageAsync(clientResponseMessage);


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
        receivedProfilesOk = !error && client1.CheckProfileListMatchAddItems(TestProfiles, receivedItems);


        // Wait for finish request for the first client.
        serverRequestMessage = await client1.ReceiveMessageAsync();
        typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

        bool finishNeighborhoodInitializationResponseOk = typeOk;

        clientResponseMessage = mb1.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client1.SendMessageAsync(clientResponseMessage);


        bool step5Ok = receivedProfilesOk && finishNeighborhoodInitializationResponseOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");



        // Step 6
        log.Trace("Step 6");


        // Wait for update requests for the third client.
        receivedItems = new List<SharedProfileAddItem>();

        error = false;
        while (receivedItems.Count < TestProfiles.Count)
        {
          serverRequestMessage = await client3.ReceiveMessageAsync();
          typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

          clientResponseMessage = mb3.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
          await client3.SendMessageAsync(clientResponseMessage);


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
        receivedProfilesOk = !error && client3.CheckProfileListMatchAddItems(TestProfiles, receivedItems);


        // Wait for finish request for the third client.
        serverRequestMessage = await client3.ReceiveMessageAsync();
        typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

        finishNeighborhoodInitializationResponseOk = typeOk;

        clientResponseMessage = mb3.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client3.SendMessageAsync(clientResponseMessage);

        bool step6Ok = receivedProfilesOk && finishNeighborhoodInitializationResponseOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");

        // Wait for finish request for the second client.
        serverRequestMessage = await client2.ReceiveMessageAsync();
        typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

        finishNeighborhoodInitializationResponseOk = typeOk;

        clientResponseMessage = mb2.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client2.SendMessageAsync(clientResponseMessage);

        bool step7Ok = receivedProfilesOk && finishNeighborhoodInitializationResponseOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client1.Dispose();
      client2.Dispose();
      client3.Dispose();

      foreach (ProtocolClient protocolClient in TestProfiles.Values)
        protocolClient.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
