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
  /// HN04014 - Check-In - Invalid Signature 2
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn04014---check-in---invalid-signature-2
  /// </summary>
  public class HN04014 : ProtocolTest
  {
    public const string TestName = "HN04014";
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
        log.Trace("Step 1");
        await client.ConnectAsync(NodeIp, ClNonCustomerPort, true);
        bool establishHomeNodeOk = await client.EstablishHomeNodeAsync();

        // Step 1 Acceptance
        bool step1Ok = establishHomeNodeOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");

        client.CloseConnection();


        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(NodeIp, ClCustomerPort, true);
        bool startConversationOk = await client.StartConversationAsync();

        Message requestMessage = mb.CreateCheckInRequest(client.Challenge);
        // Invalidate the signature.
        byte[] signature = requestMessage.Request.ConversationRequest.Signature.ToByteArray();
        byte[] sig32 = new byte[32];
        Array.Copy(signature, sig32, sig32.Length);
        requestMessage.Request.ConversationRequest.Signature = ProtocolHelper.ByteArrayToByteString(sig32);

        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidSignature;
        bool checkInOk = idOk && statusOk;

        // Step 2 Acceptance
        bool step2Ok = startConversationOk && checkInOk;
        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");

        Passed = step1Ok && step2Ok;

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
