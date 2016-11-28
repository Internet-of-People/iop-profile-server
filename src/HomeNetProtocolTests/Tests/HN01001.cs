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
  /// HN01001 - Primary Port Ping
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN01.md#hn01001---primary-port-ping
  /// </summary>
  public class HN01001 : ProtocolTest
  {
    public const string TestName = "HN01001";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress NodeIp = (IPAddress)ArgumentValues["Node IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      log.Trace("(NodeIp:'{0}',PrimaryPort:{1})", NodeIp, PrimaryPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(NodeIp, PrimaryPort, false);

        byte[] payload = Encoding.UTF8.GetBytes("Hello");
        Message requestMessage = mb.CreatePingRequest(payload);
        requestMessage.Id = 1234;

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
