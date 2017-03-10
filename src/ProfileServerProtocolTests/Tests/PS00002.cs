using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using IopCommon;
using IopProtocol;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS00002 - Invalid Message Body
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS00.md#ps00002---invalid-message-body
  /// </summary>
  public class PS00002 : ProtocolTest
  {
    public const string TestName = "PS00002";
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
        // Step 1
        await client.ConnectAsync(ServerIp, PrimaryPort, false);

        byte[] request = new byte[] { 0x0D, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };
        await client.SendRawAsync(request);

        PsProtocolMessage responseMessage = await client.ReceiveMessageAsync();

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
