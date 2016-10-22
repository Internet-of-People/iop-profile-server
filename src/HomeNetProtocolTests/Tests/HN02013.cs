using Google.Protobuf;
using HomeNetProtocol;
using Iop.Homenode;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// HN02013 - Parallel Verify Identity Requests
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn02013---parallel-verify-identity-requests
  /// </summary>
  public class HN02013 : ProtocolTest
  {
    public const string TestName = "HN02013";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("clNonCustomer Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress NodeIp = (IPAddress)ArgumentValues["Node IP"];
      int ClNonCustomerPort = (int)ArgumentValues["clNonCustomer Port"];
      log.Trace("(NodeIp:'{0}',ClNonCustomerPort:{1})", NodeIp, ClNonCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client1 = new ProtocolClient();
      MessageBuilder mb1 = client1.MessageBuilder;

      // Second client will use the same identity as the first client.
      ProtocolClient client2 = new ProtocolClient(0, new byte[] { 1, 0, 0 }, client1.GetIdentityKeys() );
      MessageBuilder mb2 = client2.MessageBuilder;
      try
      {
        // Step 1
        await client1.ConnectAsync(NodeIp, ClNonCustomerPort, true);
        bool startConversationOk = await client1.StartConversationAsync();

        Message requestMessage = mb1.CreateVerifyIdentityRequest(client1.Challenge);
        await client1.SendMessageAsync(requestMessage);
        Message responseMessage = await client1.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool verifyIdentityOk = idOk && statusOk;

        // Step 1 Acceptance
        bool step1Ok = startConversationOk && verifyIdentityOk;



        // Step 2
        await client2.ConnectAsync(NodeIp, ClNonCustomerPort, true);
        startConversationOk = await client2.StartConversationAsync();

        requestMessage = mb2.CreateVerifyIdentityRequest(client2.Challenge);
        await client2.SendMessageAsync(requestMessage);
        responseMessage = await client2.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        verifyIdentityOk = idOk && statusOk;

        // Step 2 Acceptance
        bool step2Ok = startConversationOk && verifyIdentityOk;



        // Step 3
        byte[] payload = Encoding.UTF8.GetBytes("test");
        requestMessage = mb1.CreatePingRequest(payload);
        await client1.SendMessageAsync(requestMessage);
        responseMessage = await client1.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        byte[] payloadReceived = responseMessage.Response.SingleResponse.Ping.Payload.ToByteArray();
        bool payloadOk = StructuralComparisons.StructuralComparer.Compare(payload, payloadReceived) == 0;

                // Step 3 Acceptance
        bool step3Ok = idOk && statusOk && payloadOk;

        
        Passed = step1Ok && step2Ok && step3Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client1.Dispose();
      client2.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
