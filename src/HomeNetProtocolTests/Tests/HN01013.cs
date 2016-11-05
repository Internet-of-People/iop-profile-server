using HomeNetCrypto;
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
  /// HN01013 - Application Service Receive Message Notification Response - Bad Role
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn01011---application-service-receive-message-notification-response---bad-role
  /// </summary>
  public class HN01013 : ProtocolTest
  {
    public const string TestName = "HN01013";
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

        Message requestMessage = mb.CreateApplicationServiceReceiveMessageNotificationRequest(new byte[] { 0 } );
        Message responseMessage = mb.CreateApplicationServiceReceiveMessageNotificationResponse(requestMessage);
        await client.SendMessageAsync(responseMessage);

        // We should be disconnected by now, so sending or receiving should throw.
        byte[] data = Encoding.UTF8.GetBytes("Hello");
        byte[] payload = Crypto.Sha1(data);
        requestMessage = mb.CreatePingRequest(payload);

        bool disconnectedOk = false;
        try
        {
          await client.SendMessageAsync(requestMessage);
          await client.ReceiveMessageAsync();
        }
        catch
        {
          log.Trace("Expected exception occurred.");
          // Step 1 Acceptance
          disconnectedOk = true;
        }

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
