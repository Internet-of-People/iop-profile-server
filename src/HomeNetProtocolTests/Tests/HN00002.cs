using Iop.Homenode;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// HN00002 - Invalid Message Body
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN00.md#hn00002---invalid-message-body
  /// </summary>
  public class HN00002 : ProtocolTest
  {
    public const string TestName = "HN00002";
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
        // Step 1
        await client.ConnectAsync(NodeIp, PrimaryPort, false);

        byte[] request = new byte[] { 0x0D, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        await client.SendRawAsync(request);

        Message responseMessage = await client.ReceiveMessageAsync();

        // Step 1 Acceptance
        bool statusOk = responseMessage.Response.Status == Status.ErrorProtocolViolation;

        Passed = statusOk;

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
