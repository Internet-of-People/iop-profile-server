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
using System.Threading;
using System.Threading.Tasks;

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// HN05024 - Application Service Callee Uses Same Connection Twice
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/HN05.md#hn05024---application-service-callee-uses-same-connection-twice
  /// </summary>
  public class HN05024 : ProtocolTest
  {
    public const string TestName = "HN05024";
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
      ProtocolClient clientCalleeAppService = new ProtocolClient(0, SemVer.V100, clientCallee.GetIdentityKeys());

      ProtocolClient clientCaller1 = new ProtocolClient();
      ProtocolClient clientCaller1AppService = new ProtocolClient(0, SemVer.V100, clientCaller1.GetIdentityKeys());

      ProtocolClient clientCaller2 = new ProtocolClient();
      ProtocolClient clientCaller2AppService = new ProtocolClient(0, SemVer.V100, clientCaller2.GetIdentityKeys());
      try
      {
        MessageBuilder mbCallee = clientCallee.MessageBuilder;
        MessageBuilder mbCalleeAppService = clientCalleeAppService.MessageBuilder;

        MessageBuilder mbCaller1 = clientCaller1.MessageBuilder;
        MessageBuilder mbCaller1AppService = clientCaller1AppService.MessageBuilder;

        MessageBuilder mbCaller2 = clientCaller2.MessageBuilder;
        MessageBuilder mbCaller2AppService = clientCaller2AppService.MessageBuilder;


        // Step 1
        log.Trace("Step 1");

        byte[] pubKeyCallee = clientCallee.GetIdentityKeys().PublicKey;
        byte[] identityIdCallee = clientCallee.GetIdentityId();

        byte[] pubKeyCaller1 = clientCaller1.GetIdentityKeys().PublicKey;
        byte[] identityIdCaller1 = clientCaller1.GetIdentityId();

        byte[] pubKeyCaller2 = clientCaller2.GetIdentityKeys().PublicKey;
        byte[] identityIdCaller2 = clientCaller2.GetIdentityId();

        // Get port list.

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
        await clientCaller1.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool verifyIdentityOk1 = await clientCaller1.VerifyIdentityAsync();

        Message requestMessage = mbCaller1.CreateCallIdentityApplicationServiceRequest(identityIdCallee, serviceName);
        uint initMessageCaller1Id = requestMessage.Id;
        await clientCaller1.SendMessageAsync(requestMessage);


        await clientCaller2.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool verifyIdentityOk2 = await clientCaller2.VerifyIdentityAsync();

        requestMessage = mbCaller2.CreateCallIdentityApplicationServiceRequest(identityIdCallee, serviceName);
        uint initMessageCaller2Id = requestMessage.Id;
        await clientCaller2.SendMessageAsync(requestMessage);



        // Step 2 Acceptance
        bool step2Ok = verifyIdentityOk1 && verifyIdentityOk2;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");
        Message nodeRequestMessage = await clientCallee.ReceiveMessageAsync();

        byte[] receivedPubKey = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CallerPublicKey.ToByteArray();
        bool pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller1) == 0;
        bool serviceNameOk = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.ServiceName == serviceName;

        bool incomingCallNotificationOk1 = pubKeyOk && serviceNameOk;

        byte[] calleeToken1 = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();

        Message nodeResponseMessage = mbCallee.CreateIncomingCallNotificationResponse(nodeRequestMessage);
        await clientCallee.SendMessageAsync(nodeResponseMessage);




        nodeRequestMessage = await clientCallee.ReceiveMessageAsync();

        receivedPubKey = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CallerPublicKey.ToByteArray();
        pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller2) == 0;
        serviceNameOk = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.ServiceName == serviceName;

        bool incomingCallNotificationOk2 = pubKeyOk && serviceNameOk;

        byte[] calleeToken2 = nodeRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();

        nodeResponseMessage = mbCallee.CreateIncomingCallNotificationResponse(nodeRequestMessage);
        await clientCallee.SendMessageAsync(nodeResponseMessage);





        // Connect to clAppService and send initialization message.
        await clientCalleeAppService.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClAppService], true);

        Message requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken1, null);
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);


        // Step 3 Acceptance
        bool step3Ok = incomingCallNotificationOk1 && incomingCallNotificationOk2;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");


        // Step 4
        log.Trace("Step 4");
        Message responseMessage = await clientCaller1.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == initMessageCaller1Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        byte[] callerToken = responseMessage.Response.ConversationResponse.CallIdentityApplicationService.CallerToken.ToByteArray();

        bool callIdentityOk = idOk && statusOk;

        // Connect to clAppService and send initialization message.
        await clientCaller1AppService.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClAppService], true);
        Message requestMessageAppServiceCaller = mbCaller1AppService.CreateApplicationServiceSendMessageRequest(callerToken, null);
        await clientCaller1AppService.SendMessageAsync(requestMessageAppServiceCaller);

        Message responseMessageAppServiceCaller = await clientCaller1AppService.ReceiveMessageAsync();

        idOk = responseMessageAppServiceCaller.Id == requestMessageAppServiceCaller.Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;

        bool initAppServiceMessageOk = idOk && statusOk;


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
        string caller1Message1 = "Message #1 to callee.";
        byte[] messageBytes = Encoding.UTF8.GetBytes(caller1Message1);
        requestMessageAppServiceCaller = mbCaller1AppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);
        uint callerMessage1Id = requestMessageAppServiceCaller.Id;
        await clientCaller1AppService.SendMessageAsync(requestMessageAppServiceCaller);


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
        bool messageOk = receivedMessage == caller1Message1;

        bool receiveMessageOk = versionOk && typeOk && messageOk;


        // ACK message #1.
        Message nodeResponseAppServiceCallee = mbCalleeAppService.CreateApplicationServiceReceiveMessageNotificationResponse(nodeRequestAppServiceCallee);
        await clientCalleeAppService.SendMessageAsync(nodeResponseAppServiceCallee);




        // Send second intialization message
        await Task.Delay(3000);
        requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken2, null);
        await clientCalleeAppService.SendMessageAsync(requestMessageAppServiceCallee);

        Message responseAppServiceCallee = await clientCalleeAppService.ReceiveMessageAsync();
        idOk = responseAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseAppServiceCallee.Response.Status == Status.ErrorNotFound;

        bool secondInitOk = idOk && statusOk;


        // Step 7 Acceptance
        bool step7Ok = secondInitOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        // Step 8 
        log.Trace("Step 8");

        // Receive init message response.
        bool initMessageOk = false;
        bool disconnectOk = false;
        try
        {
          Message initMessageResponseAppServiceCaller2 = await clientCaller2AppService.ReceiveMessageAsync();
          idOk = initMessageResponseAppServiceCaller2.Id == initMessageCaller2Id;
          statusOk = initMessageResponseAppServiceCaller2.Response.Status == Status.ErrorNotFound;

          initMessageOk = idOk && statusOk;
        }
        catch
        {
          log.Trace("Expected exception occurred.");
          disconnectOk = true;
        }

        // Step 8 Acceptance
        bool step8Ok = initMessageOk || disconnectOk;

        log.Trace("Step 8: {0}", step8Ok ? "PASSED" : "FAILED");


        // Step 9
        log.Trace("Step 9");

        // Receive ACK message #1.
        responseMessageAppServiceCaller = await clientCaller1AppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCaller.Id == callerMessage1Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;
        receivedVersion = new SemVer(responseMessageAppServiceCaller.Response.SingleResponse.Version);
        versionOk = receivedVersion.Equals(SemVer.V100);

        bool receiveAckOk = idOk && statusOk && versionOk;

        // Step 9 Acceptance
        bool step9Ok = receiveAckOk;

        log.Trace("Step 9: {0}", step9Ok ? "PASSED" : "FAILED");

        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok && step8Ok && step9Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      clientCallee.Dispose();
      clientCalleeAppService.Dispose();
      clientCaller1.Dispose();
      clientCaller1AppService.Dispose();
      clientCaller2.Dispose();
      clientCaller2AppService.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
