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
  /// PS04009 - Cancel Home Node Agreement - Redirection
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS04.md#ps04009---cancel-home-node-agreement---redirection
  /// </summary>
  public class HN04009 : ProtocolTest
  {
    public const string TestName = "PS04009";
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

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;
        byte[] testIdentityId = client.GetIdentityId();

        // Step 1
        await client.ConnectAsync(ServerIp, ClNonCustomerPort, true);
        bool establishHomeNodeOk = await client.EstablishHomeNodeAsync();

        // Step 1 Acceptance
        bool step1Ok = establishHomeNodeOk;
        client.CloseConnection();


        // Step 2
        await client.ConnectAsync(ServerIp, ClCustomerPort, true);
        bool checkInOk = await client.CheckInAsync();

        byte[] newNodeId = Crypto.Sha256(Encoding.UTF8.GetBytes("test"));
        Message requestMessage = mb.CreateCancelHostingAgreementRequest(newNodeId);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        bool cancelAgreementOk = idOk && statusOk;


        requestMessage = mb.CreateGetIdentityInformationRequest(testIdentityId);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        bool isHostedOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsHosted == false;
        bool isTargetHomeNodeKnownOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsTargetProfileServerKnown;
        byte[] receivedHomeNodeId = responseMessage.Response.SingleResponse.GetIdentityInformation.TargetProfileServerNetworkId.ToByteArray();
        bool targetHomeNodeIdOk = StructuralComparisons.StructuralComparer.Compare(receivedHomeNodeId, newNodeId) == 0;

        bool getIdentityInfoOk = idOk && statusOk && isHostedOk && isTargetHomeNodeKnownOk && targetHomeNodeIdOk;

        // Step 2 Acceptance
        bool step2Ok = checkInOk && cancelAgreementOk && getIdentityInfoOk;


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
