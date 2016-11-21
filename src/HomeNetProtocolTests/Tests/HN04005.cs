using Google.Protobuf;
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
  /// HN04005 - Cancel Home Node Agreement, Register Again and Check-In
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN04.md#hn04005---cancel-home-node-agreement-register-again-and-checks-in
  /// </summary>
  public class HN04005 : ProtocolTest
  {
    public const string TestName = "HN04005";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
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
      IPAddress NodeIp = (IPAddress)ArgumentValues["Node IP"];
      int ClNonCustomerPort = (int)ArgumentValues["clNonCustomer Port"];
      int ClCustomerPort = (int)ArgumentValues["clCustomer Port"];
      log.Trace("(NodeIp:'{0}',ClNonCustomerPort:{1},ClCustomerPort:{2})", NodeIp, ClNonCustomerPort, ClCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(NodeIp, ClNonCustomerPort, true);
        bool establishHomeNodeOk = await client.EstablishHomeNodeAsync();

        // Step 1 Acceptance
        bool step1Ok = establishHomeNodeOk;
        client.CloseConnection();


        // Step 2
        await client.ConnectAsync(NodeIp, ClCustomerPort, true);
        bool checkInOk = await client.CheckInAsync();

        Message requestMessage = mb.CreateCancelHomeNodeAgreementRequest(null);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        bool cancelHomeNodeAgreementOk = idOk && statusOk;

        // Step 2 Acceptance
        bool step2Ok = checkInOk && cancelHomeNodeAgreementOk;

        client.CloseConnection();

        // Step 3
        await client.ConnectAsync(NodeIp, ClCustomerPort, true);
        bool startConversationOk = await client.StartConversationAsync();

        requestMessage = mb.CreateCheckInRequest(client.Challenge);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorNotFound;
        checkInOk = idOk && statusOk;

        // Step 3 Acceptance
        bool step3Ok = startConversationOk && checkInOk;

        client.CloseConnection();



        // Step 4
        await client.ConnectAsync(NodeIp, ClNonCustomerPort, true);
        establishHomeNodeOk = await client.EstablishHomeNodeAsync();

        // Step 4 Acceptance
        bool step4Ok = establishHomeNodeOk;
        client.CloseConnection();



        // Step 5
        await client.ConnectAsync(NodeIp, ClCustomerPort, true);
        checkInOk = await client.CheckInAsync();

        // Step 5 Acceptance
        bool step5Ok = checkInOk;


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok;

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
