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
  /// PS06002 - Profile Search - Empty Database
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS06.md#ps06002---profile-search---empty-database
  /// </summary>
  public class PS06002 : ProtocolTest
  {
    public const string TestName = "PS06002";
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }

    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress ServerIp = (IPAddress)ArgumentValues["Server IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1})", ServerIp, PrimaryPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        PsMessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListServerPorts(rolePorts);
        client.CloseConnection();

        // Start conversation.
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool startConversationOk = await client.StartConversationAsync();

        // Search profile request.
        PsProtocolMessage requestMessage = mb.CreateProfileSearchRequest(null, null, null);
        await client.SendMessageAsync(requestMessage);

        PsProtocolMessage responseMessage = await client.ReceiveMessageAsync();
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
