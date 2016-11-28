using HomeNetProtocol;
using Iop.Profileserver;
using System;
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
  /// PS00006 - Disconnection of Inactive TCP Client from Non-Customer Port - No Message
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS00.md#ps00006---disconnection-of-inactive-tcp-client-from-non-customer-port---no-message
  /// </summary>
  public class HN00006 : ProtocolTest
  {
    public const string TestName = "PS00006";
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
      int NonCustomerPort = (int)ArgumentValues["clNonCustomer Port"];
      log.Trace("(ServerIp:'{0}',NonCustomerPort:{1})", ServerIp, NonCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(ServerIp, NonCustomerPort, true);

        log.Trace("Entering 180 seconds wait...");
        await Task.Delay(180 * 1000);
        log.Trace("Wait completed.");

        byte[] payload = Encoding.UTF8.GetBytes("test");
        Message requestMessage = mb.CreatePingRequest(payload);

        // We should be disconnected by now, so sending or receiving should throw.
        bool disconnectedOk = false;
        try
        { 
          await client.SendMessageAsync(requestMessage);
          await client.ReceiveMessageAsync();
        }
        catch
        {
          log.Trace("Expected exception occurred.");
          disconnectedOk = true;
        }

        // Step 1 Acceptance
        Passed = disconnectedOk;

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
