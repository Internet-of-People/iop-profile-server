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
  /// HN02007 - Home Node Request - Quota Exceeded
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn02007---home-node-request---quota-exceeded
  /// </summary>
  public class HN02007 : ProtocolTest
  {
    public const string TestName = "HN02007";
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
      ProtocolClient client2 = new ProtocolClient();
      try
      {
        MessageBuilder mb1 = client1.MessageBuilder;
        MessageBuilder mb2 = client2.MessageBuilder;

        // Step 1
        await client1.ConnectAsync(NodeIp, ClNonCustomerPort, true);
        bool startConversationOk = await client1.StartConversationAsync();

        Message requestMessage = mb1.CreateHomeNodeRequestRequest(null);
        await client1.SendMessageAsync(requestMessage);
        Message responseMessage = await client1.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        // Step 1 Acceptance
        bool step1Ok = idOk && statusOk;


        client1.Dispose();
        client1 = null;


        // Step 2
        await client2.ConnectAsync(NodeIp, ClNonCustomerPort, true);
        startConversationOk = await client2.StartConversationAsync();

        requestMessage = mb2.CreateHomeNodeRequestRequest(null);
        await client2.SendMessageAsync(requestMessage);
        responseMessage = await client2.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorQuotaExceeded;

        // Step 2 Acceptance
        bool step2Ok = idOk && statusOk;


        Passed = step1Ok && step2Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      if (client1 != null) client1.Dispose();
      if (client2 != null) client2.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
