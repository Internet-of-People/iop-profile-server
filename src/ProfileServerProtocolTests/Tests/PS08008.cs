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
  /// PS08008 - Neighborhood Initialization Process - Busy
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08008---neighborhood-initialization-process---busy
  /// </summary>
  public class PS08008 : ProtocolTest
  {
    public const string TestName = "PS08008";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Generated test profiles mapped by their name.</summary>
    public static Dictionary<string, ProtocolClient> TestProfiles = new Dictionary<string, ProtocolClient>();

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
      try
      {
        MessageBuilder mb = client1.MessageBuilder;

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


        bool step1Ok = listPortsOk && profileInitializationOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client1.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);

        bool verifyIdentityOk = await client1.VerifyIdentityAsync();

        // Start neighborhood initialization process.
        Message requestMessage = mb.CreateStartNeighborhoodInitializationRequest(1, 1);
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

        // Start neighborhood initialization process.
        requestMessage = client2.MessageBuilder.CreateStartNeighborhoodInitializationRequest(2, 2);
        await client2.SendMessageAsync(requestMessage);

        responseMessage = await client2.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorBusy;
        startNeighborhoodInitializationOk = idOk && statusOk;

        client2.CloseConnection();

        bool step3Ok = verifyIdentityOk && startNeighborhoodInitializationOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        // Step 4
        log.Trace("Step 4");

        // Wait for update request.
        Message serverRequestMessage = null;
        Message clientResponseMessage = null;
        bool typeOk = false;

        List<SharedProfileAddItem> receivedItems = new List<SharedProfileAddItem>();

        bool error = false;
        while (receivedItems.Count < TestProfiles.Count)
        {
          serverRequestMessage = await client1.ReceiveMessageAsync();
          typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
            && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
            && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate;

          clientResponseMessage = mb.CreateNeighborhoodSharedProfileUpdateResponse(serverRequestMessage);
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
        bool receivedProfilesOk = !error && client1.CheckProfileListMatchAddItems(TestProfiles, receivedItems);

        // Wait for finish request.
        serverRequestMessage = await client1.ReceiveMessageAsync();
        typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

        bool finishNeighborhoodInitializationResponseOk = typeOk;

        clientResponseMessage = mb.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client1.SendMessageAsync(clientResponseMessage);
        client1.CloseConnection();

        bool step4Ok = verifyIdentityOk && startNeighborhoodInitializationOk && receivedProfilesOk && finishNeighborhoodInitializationResponseOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");


        // Step 5
        log.Trace("Step 5");
        await client2.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool neighborhoodInitializationProcessOk = await client2.NeighborhoodInitializationProcessAsync(2, 2, TestProfiles);

        client2.CloseConnection();

        bool step5Ok = neighborhoodInitializationProcessOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");



        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client1.Dispose();
      client2.Dispose();

      foreach (ProtocolClient protocolClient in TestProfiles.Values)
        protocolClient.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
