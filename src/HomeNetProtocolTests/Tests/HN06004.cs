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
  /// HN06004 - Profile Search - Invalid Queries
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn06004---profile-search---invalid-queries
  /// </summary>
  public class HN06004 : ProtocolTest
  {
    public const string TestName = "HN06004";
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

        // Search profile requests.
        Message requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 0, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "maxResponseRecordCount";

        bool query1Ok = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 200, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "maxResponseRecordCount";

        bool query2Ok = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 1200, 2000, false, false);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "maxResponseRecordCount";

        bool query3Ok = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 50, 25, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "maxResponseRecordCount";

        bool query4Ok = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 50, 1001, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "maxTotalRecordCount";

        bool query5Ok = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateProfileSearchRequest(null, null, null, null, 0, 50, 10010, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "maxTotalRecordCount";

        bool query6Ok = idOk && statusOk && detailsOk;


        string type = new string('a', 70);
        requestMessage = mb.CreateProfileSearchRequest(type, null, null, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "type";

        bool query7Ok = idOk && statusOk && detailsOk;

        type = new string('ɐ', 50);
        requestMessage = mb.CreateProfileSearchRequest(type, null, null, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "type";

        bool query8Ok = idOk && statusOk && detailsOk;


        string name = new string('a', 70);
        requestMessage = mb.CreateProfileSearchRequest(null, name, null, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "name";

        bool query9Ok = idOk && statusOk && detailsOk;


        name = new string('ɐ', 50);
        requestMessage = mb.CreateProfileSearchRequest(null, name, null, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "name";

        bool query10Ok = idOk && statusOk && detailsOk;


        GpsLocation loc = new GpsLocation(-90000001, 1);
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, loc, 1, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "latitude";

        bool query11Ok = idOk && statusOk && detailsOk;


        loc = new GpsLocation(90000001, 1);
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, loc, 1, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "latitude";

        bool query12Ok = idOk && statusOk && detailsOk;


        loc = new GpsLocation(1, -180000000);
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, loc, 1, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "longitude";

        bool query13Ok = idOk && statusOk && detailsOk;


        loc = new GpsLocation(1, 180000001);
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, loc, 1, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "longitude";

        bool query14Ok = idOk && statusOk && detailsOk;

        loc = new GpsLocation(1, 1);
        requestMessage = mb.CreateProfileSearchRequest(null, null, null, loc, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "radius";

        bool query15Ok = idOk && statusOk && detailsOk;

        string extraData = new string('a', 300);
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query16Ok = idOk && statusOk && detailsOk;


        extraData = new string('ɐ', 150);
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query17Ok = idOk && statusOk && detailsOk;


        extraData = @"(^|;)key=([^=]+;)?va(?'alpha')lue($|,|;)";
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query18Ok = idOk && statusOk && detailsOk;


        extraData = @"iuawhefiuhawef\aaerwergj";
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query19Ok = idOk && statusOk && detailsOk;


        extraData = @"aerghearg\beraarg";
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query20Ok = idOk && statusOk && detailsOk;


        extraData = @"afewafawefwaef\";
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query21Ok = idOk && statusOk && detailsOk;


        extraData = @"(^|;)key=([^=]+;)?(?<double>A)B<double>($|,|;)";
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query22Ok = idOk && statusOk && detailsOk;


        extraData = @"(^|;)key=rai??n($|,|;)";
        requestMessage = mb.CreateProfileSearchRequest(null, null, extraData, null, 0, 100, 1000, false, true);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool query23Ok = idOk && statusOk && detailsOk;


        // Step 1 Acceptance
        bool step1Ok = listPortsOk && startConversationOk && query1Ok && query2Ok && query3Ok && query4Ok && query5Ok && query6Ok
          && query7Ok && query8Ok && query9Ok && query10Ok && query11Ok && query12Ok && query13Ok && query14Ok && query15Ok && query16Ok
          && query17Ok && query18Ok && query19Ok && query20Ok && query21Ok && query22Ok && query23Ok;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 1
        log.Trace("Step 2");

        requestMessage = mb.CreateProfileSearchPartRequest(10, 20);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorNotAvailable;

        // Step 2 Acceptance
        bool step2Ok = idOk && statusOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");


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
