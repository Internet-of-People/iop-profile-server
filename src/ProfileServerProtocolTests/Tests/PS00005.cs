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
  /// PS00005 - Disconnection of Inactive TCP Client from Primary Port - Incomplete Message
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS00.md#ps00005---disconnection-of-inactive-tcp-client-from-primary-port---incomplete-message
  /// </summary>
  public class HN00005 : ProtocolTest
  {
    public const string TestName = "PS00005";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

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
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(ServerIp, PrimaryPort, false);

        byte[] payload = Encoding.UTF8.GetBytes("test");
        Message requestMessage = mb.CreatePingRequest(payload);

        byte[] messageData = ProtocolHelper.GetMessageBytes(requestMessage);
        byte[] part1 = new byte[6];
        byte[] part2 = new byte[messageData.Length - part1.Length];
        Array.Copy(messageData, 0, part1, 0, part1.Length);
        Array.Copy(messageData, part1.Length, part2, 0, part2.Length);
        await client.SendRawAsync(part1);


        log.Trace("Entering 500 seconds wait...");
        await Task.Delay(500 * 1000);
        log.Trace("Wait completed.");

        // We should be disconnected by now, so sending or receiving should throw.
        bool disconnectedOk = false;
        try
        {
          await client.SendRawAsync(part2);
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
