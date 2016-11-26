using Google.Protobuf;
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
  /// HN02004 - Start Conversation - Unsupported Version
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN02.md#hn02004---start-conversation---unsupported-version
  /// </summary>
  public class HN02004 : ProtocolTest
  {
    public const string TestName = "HN02004";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("clNonCustomer Port", ProtocolTestArgumentType.Port),
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
      log.Trace("(NodeIp:'{0}',ClNonCustomerPort:{1})", NodeIp, ClNonCustomerPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(NodeIp, ClNonCustomerPort, true);

        Message requestMessage = client.CreateStartConversationRequest();
        ByteString myKey = requestMessage.Request.ConversationRequest.Start.PublicKey;
        ByteString myChallenge = requestMessage.Request.ConversationRequest.Start.ClientChallenge;
        requestMessage.Request.ConversationRequest.Start = new StartConversationRequest();
        requestMessage.Request.ConversationRequest.Start.PublicKey = myKey;
        requestMessage.Request.ConversationRequest.Start.ClientChallenge = myChallenge;
        requestMessage.Request.ConversationRequest.Start.SupportedVersions.Add(new SemVer(255, 255, 255).ToByteString());
        requestMessage.Request.ConversationRequest.Start.SupportedVersions.Add(new SemVer(255, 255, 254).ToByteString());

        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        // Step 1 Acceptance
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorUnsupported;

        Passed = idOk && statusOk;

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
