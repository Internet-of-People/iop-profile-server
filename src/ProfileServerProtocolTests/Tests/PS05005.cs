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
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS05005 - Disconnection of Inactive TCP Client from AppService Port
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS05.md#ps05005---disconnection-of-inactive-tcp-client-from-appservice-port
  /// </summary>
  public class PS05005 : ProtocolTest
  {
    public const string TestName = "PS05005";
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

      ProtocolClient clientCallee = new ProtocolClient();
      ProtocolClient clientCalleeAppService = new ProtocolClient(0, SemVer.V100, clientCallee.GetIdentityKeys());

      ProtocolClient clientCaller = new ProtocolClient();
      ProtocolClient clientCallerAppService = new ProtocolClient(0, SemVer.V100, clientCaller.GetIdentityKeys());
      try
      {
        MessageBuilder mbCallee = clientCallee.MessageBuilder;
        MessageBuilder mbCalleeAppService = clientCalleeAppService.MessageBuilder;

        MessageBuilder mbCaller = clientCaller.MessageBuilder;
        MessageBuilder mbCallerAppService = clientCallerAppService.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        byte[] pubKeyCallee = clientCallee.GetIdentityKeys().PublicKey;
        byte[] identityIdCallee = clientCallee.GetIdentityId();

        byte[] pubKeyCaller = clientCaller.GetIdentityKeys().PublicKey;
        byte[] identityIdCaller = clientCaller.GetIdentityId();


        await clientCallee.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await clientCallee.ListServerPorts(rolePorts);

        clientCallee.CloseConnection();


        // Establish hosting agreement for identity 1.
        await clientCallee.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool establishHostingOk = await clientCallee.EstablishHostingAsync();

        clientCallee.CloseConnection();


        // Check-in and initialize the profile of identity 1.

        await clientCallee.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
        bool checkInOk = await clientCallee.CheckInAsync();
        bool initializeProfileOk = await clientCallee.InitializeProfileAsync("Test Identity", null, new GpsLocation(0, 0), null);


        // Add application service to the current session.
        string serviceName = "Test Service";
        bool addAppServiceOk = await clientCallee.AddApplicationServicesAsync(new List<string>() { serviceName });


        // Step 1 Acceptance
        bool step1Ok = listPortsOk && establishHostingOk && checkInOk && initializeProfileOk && addAppServiceOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await clientCaller.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool verifyIdentityOk = await clientCaller.VerifyIdentityAsync();

        Message requestMessage = mbCaller.CreateCallIdentityApplicationServiceRequest(identityIdCallee, serviceName);
        await clientCaller.SendMessageAsync(requestMessage);


        // Step 2 Acceptance
        bool step2Ok = verifyIdentityOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");
        Message nodeRequestMessage = await clientCallee.ReceiveMessageAsync();

        byte[] receivedPubKey = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CallerPublicKey.ToByteArray();
        bool pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller) == 0;
        bool serviceNameOk = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.ServiceName == serviceName;

        bool incomingCallNotificationOk = pubKeyOk && serviceNameOk;

        byte[] calleeToken = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();

        Message nodeResponseMessage = mbCallee.CreateIncomingCallNotificationResponse(nodeRequestMessage);
        await clientCallee.SendMessageAsync(nodeResponseMessage);


        // Connect to clAppService and send initialization message.
        await clientCalleeAppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);

        Message requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, null);
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);


        // Step 3 Acceptance
        bool step3Ok = incomingCallNotificationOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        // Step 4
        log.Trace("Step 4");
        Message responseMessage = await clientCaller.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        byte[] callerToken = responseMessage.Response.ConversationResponse.CallIdentityApplicationService.CallerToken.ToByteArray();

        bool callIdentityOk = idOk && statusOk;

        // Connect to clAppService and send initialization message.
        await clientCallerAppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);
        Message requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, null);
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);

        Message responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();

        idOk = responseMessageAppServiceCaller.Id == requestMessageAppServiceCaller.Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;

        bool initAppServiceMessageOk = idOk && statusOk;

        // And close connection to clNonCustomer port.
        clientCaller.CloseConnection();


        // Step 4 Acceptance
        bool step4Ok = callIdentityOk && initAppServiceMessageOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");



        // Step 5
        log.Trace("Step 5");
        Message responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;

        bool typeOk = (responseMessageAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          && (responseMessageAppServiceCallee.Response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
          && (responseMessageAppServiceCallee.Response.SingleResponse.ResponseTypeCase == SingleResponse.ResponseTypeOneofCase.ApplicationServiceSendMessage);

        bool appServiceSendOk = idOk && statusOk && typeOk;

        // Step 5 Acceptance
        bool step5Ok = appServiceSendOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");



        // Step 6
        log.Trace("Step 6");
        string callerMessage1 = "Message #1 to callee.";
        byte[] messageBytes = Encoding.UTF8.GetBytes(callerMessage1);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);
        uint callerMessage1Id = requestMessageAppServiceCaller.Id;
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);


        // Step 6 Acceptance
        bool step6Ok = true;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");
        // Receive message #1.
        Message nodeRequestAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        SemVer receivedVersion = new SemVer(nodeRequestAppServiceCallee.Request.SingleRequest.Version);
        bool versionOk = receivedVersion.Equals(SemVer.V100);

        typeOk = (nodeRequestAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (nodeRequestAppServiceCallee.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (nodeRequestAppServiceCallee.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        string receivedMessage = Encoding.UTF8.GetString(nodeRequestAppServiceCallee.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        bool messageOk = receivedMessage == callerMessage1;

        bool receiveMessageOk = versionOk && typeOk && messageOk;


        // ACK message #1.
        Message nodeResponseAppServiceCallee = mbCalleeAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCallee);
        await clientCalleeAppService.SendMessageAsync(nodeResponseAppServiceCallee);


        // Send our message #1.
        string calleeMessage1 = "Message #1 to CALLER.";
        messageBytes = Encoding.UTF8.GetBytes(calleeMessage1);
        requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, messageBytes);
        uint calleeMessage1Id = requestMessageAppServiceCallee.Id;
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);



        // Step 7 Acceptance
        bool step7Ok = receiveMessageOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        // Step 8 
        log.Trace("Step 8");
        // Receive ACK message #1.
        responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCaller.Id == callerMessage1Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;
        receivedVersion = new SemVer(responseMessageAppServiceCaller.Response.SingleResponse.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        bool receiveAckOk = idOk && statusOk && versionOk;


        // Receive message #1 from callee.
        Message nodeRequestAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        receivedVersion = new SemVer(nodeRequestAppServiceCaller.Request.SingleRequest.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        typeOk = (nodeRequestAppServiceCaller.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (nodeRequestAppServiceCaller.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (nodeRequestAppServiceCaller.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        receivedMessage = Encoding.UTF8.GetString(nodeRequestAppServiceCaller.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        messageOk = receivedMessage == calleeMessage1;

        receiveMessageOk = versionOk && typeOk && messageOk;


        // ACK message #1 from callee.
        Message nodeResponseAppServiceCaller = mbCallerAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCaller);
        await clientCallerAppService.SendMessageAsync(nodeResponseAppServiceCaller);


        // Step 8 Acceptance
        bool step8Ok = receiveAckOk && receiveMessageOk;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");


        // Step 9
        log.Trace("Step 9");
        // Receive ACK message #1.
        responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == calleeMessage1Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;
        receivedVersion = new SemVer(responseMessageAppServiceCallee.Response.SingleResponse.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        receiveAckOk = idOk && statusOk && versionOk;

        log.Trace("Going to wait for 3 minutes...");
        await Task.Delay(180 * 1000);
        log.Trace("Waiting done.");


        // Step 9 Acceptance
        bool step9Ok = receiveAckOk;

        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");


        // Step 10
        string callerMessage2 = "Message #1 to callee.";
        messageBytes = Encoding.UTF8.GetBytes(callerMessage2);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);

        // We should be disconnected now, send or receive should throw.
        bool disconnectedOk = false;
        try
        {
          await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);
          await clientCallerAppService.ReceiveMessageAsync();
        }
        catch
        {
          log.Trace("Expected exception occurred.");
          disconnectedOk = true;
        }

        // Step 10 Acceptance
        bool step10Ok = disconnectedOk;

        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");



        // Step 11
        string calleeMessage2 = "Message #1 to CALLER.";
        messageBytes = Encoding.UTF8.GetBytes(calleeMessage2);
        requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, messageBytes);

        // We should be disconnected now, send or receive should throw.
        disconnectedOk = false;
        try
        {
          await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);
          await clientCalleeAppService.ReceiveMessageAsync();
        }
        catch
        {
          log.Trace("Expected exception occurred.");
          disconnectedOk = true;
        }

        // Step 11 Acceptance
        bool step11Ok = disconnectedOk;

        log.Trace("Step 11: {0}", step11Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok && step10Ok && step11Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      clientCallee.Dispose();
      clientCalleeAppService.Dispose();
      clientCaller.Dispose();
      clientCallerAppService.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
