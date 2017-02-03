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
  /// PS09002 - CAN Store Data and IPNS Record
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS09.md#ps09002---can-store-data-and-ipns-record
  /// </summary>
  public class PS09002 : ProtocolTest
  {
    public const string TestName = "PS09002";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("CAN Port", ProtocolTestArgumentType.Port),
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
      int CanPort = (int)ArgumentValues["CAN Port"];
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1},CanPort:{2})", ServerIp, PrimaryPort, CanPort);

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
        byte[] serverId = Crypto.Sha256(client.ServerKey);
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
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool canStoreDataOk = idOk && statusOk;

        byte[] objectHash1 = responseMessage.Response.ConversationResponse.CanStoreData.Hash.ToByteArray();


        string objectPath1 = client.CreateIpfsPathFromHash(objectHash1);
        log.Trace("Object path 1 is '{0}'.", objectPath1);

        string validityString = DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", DateTimeFormatInfo.InvariantInfo);
        CanIpnsEntry ipnsRecord = new CanIpnsEntry()
        {
          Value = ProtocolHelper.ByteArrayToByteString(Encoding.UTF8.GetBytes(objectPath1)),
          ValidityType = CanIpnsEntry.Types.ValidityType.Eol,
          Validity = ProtocolHelper.ByteArrayToByteString(Encoding.UTF8.GetBytes(validityString)),
          Sequence = 1,
          Ttl = 6000000000,
        };
        ipnsRecord.Signature = ProtocolHelper.ByteArrayToByteString(client.CreateIpnsRecordSignature(ipnsRecord));

        requestMessage = mb.CreateCanPublishIpnsRecordRequest(ipnsRecord);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        bool canPublishIpnsRecordOk = idOk && statusOk;



        // Step 2 Acceptance
        bool step2Ok = canStoreDataOk && canPublishIpnsRecordOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");




        // Step 3
        log.Trace("Step 3");
        IPEndPoint canEndPoint = new IPEndPoint(ServerIp, CanPort);
        byte[] clientCanId = client.CanPublicKeyToId(client.GetIdentityKeys().PublicKey);
        string ipnsPath = client.CreateIpnsPathFromHash(clientCanId);

        ProtocolClient.CanIpnsResolveResult canIpnsResolveResult = await client.CanIpnsResolve(canEndPoint, ipnsPath);
        string canObjectPath = canIpnsResolveResult.Path;
        bool objectPathOk = canObjectPath == objectPath1;
        bool resolveOk = canIpnsResolveResult.Success && objectPathOk;

        ProtocolClient.CanCatResult canCatResult = await client.CanGetObject(canEndPoint, canObjectPath);
        byte[] receivedData = canCatResult.Data;
        byte[] expectedData = identityData1.ToByteArray();
        bool dataOk = StructuralComparisons.StructuralComparer.Compare(receivedData, expectedData) == 0;
        bool catOk = canCatResult.Success && dataOk;

        // Step 3 Acceptance
        bool step3Ok = resolveOk && catOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        // Step 4
        log.Trace("Step 4");

        ipnsRecord.Sequence = 2;
        ipnsRecord.Signature = ProtocolHelper.ByteArrayToByteString(client.CreateIpnsRecordSignature(ipnsRecord));

        requestMessage = mb.CreateCanPublishIpnsRecordRequest(ipnsRecord);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        canPublishIpnsRecordOk = idOk && statusOk;


        canIpnsResolveResult = await client.CanIpnsResolve(canEndPoint, ipnsPath);
        canObjectPath = canIpnsResolveResult.Path;
        objectPathOk = canObjectPath == objectPath1;
        resolveOk = canIpnsResolveResult.Success && objectPathOk;

        // Step 4 Acceptance
        bool step4Ok = canPublishIpnsRecordOk && resolveOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");


        // Step 5
        log.Trace("Step 5");

        byte[] valX = new byte[50000];
        for (int i = 0; i < valX.Length; i++)
          valX[i] = 0x30;

        clientData = new List<CanKeyValue>()
        {
          new CanKeyValue() { Key = "key1", StringValue = "value 1" },
          new CanKeyValue() { Key = "key2", Uint32Value = 3 },
          new CanKeyValue() { Key = "key3", BoolValue = false },
          new CanKeyValue() { Key = "keyX", BinaryValue = ProtocolHelper.ByteArrayToByteString(valX) },
        };

        CanIdentityData identityData2 = new CanIdentityData()
        {
          HostingServerId = ProtocolHelper.ByteArrayToByteString(serverId)
        };
        identityData2.KeyValueList.AddRange(clientData);


        requestMessage = mb.CreateCanStoreDataRequest(identityData2);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        canStoreDataOk = idOk && statusOk;

        byte[] objectHash2 = responseMessage.Response.ConversationResponse.CanStoreData.Hash.ToByteArray();


        string objectPath2 = client.CreateIpfsPathFromHash(objectHash2);
        log.Trace("Object path 2 is '{0}'.", objectPath2);

        ipnsRecord.Sequence = 3;
        ipnsRecord.Value = ProtocolHelper.ByteArrayToByteString(Encoding.UTF8.GetBytes(objectPath2));
        ipnsRecord.Signature = ProtocolHelper.ByteArrayToByteString(client.CreateIpnsRecordSignature(ipnsRecord));

        requestMessage = mb.CreateCanPublishIpnsRecordRequest(ipnsRecord);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        canPublishIpnsRecordOk = idOk && statusOk;
        

        // Step 5 Acceptance
        bool step5Ok = canStoreDataOk && canPublishIpnsRecordOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");


        // Step 6
        log.Trace("Step 6");

        await Task.Delay(10000);
        canIpnsResolveResult = await client.CanIpnsResolve(canEndPoint, ipnsPath);
        canObjectPath = canIpnsResolveResult.Path;
        objectPathOk = canObjectPath == objectPath2;
        resolveOk = canIpnsResolveResult.Success && objectPathOk;

        canCatResult = await client.CanGetObject(canEndPoint, canObjectPath);
        receivedData = canCatResult.Data;
        expectedData = identityData2.ToByteArray();
        dataOk = StructuralComparisons.StructuralComparer.Compare(receivedData, expectedData) == 0;
        catOk = canCatResult.Success && dataOk;

        // Step 6 Acceptance
        bool step6Ok = resolveOk && catOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");

        requestMessage = mb.CreateCanStoreDataRequest(null);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        canStoreDataOk = idOk && statusOk;

        ProtocolClient.CanDeleteResult canDeleteResult = await client.CanDeleteObject(canEndPoint, canObjectPath);
        bool pinsOk = (canDeleteResult.Pins == null) || (canDeleteResult.Pins.Length == 0);
        bool deleteOk = canDeleteResult.Success && pinsOk;

        // Step 7 Acceptance
        bool step7Ok = canStoreDataOk && deleteOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");



        // Step 8
        log.Trace("Step 8");

        canDeleteResult = await client.CanDeleteObject(canEndPoint, objectPath1);
        pinsOk = (canDeleteResult.Pins == null) || (canDeleteResult.Pins.Length == 0);
        deleteOk = canDeleteResult.Success && pinsOk;

        // Step 8 Acceptance
        bool step8Ok = deleteOk;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");



        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok;

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
