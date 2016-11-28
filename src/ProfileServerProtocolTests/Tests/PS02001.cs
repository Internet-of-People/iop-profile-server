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
  /// PS02001 - Client Non-Customer Port Ping
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS02.md#ps02001---client-non-customer-port-ping
  /// </summary>
  public class PS02001 : ProtocolTest
  {
    public const string TestName = "PS02001";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("clNonCustomer Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress ServerIp = (IPAddress)ArgumentValues["Server IP"];
      int ClNonCustomerPort = (int)ArgumentValues["clNonCustomer Port"];
      log.Trace("(ServerIp:'{0}',ClNonCustomerPort:{1})", ServerIp, ClNonCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(ServerIp, ClNonCustomerPort, true);

        byte[] payload = Encoding.UTF8.GetBytes("Hello");
        Message requestMessage = mb.CreatePingRequest(payload);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        // Step 1 Acceptance
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        byte[] payloadReceived = responseMessage.Response.SingleResponse.Ping.Payload.ToByteArray();
        bool payloadOk = StructuralComparisons.StructuralComparer.Compare(payload, payloadReceived) == 0;

        DateTime clock = ProtocolHelper.UnixTimestampMsToDateTime(responseMessage.Response.SingleResponse.Ping.Clock);
        bool clockOk = Math.Abs((DateTime.UtcNow - clock).TotalMinutes) <= 10; 

        Passed = idOk && statusOk && payloadOk && clockOk;

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
