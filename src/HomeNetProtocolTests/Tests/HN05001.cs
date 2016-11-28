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
  /// HN05001 - Application Service Call
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn05001---application-service-call
  /// </summary>
  public class HN05001 : ProtocolTest
  {
    public const string TestName = "HN05001";
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

      ProtocolClient clientCallee = new ProtocolClient();
      ProtocolClient clientCalleeAppService = new ProtocolClient(0, new byte[] { 1, 0, 0 }, clientCallee.GetIdentityKeys());

      ProtocolClient clientCaller = new ProtocolClient();
      ProtocolClient clientCallerAppService = new ProtocolClient(0, new byte[] { 1, 0, 0 }, clientCaller.GetIdentityKeys());
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


        await clientCallee.ConnectAsync(NodeIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await clientCallee.ListNodePorts(rolePorts);

        clientCallee.CloseConnection();


        // Establish home node for identity 1.
        await clientCallee.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool establishHomeNodeOk = await clientCallee.EstablishHomeNodeAsync();

        clientCallee.CloseConnection();


        // Check-in and initialize the profile of identity 1.

        await clientCallee.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClCustomer], true);
        bool checkInOk = await clientCallee.CheckInAsync();
        bool initializeProfileOk = await clientCallee.InitializeProfileAsync("Test Identity", null, new GpsLocation(0, 0), null);


        // Add application service to the current session.
        string serviceName = "Test Service";
        bool addAppServiceOk = await clientCallee.AddApplicationServicesAsync(new List<string>() { serviceName });


        // Step 1 Acceptance
        bool step1Ok = listPortsOk && establishHomeNodeOk && checkInOk && initializeProfileOk && addAppServiceOk;

        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await clientCaller.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool verifyIdentityOk = await clientCaller.VerifyIdentityAsync();


        Message requestMessage = mbCaller.CreateGetIdentityInformationRequest(identityIdCallee, false, false, true);
        await clientCaller.SendMessageAsync(requestMessage);
        Message responseMessage = await clientCaller.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;

        bool isHostedOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsHosted;
        bool isOnlineOk = responseMessage.Response.SingleResponse.GetIdentityInformation.IsOnline;

        byte[] receivedPubKey = responseMessage.Response.SingleResponse.GetIdentityInformation.IdentityPublicKey.ToByteArray();
        bool pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCallee) == 0;

        HashSet<string> receivedAsList = new HashSet<string>(responseMessage.Response.SingleResponse.GetIdentityInformation.ApplicationServices);
        bool appServiceReadyOk = receivedAsList.Contains(serviceName);

        bool getInfoOk = idOk && statusOk && isHostedOk && isOnlineOk && pubKeyOk && appServiceReadyOk;


        // Step 2 Acceptance
        bool step2Ok = verifyIdentityOk && getInfoOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");
        requestMessage = mbCaller.CreateCallIdentityApplicationServiceRequest(identityIdCallee, serviceName);
        await clientCaller.SendMessageAsync(requestMessage);

        // Step 3 Acceptance
        bool step3Ok = true;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        // Step 4
        log.Trace("Step 4");
        Message nodeRequestMessage = await clientCallee.ReceiveMessageAsync();

        receivedPubKey = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CallerPublicKey.ToByteArray();
        pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller) == 0;
        bool serviceNameOk = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.ServiceName == serviceName;

        bool incomingCallNotificationOk = pubKeyOk && serviceNameOk;

        byte[] calleeToken = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();

        Message nodeResponseMessage = mbCallee.CreateIncomingCallNotificationResponse(nodeRequestMessage);
        await clientCallee.SendMessageAsync(nodeResponseMessage);


        // Connect to clAppService and send initialization message.
        await clientCalleeAppService.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClAppService], true);

        Message requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, null);
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);


        // Step 4 Acceptance
        bool step4Ok = incomingCallNotificationOk;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");


        // Step 5
        log.Trace("Step 5");
        responseMessage = await clientCaller.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        byte[] callerToken = responseMessage.Response.ConversationResponse.CallIdentityApplicationService.CallerToken.ToByteArray();

        bool callIdentityOk = idOk && statusOk;

        // Connect to clAppService and send initialization message.
        await clientCallerAppService.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClAppService], true);
        Message requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, null);
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);

        Message responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();

        idOk = responseMessageAppServiceCaller.Id == requestMessageAppServiceCaller.Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;

        bool initAppServiceMessageOk = idOk && statusOk;


        // And close connection to clNonCustomer port.
        clientCaller.CloseConnection();


        // Step 5 Acceptance
        bool step5Ok = callIdentityOk && initAppServiceMessageOk;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");



        // Step 6
        log.Trace("Step 6");
        Message responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;

        bool typeOk = (responseMessageAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          && (responseMessageAppServiceCallee.Response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
          && (responseMessageAppServiceCallee.Response.SingleResponse.ResponseTypeCase == SingleResponse.ResponseTypeOneofCase.ApplicationServiceSendMessage);

        bool appServiceSendOk = idOk && statusOk && typeOk;

        // Step 6 Acceptance
        bool step6Ok = appServiceSendOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");
        string callerMessage1 = "Message #1 to callee.";
        byte[] messageBytes = Encoding.UTF8.GetBytes(callerMessage1);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);
        uint callerMessage1Id = requestMessageAppServiceCaller.Id;
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);

        string callerMessage2 = "Message #2 to callee.";
        messageBytes = Encoding.UTF8.GetBytes(callerMessage2);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);
        uint callerMessage2Id = requestMessageAppServiceCaller.Id;
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);

        string callerMessage3 = "Message #3 to callee.";
        messageBytes = Encoding.UTF8.GetBytes(callerMessage3);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);
        uint callerMessage3Id = requestMessageAppServiceCaller.Id;
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);


        // Step 7 Acceptance
        bool step7Ok = true;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        // Step 8
        log.Trace("Step 8");
        // Receive message #1.
        Message nodeRequestAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        byte[] receivedVersion = nodeRequestAppServiceCallee.Request.SingleRequest.Version.ToByteArray();
        bool versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        typeOk = (nodeRequestAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (nodeRequestAppServiceCallee.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (nodeRequestAppServiceCallee.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        string receivedMessage = Encoding.UTF8.GetString(nodeRequestAppServiceCallee.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        bool messageOk = receivedMessage == callerMessage1;

        bool receiveMessageOk1 = versionOk && typeOk && messageOk;


        // ACK message #1.
        Message nodeResponseAppServiceCallee = mbCalleeAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCallee);
        await clientCalleeAppService.SendMessageAsync(nodeResponseAppServiceCallee);


        // Receive message #2.
        nodeRequestAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        receivedVersion = nodeRequestAppServiceCallee.Request.SingleRequest.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        typeOk = (nodeRequestAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (nodeRequestAppServiceCallee.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (nodeRequestAppServiceCallee.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        receivedMessage = Encoding.UTF8.GetString(nodeRequestAppServiceCallee.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        messageOk = receivedMessage == callerMessage2;

        bool receiveMessageOk2 = versionOk && typeOk && messageOk;


        // ACK message #2.
        nodeResponseAppServiceCallee = mbCalleeAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCallee);
        await clientCalleeAppService.SendMessageAsync(nodeResponseAppServiceCallee);


        // Send our message #1.
        string calleeMessage1 = "Message #1 to CALLER.";
        messageBytes = Encoding.UTF8.GetBytes(calleeMessage1);
        requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, messageBytes);
        uint calleeMessage1Id = requestMessageAppServiceCallee.Id;
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);


        await Task.Delay(3000);


        // Send ping request.
        byte[] payload = Encoding.UTF8.GetBytes("test");
        requestMessageAppServiceCallee = mbCalleeAppService.CreatePingRequest(payload);
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);



        // Receive message #3.
        nodeRequestAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        receivedVersion = nodeRequestAppServiceCallee.Request.SingleRequest.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        typeOk = (nodeRequestAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (nodeRequestAppServiceCallee.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (nodeRequestAppServiceCallee.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        receivedMessage = Encoding.UTF8.GetString(nodeRequestAppServiceCallee.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        messageOk = receivedMessage == callerMessage3;

        bool receiveMessageOk3 = versionOk && typeOk && messageOk;


        // Read ping response.
        responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;
        receivedVersion = responseMessageAppServiceCallee.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;


        typeOk = (responseMessageAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          && (responseMessageAppServiceCallee.Response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
          && (responseMessageAppServiceCallee.Response.SingleResponse.ResponseTypeCase == SingleResponse.ResponseTypeOneofCase.Ping);

        byte[] receivedPayload = responseMessageAppServiceCallee.Response.SingleResponse.Ping.Payload.ToByteArray();
        bool payloadOk = StructuralComparisons.StructuralComparer.Compare(receivedPayload, payload) == 0;

        bool pingOk = idOk && statusOk && versionOk && typeOk && payloadOk;


        // ACK message #2.
        nodeResponseAppServiceCallee = mbCalleeAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCallee);
        await clientCalleeAppService.SendMessageAsync(nodeResponseAppServiceCallee);



        // Send our message #2.
        string calleeMessage2 = "Message #2 to CALLER.";
        messageBytes = Encoding.UTF8.GetBytes(calleeMessage2);
        requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, messageBytes);
        uint calleeMessage2Id = requestMessageAppServiceCallee.Id;
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);

        await Task.Delay(3000);


        // Step 8 Acceptance
        bool step8Ok = receiveMessageOk1 && receiveMessageOk2 && pingOk && receiveMessageOk3;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");





        // Step 9
        log.Trace("Step 9");
        // Receive ACK message #1.
        responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCaller.Id == callerMessage1Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;
        receivedVersion = responseMessageAppServiceCaller.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        bool receiveAck1Ok = idOk && statusOk && versionOk;



        // Receive ACK message #2.
        responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCaller.Id == callerMessage2Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;
        receivedVersion = responseMessageAppServiceCaller.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        bool receiveAck2Ok = idOk && statusOk && versionOk;



        // Receive message #1 from callee.
        Message nodeRequestAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        receivedVersion = nodeRequestAppServiceCaller.Request.SingleRequest.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        typeOk = (nodeRequestAppServiceCaller.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (nodeRequestAppServiceCaller.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (nodeRequestAppServiceCaller.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        receivedMessage = Encoding.UTF8.GetString(nodeRequestAppServiceCaller.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        messageOk = receivedMessage == calleeMessage1;

        receiveMessageOk1 = versionOk && typeOk && messageOk;




        // ACK message #1 from callee.
        Message nodeResponseAppServiceCaller = mbCallerAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCaller);
        await clientCallerAppService.SendMessageAsync(nodeResponseAppServiceCaller);




        // Receive ACK message #3.
        responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCaller.Id == callerMessage3Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;
        receivedVersion = responseMessageAppServiceCaller.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        bool receiveAck3Ok = idOk && statusOk && versionOk;



        // Receive message #2 from callee.
        nodeRequestAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        receivedVersion = nodeRequestAppServiceCaller.Request.SingleRequest.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        typeOk = (nodeRequestAppServiceCaller.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (nodeRequestAppServiceCaller.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (nodeRequestAppServiceCaller.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        receivedMessage = Encoding.UTF8.GetString(nodeRequestAppServiceCaller.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        messageOk = receivedMessage == calleeMessage2;

        receiveMessageOk2 = versionOk && typeOk && messageOk;



        // ACK message #2 from callee.
        nodeResponseAppServiceCaller = mbCallerAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCaller);
        await clientCallerAppService.SendMessageAsync(nodeResponseAppServiceCaller);


        // Step 9 Acceptance
        bool step9Ok = receiveAck1Ok && receiveAck2Ok && receiveMessageOk1 && receiveAck3Ok && receiveMessageOk2;

        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");



        // Step 10
        log.Trace("Step 10");

        // Receive ACK message #1.
        responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == calleeMessage1Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;
        receivedVersion = responseMessageAppServiceCallee.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        receiveAck1Ok = idOk && statusOk && versionOk;

        // Receive ACK message #2.
        responseMessageAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == calleeMessage2Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;
        receivedVersion = responseMessageAppServiceCallee.Response.SingleResponse.Version.ToByteArray();
        versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, new byte[] { 1, 0, 0 }) == 0;

        receiveAck2Ok = idOk && statusOk && versionOk;


        // Step 10 Acceptance
        bool step10Ok = receiveAck1Ok && receiveAck2Ok;

        log.Trace("Step 10: {0}", step10Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok && step10Ok;

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
