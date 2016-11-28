using Google.Protobuf;
using HomeNetCrypto;
using HomeNetProtocol;
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

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// PS04011 - Parallel Check-Ins
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS04.md#ps04011---parallel-check-ins
  /// </summary>
  public class HN04011 : ProtocolTest
  {
    public const string TestName = "PS04011";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("clNonCustomer Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("clCustomer Port", ProtocolTestArgumentType.Port),
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
      int ClCustomerPort = (int)ArgumentValues["clCustomer Port"];
      log.Trace("(ServerIp:'{0}',ClNonCustomerPort:{1},ClCustomerPort:{2})", ServerIp, ClNonCustomerPort, ClCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client1 = new ProtocolClient();
      ProtocolClient client2 = new ProtocolClient(0, SemVer.V100, client1.GetIdentityKeys());
      try
      {
        MessageBuilder mb1 = client1.MessageBuilder;

        // Step 1
        await client1.ConnectAsync(ServerIp, ClNonCustomerPort, true);
        bool establishHomeNodeOk = await client1.EstablishHomeNodeAsync();

        // Step 1 Acceptance
        bool step1Ok = establishHomeNodeOk;
        client1.CloseConnection();


        // Step 2
        await client1.ConnectAsync(ServerIp, ClCustomerPort, true);
        bool checkInOk = await client1.CheckInAsync();

        // Step 2 Acceptance
        bool step2Ok = checkInOk;


        // Step 3
        await client2.ConnectAsync(ServerIp, ClCustomerPort, true);
        checkInOk = await client2.CheckInAsync();

        // Step 3 Acceptance
        bool step3Ok = checkInOk;


        // Step 4
        byte[] payload = Encoding.UTF8.GetBytes("test");
        Message requestMessage = mb1.CreatePingRequest(payload);
        bool disconnectedOk = false;

        // We should be disconnected by now, so sending or receiving should throw.
        try
        {
          await client1.SendMessageAsync(requestMessage);
          await client1.ReceiveMessageAsync();
        }
        catch
        {
          log.Trace("Expected exception occurred.");
          disconnectedOk = true;
        }

        // Step 4 Acceptance
        bool step4Ok = disconnectedOk;


        Passed = step1Ok && step2Ok && step3Ok && step4Ok;

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
