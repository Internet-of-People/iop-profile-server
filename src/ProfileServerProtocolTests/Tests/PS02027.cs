using IopCommon;
using Google.Protobuf;
using IopCrypto;
using IopProtocol;
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
  /// PS02027 - Start Conversation - Invalid Public Key
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS02.md#ps02027---start-conversation---invalid-public-key
  /// </summary>
  public class PS02027 : ProtocolTest
  {
    public const string TestName = "PS02027";
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests." + TestName);

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
      int ClNonCustomerPort = (int)ArgumentValues["clNonCustomer Port"];
      log.Trace("(ServerIp:'{0}',ClNonCustomerPort:{1})", ServerIp, ClNonCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        PsMessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(ServerIp, ClNonCustomerPort, true);

        PsProtocolMessage requestMessage = client.CreateStartConversationRequest();
        byte[] badPubKey = new byte[0];
        requestMessage.Request.ConversationRequest.Start.PublicKey = ProtocolHelper.ByteArrayToByteString(badPubKey);
        await client.SendMessageAsync(requestMessage);
        PsProtocolMessage responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "publicKey";

        bool start1Ok = idOk && statusOk && detailsOk;


        requestMessage = client.CreateStartConversationRequest();
        badPubKey = new byte[130];
        for (int i = 0; i < badPubKey.Length; i++)
          badPubKey[i] = 0x40;

        requestMessage.Request.ConversationRequest.Start.PublicKey = ProtocolHelper.ByteArrayToByteString(badPubKey);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "publicKey";

        bool start2Ok = idOk && statusOk && detailsOk;

        // Step 1 Acceptance
        bool step1Ok = start1Ok && start2Ok;


        Passed = step1Ok;

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
