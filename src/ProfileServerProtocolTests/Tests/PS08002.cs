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
  /// PS08002 - Neighborhood Initialization Process - No Profiles
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08002---neighborhood-initialization-process---no-profiles
  /// </summary>
  public class PS08002 : ProtocolTest
  {
    public const string TestName = "PS08002";
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


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

      ProtocolClient client = new ProtocolClient();
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

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client.VerifyIdentityAsync();

        bool step1Ok = listPortsOk && verifyIdentityOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        PsProtocolMessage requestMessage = mb.CreateStartNeighborhoodInitializationRequest(1, 1);
        await client.SendMessageAsync(requestMessage);

        PsProtocolMessage responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool startNeighborhoodInitializationOk = idOk && statusOk;


        PsProtocolMessage serverRequestMessage = await client.ReceiveMessageAsync();
        bool typeOk = serverRequestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request
          && serverRequestMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest
          && serverRequestMessage.Request.ConversationRequest.RequestTypeCase == ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization;

        PsProtocolMessage clientResponseMessage = mb.CreateFinishNeighborhoodInitializationResponse(serverRequestMessage);
        await client.SendMessageAsync(clientResponseMessage);


        // Step 2 Acceptance
        bool step2Ok = startNeighborhoodInitializationOk && typeOk;

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
  }
}
