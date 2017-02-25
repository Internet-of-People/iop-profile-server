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
  /// PS05019 - Application Service Callee Disconnects Administrative Connection
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS05.md#ps05019---application-service-callee-disconnects-administrative-connection
  /// </summary>
  public class PS05019 : ProtocolTest
  {
    public const string TestName = "PS05019";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

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
        Message serverRequestMessage = await clientCallee.ReceiveMessageAsync();

        byte[] receivedPubKey = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CallerPublicKey.ToByteArray();
        bool pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller) == 0;
        bool serviceNameOk = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.ServiceName == serviceName;

        bool incomingCallNotificationOk = pubKeyOk && serviceNameOk;

        byte[] calleeToken = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();

        Message serverResponseMessage = mbCallee.CreateIncomingCallNotificationResponse(serverRequestMessage);
        await clientCallee.SendMessageAsync(serverResponseMessage);

        clientCallee.CloseConnection();

        await Task.Delay(3000);

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

        clientCaller.CloseConnection();


        // Step 4 Acceptance
        bool step4Ok = callIdentityOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");

        await Task.Delay(3000);



        // Step 5
        log.Trace("Step 5");

        // Connect to clAppService and send initialization message.
        await clientCalleeAppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);

        Message requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, null);
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);


        // Step 5 Acceptance
        bool step5Ok = true;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");


        // Step 6
        log.Trace("Step 6");

        // Connect to clAppService and send initialization message.
        await clientCallerAppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);
        Message requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, null);
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);

        Message responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();

        idOk = responseMessageAppServiceCaller.Id == requestMessageAppServiceCaller.Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;

        bool initAppServiceMessageOk = idOk && statusOk;

        
        // Step 6 Acceptance
        bool step6Ok = initAppServiceMessageOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");



        // Step 7
        log.Trace("Step 7");
        Message responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;

        bool appServiceSendOk = idOk && statusOk;

        // Step 7 Acceptance
        bool step7Ok = appServiceSendOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");



        // Step 8
        log.Trace("Step 8");
        string callerMessage1 = "Message #1 to callee.";
        byte[] messageBytes = Encoding.UTF8.GetBytes(callerMessage1);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);
        uint callerMessage1Id = requestMessageAppServiceCaller.Id;

        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);


        // Step 8 Acceptance
        bool step8Ok = true;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");



        // Step 9
        log.Trace("Step 9");
        // Receive message #1.
        Message serverRequestAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        SemVer receivedVersion = new SemVer(serverRequestAppServiceCallee.Request.SingleRequest.Version);
        bool versionOk = receivedVersion.Equals(SemVer.V100);

        bool typeOk = (serverRequestAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (serverRequestAppServiceCallee.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (serverRequestAppServiceCallee.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        string receivedMessage = Encoding.UTF8.GetString(serverRequestAppServiceCallee.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        bool messageOk = receivedMessage == callerMessage1;

        bool receiveMessageOk = versionOk && typeOk && messageOk;


        // ACK message #1.
        Message serverResponseAppServiceCallee = mbCalleeAppService.CreateApplicationServiceReceiveMessageNotificationResponse(serverRequestAppServiceCallee);
        await clientCalleeAppService.SendMessageAsync(serverResponseAppServiceCallee);


        // Send our message #1.
        string calleeMessage1 = "Message #1 to CALLER.";
        messageBytes = Encoding.UTF8.GetBytes(calleeMessage1);
        requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, messageBytes);
        uint calleeMessage1Id = requestMessageAppServiceCallee.Id;
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);


        // Step 9 Acceptance
        bool step9Ok = receiveMessageOk;

        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");



        
        // Step 10
        log.Trace("Step 10");

        // Receive ACK message #1.
        responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCaller.Id == callerMessage1Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;
        receivedVersion = new SemVer(responseMessageAppServiceCaller.Response.SingleResponse.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        bool receiveAckOk = idOk && statusOk && versionOk;

        
        // Receive message #1 from callee.
        Message serverRequestAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        receivedVersion = new SemVer(serverRequestAppServiceCaller.Request.SingleRequest.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        typeOk = (serverRequestAppServiceCaller.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (serverRequestAppServiceCaller.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (serverRequestAppServiceCaller.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        receivedMessage = Encoding.UTF8.GetString(serverRequestAppServiceCaller.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        messageOk = receivedMessage == calleeMessage1;

        receiveMessageOk = versionOk && typeOk && messageOk;


        // ACK message #1 from callee.
        Message serverResponseAppServiceCaller = mbCallerAppService.CreateApplicationServiceReceiveMessageNotificationResponse(serverRequestAppServiceCaller);
        await clientCallerAppService.SendMessageAsync(serverResponseAppServiceCaller);


        // Step 10 Acceptance
        bool step10Ok = receiveAckOk && receiveMessageOk;

        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");




        // Step 11
        log.Trace("Step 11");

        // Receive ACK message #1.
        responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == calleeMessage1Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;
        receivedVersion = new SemVer(responseMessageAppServiceCallee.Response.SingleResponse.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        receiveAckOk = idOk && statusOk && versionOk;

        // Step 10 Acceptance
        bool step11Ok = receiveAckOk && receiveMessageOk;

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
