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
  /// HN06002 - Profile Search - Empty Database
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn06002---profile-search---empty-database
  /// </summary>
  public class HN06002 : ProtocolTest
  {
    public const string TestName = "HN06002";
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
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(NodeIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListNodePorts(rolePorts);
        client.CloseConnection();

        // Start conversation.
        await client.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        // Search profile request.
        Message requestMessage = mb.CreateProfileSearchRequest(null, null, null);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;


        bool totalRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.TotalRecordCount == 0;
        bool maxResponseRecordCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.MaxResponseRecordCount == 100;
        bool profilesCountOk = responseMessage.Response.ConversationResponse.ProfileSearch.Profiles.Count == 0;

        // Step 1 Acceptance
        bool step1Ok = listPortsOk && startConversationOk && idOk && statusOk && totalRecordCountOk && maxResponseRecordCountOk && profilesCountOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");


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
