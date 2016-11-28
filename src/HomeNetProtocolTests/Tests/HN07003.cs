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
  /// HN07003 - Add Related Identity - Quota Exceeded
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN07.md#hn07003---add-related-identity---quota-exceeded
  /// </summary>
  public class HN07003 : ProtocolTest
  {
    public const string TestName = "HN07003";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Total add relation requests to be sent to the node.</summary>
    public const int RequestCount = 101;

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
      ProtocolClient issuer = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
    
        // Get port list.
        await client.ConnectAsync(NodeIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListNodePorts(rolePorts);
        client.CloseConnection();

        // Establish home node agreement for primary client.
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool establishHomeNodeOk = await client.EstablishHomeNodeAsync("Primary");
        client.CloseConnection();

        // Check in primary client.
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
        bool checkInOk = await client.CheckInAsync();

        // Step 1 Acceptance
        bool step1Ok = listPortsOk && establishHomeNodeOk && checkInOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");

        byte[] primaryPubKey = client.GetIdentityKeys().PublicKey;
        string type = "Card Type A";

        DateTime validFrom = ProtocolHelper.UnixTimestampMsToDateTime(1479220556000);
        DateTime validTo = ProtocolHelper.UnixTimestampMsToDateTime(2479220556000);

        bool reqOk = true;
        for (int i = 0; i < RequestCount; i++)
        {
          SignedRelationshipCard signedCard = issuer.IssueRelationshipCard(primaryPubKey, type, validFrom, validTo);

          byte[] applicationId = new byte[] { (byte)i };
          CardApplicationInformation cardApplication = client.CreateRelationshipCardApplication(applicationId, signedCard);

          Message requestMessage = mb.CreateAddRelatedIdentityRequest(cardApplication, signedCard);
          await client.SendMessageAsync(requestMessage);
          Message responseMessage = await client.ReceiveMessageAsync();

          bool idOk = responseMessage.Id == requestMessage.Id;
          bool statusOk = i < 100 ? (responseMessage.Response.Status == Status.Ok) : responseMessage.Response.Status == Status.ErrorQuotaExceeded;

          reqOk = idOk && statusOk;
          if (!reqOk)
            break;
        }

        // Step 2 Acceptance
        bool step2Ok = reqOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();
      issuer.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
