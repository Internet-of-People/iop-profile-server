using Google.Protobuf;
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
  /// PS02023 - Profile Stats
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS02.md#ps02023---profile-stats
  /// </summary>
  public class HN02023 : ProtocolTest
  {
    public const string TestName = "PS02023";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("clNonCustomer Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Identity types for test identities.</summary>
    public static List<string> IdentityTypes = new List<string>()
    {
      "Type A",
      "Type A",
      "Type B",
      "Type Alpha",
      "Type Beta",
      "Type B",
      "Type A B",
      "Type B",
      "Type A B",
      "Type C",
    };


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
      List<ProtocolClient> testIdentities = new List<ProtocolClient>();
      for (int i = 0; i < IdentityTypes.Count; i++)
        testIdentities.Add(new ProtocolClient());
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        bool error = false;
        for (int i = 0; i < IdentityTypes.Count - 2; i++)
        {
          ProtocolClient cl = testIdentities[i];
          await cl.ConnectAsync(ServerIp, ClNonCustomerPort, true);
          if (!await cl.EstablishHomeNodeAsync(IdentityTypes[i]))
          {
            error = true;
            break;
          }
          cl.CloseConnection();
        }

        bool homeNodesOk = !error;

        await client.ConnectAsync(ServerIp, ClNonCustomerPort, true);
        Message requestMessage = mb.CreateProfileStatsRequest();
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool countOk = responseMessage.Response.SingleResponse.ProfileStats.Stats.Count == 5;

        List<ProfileStatsItem> controlList = new List<ProfileStatsItem>()
        {
          new ProfileStatsItem() { IdentityType = "Type A", Count = 2 },
          new ProfileStatsItem() { IdentityType = "Type B", Count = 3 },
          new ProfileStatsItem() { IdentityType = "Type Alpha", Count = 1 },
          new ProfileStatsItem() { IdentityType = "Type A B", Count = 1 },
          new ProfileStatsItem() { IdentityType = "Type Beta", Count = 1 },
        };

        foreach (ProfileStatsItem item in responseMessage.Response.SingleResponse.ProfileStats.Stats)
        {
          ProfileStatsItem controlItem = controlList.Find(i => (i.IdentityType == item.IdentityType) && (i.Count == item.Count));
          if (!controlList.Remove(controlItem))
          {
            error = true;
            break;
          }
        }

        bool contentOk = (controlList.Count == 0) && !error;

        // Step 1 Acceptance
        bool step1Ok = idOk && statusOk && countOk && contentOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        for (int i = IdentityTypes.Count - 2; i < IdentityTypes.Count; i++)
        {
          ProtocolClient cl = testIdentities[i];
          await cl.ConnectAsync(ServerIp, ClNonCustomerPort, true);
          if (!await cl.EstablishHomeNodeAsync(IdentityTypes[i]))
          {
            error = true;
            break;
          }
          cl.CloseConnection();
        }

        homeNodesOk = !error;

        requestMessage = mb.CreateProfileStatsRequest();
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        countOk = responseMessage.Response.SingleResponse.ProfileStats.Stats.Count == 6;

        controlList = new List<ProfileStatsItem>()
        {
          new ProfileStatsItem() { IdentityType = "Type A", Count = 2 },
          new ProfileStatsItem() { IdentityType = "Type B", Count = 3 },
          new ProfileStatsItem() { IdentityType = "Type Alpha", Count = 1 },
          new ProfileStatsItem() { IdentityType = "Type A B", Count = 2 },
          new ProfileStatsItem() { IdentityType = "Type Beta", Count = 1 },
          new ProfileStatsItem() { IdentityType = "Type C", Count = 1 },
        };

        foreach (ProfileStatsItem item in responseMessage.Response.SingleResponse.ProfileStats.Stats)
        {
          ProfileStatsItem controlItem = controlList.Find(i => (i.IdentityType == item.IdentityType) && (i.Count == item.Count));
          if (!controlList.Remove(controlItem))
          {
            error = true;
            break;
          }
        }

        contentOk = (controlList.Count == 0) && !error;

        // Step 2 Acceptance
        bool step2Ok = idOk && statusOk && countOk && contentOk;

        log.Trace("Step 2: {0}", step1Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      foreach (ProtocolClient cl in testIdentities)
        cl.Dispose();

      client.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
