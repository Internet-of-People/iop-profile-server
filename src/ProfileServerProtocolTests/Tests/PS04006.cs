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

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS04006 - Home Node Agreement, Update Profile, Get Identity Information 
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS04.md#ps04006---home-node-agreement-update-profile-get-identity-information
  /// </summary>
  public class PS04006 : ProtocolTest
  {
    public const string TestName = "PS04006";
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
        byte[] testPubKey = client.GetIdentityKeys().PublicKey;
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

        Message requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "Test Identity", null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        bool updateProfileOk = idOk && statusOk;


        requestMessage = mb.CreateGetIdentityInformationRequest(testIdentityId);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        bool isHostedOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsHosted;
        bool isOnlineOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsOnline;

        byte[] receivedPubKey = responseMessage.Response.SingleResponse.GetIdentityInformation.IdentityPublicKey.ToByteArray();
        bool pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, testPubKey) == 0;
        SemVer receivedVersion = new SemVer(responseMessage.Response.SingleResponse.GetIdentityInformation.Version);
        bool versionOk = receivedVersion.Equals(SemVer.V100);

        bool nameOk = responseMessage.Response.SingleResponse.GetIdentityInformation.Name == "Test Identity";
        bool extraDataOk = responseMessage.Response.SingleResponse.GetIdentityInformation.ExtraData == "";
        bool locationOk = responseMessage.Response.SingleResponse.GetIdentityInformation.Latitude == 1
          && responseMessage.Response.SingleResponse.GetIdentityInformation.Longitude == 2;

        bool getIdentityInfoOk = idOk && statusOk && isHostedOk && isOnlineOk && pubKeyOk && versionOk && nameOk && extraDataOk && locationOk;


        byte[] imageData = File.ReadAllBytes(string.Format("images{0}PS04006.jpg", Path.DirectorySeparatorChar));
        requestMessage = mb.CreateUpdateProfileRequest(null, "Test Identity Renamed", imageData, new GpsLocation(-1, -2), "a=b");
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool updateProfileOk2 = idOk && statusOk;

        requestMessage = mb.CreateGetIdentityInformationRequest(testIdentityId, true, true, false);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        isHostedOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsHosted;
        isOnlineOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsOnline;

        receivedPubKey = responseMessage.Response.SingleResponse.GetIdentityInformation.IdentityPublicKey.ToByteArray();
        pubKeyOk = StructuralComparisons.StructuralComparer.Compare(testPubKey, receivedPubKey) == 0;
        receivedVersion = new SemVer(responseMessage.Response.SingleResponse.GetIdentityInformation.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        nameOk = responseMessage.Response.SingleResponse.GetIdentityInformation.Name == "Test Identity Renamed";
        extraDataOk = responseMessage.Response.SingleResponse.GetIdentityInformation.ExtraData == "a=b";
        locationOk = responseMessage.Response.SingleResponse.GetIdentityInformation.Latitude == -1
          && responseMessage.Response.SingleResponse.GetIdentityInformation.Longitude == -2;


        byte[] receivedProfileImage = responseMessage.Response.SingleResponse.GetIdentityInformation.ProfileImage.ToByteArray();
        bool profileImageOk = StructuralComparisons.StructuralComparer.Compare(receivedProfileImage, imageData) == 0;
        byte[] receivedThumbnailImage = responseMessage.Response.SingleResponse.GetIdentityInformation.ThumbnailImage.ToByteArray();
        bool thumbnailImageOk = receivedThumbnailImage.Length > 0;

        bool getIdentityInfoOk2 = idOk && statusOk && isHostedOk && isOnlineOk && pubKeyOk && versionOk && nameOk && extraDataOk && locationOk && profileImageOk && thumbnailImageOk;

        // Step 2 Acceptance
        bool step2Ok = checkInOk && updateProfileOk && getIdentityInfoOk && updateProfileOk2 && getIdentityInfoOk2;


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
