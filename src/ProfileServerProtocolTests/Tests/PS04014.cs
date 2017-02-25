using Google.Protobuf;
using ProfileServerCrypto;
using ProfileServerProtocol;
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

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS04014 - Check-In - Invalid Signature 2
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS04.md#ps04014---check-in---invalid-signature-2
  /// </summary>
  public class PS04014 : ProtocolTest
  {
    public const string TestName = "PS04014";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

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

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        await client.ConnectAsync(ServerIp, ClNonCustomerPort, true);
        bool establishHostingOk = await client.EstablishHostingAsync();

        // Step 1 Acceptance
        bool step1Ok = establishHostingOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");

        client.CloseConnection();


        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(ServerIp, ClCustomerPort, true);
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
