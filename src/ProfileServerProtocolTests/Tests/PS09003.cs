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
using System.Globalization;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS09003 - CAN Store Data - Invalid Hosting Server ID
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS09.md#ps09003---can-store-data---invalid-hosting-server-id
  /// </summary>
  public class PS09003 : ProtocolTest
  {
    public const string TestName = "PS09003";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

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
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListServerPorts(rolePorts);
        client.CloseConnection();

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool hostingOk = await client.EstablishHostingAsync("Test");
        client.CloseConnection();

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
        bool checkInOk = await client.CheckInAsync();

        bool step1Ok = listPortsOk && hostingOk && checkInOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        byte[] serverId = new byte[5] { 0x40, 0x40, 0x40, 0x40, 0x40 };
        List<CanKeyValue> clientData = new List<CanKeyValue>()
        {
          new CanKeyValue() { Key = "key1", StringValue = "value 1" },
          new CanKeyValue() { Key = "key2", Uint32Value = 2 },
          new CanKeyValue() { Key = "key3", BoolValue = true },
          new CanKeyValue() { Key = "key4", BinaryValue = ProtocolHelper.ByteArrayToByteString(new byte[] { 1, 2, 3 }) },
        };

        CanIdentityData identityData1 = new CanIdentityData()
        {
          HostingServerId = ProtocolHelper.ByteArrayToByteString(serverId)
        };
        identityData1.KeyValueList.AddRange(clientData);

        Message requestMessage = mb.CreateCanStoreDataRequest(identityData1);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "data.hostingServerId";

        // Step 2 Acceptance
        bool step2Ok = idOk && statusOk && detailsOk;

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
