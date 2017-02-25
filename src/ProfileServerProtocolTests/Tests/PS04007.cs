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
  /// PS04007 - Update Profile - Invalid Initialization and Invalid Values
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS04.md#ps04007---update-profile---invalid-initialization-and-invalid-values
  /// </summary>
  public class PS04007 : ProtocolTest
  {
    public const string TestName = "PS04007";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

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
        bool establishHostingOk = await client.EstablishHostingAsync();

        // Step 1 Acceptance
        bool step1Ok = establishHostingOk;
        client.CloseConnection();


        // Step 2
        await client.ConnectAsync(ServerIp, ClCustomerPort, true);
        bool checkInOk = await client.CheckInAsync();

        Message requestMessage = mb.CreateUpdateProfileRequest(null, "Test Identity", null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "setVersion";

        bool updateProfileOk1 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, null, null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "setName";

        bool updateProfileOk2 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "Test Identity", null, null, null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "setLocation";

        bool updateProfileOk3 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateUpdateProfileRequest(new SemVer(0, 0, 0), "Test Identity", null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "version";

        bool updateProfileOk4 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateUpdateProfileRequest(new SemVer(255, 0, 0), "Test Identity", null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "version";

        bool updateProfileOk5 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "", null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "name";

        bool updateProfileOk6 = idOk && statusOk && detailsOk;

        string name = new string('a', 100);
        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, name, null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "name";

        bool updateProfileOk7 = idOk && statusOk && detailsOk;


        name = new string('ɐ', 50);
        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, name, null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "name";

        bool updateProfileOk8 = idOk && statusOk && detailsOk;

        
        byte[] imageData = File.ReadAllBytes(string.Format("images{0}PS04007-too-big.jpg", Path.DirectorySeparatorChar));
        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "Test Identity", imageData, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "image";

        bool updateProfileOk9 = idOk && statusOk && detailsOk;

        
        imageData = File.ReadAllBytes(string.Format("images{0}PS04007-not-image.jpg", Path.DirectorySeparatorChar));
        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "Test Identity", imageData, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "image";
        
        bool updateProfileOk10 = idOk && statusOk && detailsOk;

  
        string extraData = new string('a', 300);
        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "Test Identity", null, new GpsLocation(1, 2), extraData);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool updateProfileOk11 = idOk && statusOk && detailsOk;


        extraData = new string('ɐ', 150);
        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "Test Identity", null, new GpsLocation(1, 2), extraData);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "extraData";

        bool updateProfileOk12 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateUpdateProfileRequest(SemVer.V100, "Test Identity", null, new GpsLocation(1, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;

        bool updateProfileOk13 = idOk && statusOk;


        requestMessage = mb.CreateUpdateProfileRequest(null, null, null, null, null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "set*";

        bool updateProfileOk14 = idOk && statusOk && detailsOk;

        int badLatitude = (int)(GpsLocation.LatitudeMin * GpsLocation.LocationTypeFactor - 1);
        requestMessage = mb.CreateUpdateProfileRequest(null, null, null, new GpsLocation(badLatitude, 2), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "latitude";

        bool updateProfileOk15 = idOk && statusOk && detailsOk;


        int badLongitude = (int)(GpsLocation.LongitudeMax * GpsLocation.LocationTypeFactor + 1);
        requestMessage = mb.CreateUpdateProfileRequest(null, null, null, new GpsLocation(0, badLongitude), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "longitude";

        bool updateProfileOk16 = idOk && statusOk && detailsOk;

  
        requestMessage = mb.CreateUpdateProfileRequest(null, null, null, new GpsLocation(badLatitude, badLongitude), null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "latitude";

        bool updateProfileOk17 = idOk && statusOk && detailsOk;

        // Step 2 Acceptance
        
        bool step2Ok = checkInOk && updateProfileOk1 && updateProfileOk2 && updateProfileOk3 && updateProfileOk4 && updateProfileOk5 && updateProfileOk6 && updateProfileOk7 && updateProfileOk8
         && updateProfileOk9 && updateProfileOk10 && updateProfileOk11 && updateProfileOk12 && updateProfileOk13 && updateProfileOk14 && updateProfileOk15 && updateProfileOk16 && updateProfileOk17;


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
