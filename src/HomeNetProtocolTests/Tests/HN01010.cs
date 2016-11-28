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
  /// HN01010 - Incoming Call Notification - Protocol Violation
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN01.md#hn01010---incoming-call-notification---protocol-violation
  /// </summary>
  public class HN01010 : ProtocolTest
  {
    public const string TestName = "HN01010";
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

        byte[] data = Encoding.UTF8.GetBytes("test");
        byte[] token = Crypto.Sha256(data);
        Message requestMessage = mb.CreateIncomingCallNotificationRequest(client.GetIdentityKeys().PublicKey, "Test Service", token);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();

        // Step 1 Acceptance
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorProtocolViolation;


        // We should be disconnected by now, so sending or receiving should throw.
        bool disconnectedOk = false;
        data = Encoding.UTF8.GetBytes("Hello");
        byte[] payload = Crypto.Sha256(data);
        requestMessage = mb.CreatePingRequest(payload);

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

        Passed = idOk && statusOk && disconnectedOk;

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
