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
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS05025 - Application Service Callee Uses Same Connection Twice 2
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS05.md#ps05025---application-service-callee-uses-same-connection-twice-2
  /// </summary>
  public class PS05025 : ProtocolTest
  {
    public const string TestName = "PS05025";
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

      ProtocolClient clientCallee = new ProtocolClient();
      ProtocolClient clientCallee1AppService = new ProtocolClient(0, SemVer.V100, clientCallee.GetIdentityKeys());
      ProtocolClient clientCallee2AppService = new ProtocolClient(0, SemVer.V100, clientCallee.GetIdentityKeys());

      ProtocolClient clientCaller1 = new ProtocolClient();
      ProtocolClient clientCaller1AppService = new ProtocolClient(0, SemVer.V100, clientCaller1.GetIdentityKeys());

      ProtocolClient clientCaller2 = new ProtocolClient();
      ProtocolClient clientCaller2AppService = new ProtocolClient(0, SemVer.V100, clientCaller2.GetIdentityKeys());
      try
      {
        PsMessageBuilder mbCallee = clientCallee.MessageBuilder;
        PsMessageBuilder mbCallee1AppService = clientCallee1AppService.MessageBuilder;
        PsMessageBuilder mbCallee2AppService = clientCallee2AppService.MessageBuilder;

        PsMessageBuilder mbCaller1 = clientCaller1.MessageBuilder;
        PsMessageBuilder mbCaller1AppService = clientCaller1AppService.MessageBuilder;

        PsMessageBuilder mbCaller2 = clientCaller2.MessageBuilder;
        PsMessageBuilder mbCaller2AppService = clientCaller2AppService.MessageBuilder;


        // Step 1
        log.Trace("Step 1");

        byte[] pubKeyCallee = clientCallee.GetIdentityKeys().PublicKey;
        byte[] identityIdCallee = clientCallee.GetIdentityId();

        byte[] pubKeyCaller1 = clientCaller1.GetIdentityKeys().PublicKey;
        byte[] identityIdCaller1 = clientCaller1.GetIdentityId();

        byte[] pubKeyCaller2 = clientCaller2.GetIdentityKeys().PublicKey;
        byte[] identityIdCaller2 = clientCaller2.GetIdentityId();

        // Get port list.

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
        await clientCaller1.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool verifyIdentityOk1 = await clientCaller1.VerifyIdentityAsync();

        PsProtocolMessage requestMessage = mbCaller1.CreateCallIdentityApplicationServiceRequest(identityIdCallee, serviceName);
        uint initMessageCaller1Id = requestMessage.Id;
        await clientCaller1.SendMessageAsync(requestMessage);


        await clientCaller2.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], true);
        bool verifyIdentityOk2 = await clientCaller2.VerifyIdentityAsync();

        requestMessage = mbCaller2.CreateCallIdentityApplicationServiceRequest(identityIdCallee, serviceName);
        uint initMessageCaller2Id = requestMessage.Id;
        await clientCaller2.SendMessageAsync(requestMessage);



        // Step 2 Acceptance
        bool step2Ok = verifyIdentityOk1 && verifyIdentityOk2;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");



        // Step 3
        log.Trace("Step 3");
        PsProtocolMessage serverRequestMessage = await clientCallee.ReceiveMessageAsync();

        // What we received now can actually be either notification about caller1 or caller2, we do not know. 
        // So we have to check, which one is it before we proceed.
        byte[] receivedPubKey = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CallerPublicKey.ToByteArray();
        bool isCaller1 = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller1) == 0;
        bool isCaller2 = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller2) == 0;
        bool pubKeyOk = isCaller1 || isCaller2;
        bool serviceNameOk = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.ServiceName == serviceName;

        bool incomingCallNotificationOk1 = pubKeyOk && serviceNameOk;

        byte[] calleeToken1 = null;
        byte[] calleeToken2 = null;
        if (isCaller1) calleeToken1 = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();
        else calleeToken2 = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();

        PsProtocolMessage serverResponseMessage = mbCallee.CreateIncomingCallNotificationResponse(serverRequestMessage);
        await clientCallee.SendMessageAsync(serverResponseMessage);



        serverRequestMessage = await clientCallee.ReceiveMessageAsync();

        // This time, it has to be the other caller
        receivedPubKey = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CallerPublicKey.ToByteArray();
        if (isCaller1) pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller2) == 0;
        else pubKeyOk = StructuralComparisons.StructuralComparer.Compare(receivedPubKey, pubKeyCaller1) == 0;
        serviceNameOk = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.ServiceName == serviceName;

        bool incomingCallNotificationOk2 = pubKeyOk && serviceNameOk;

        if (isCaller1) calleeToken2 = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();
        else calleeToken1 = serverRequestMessage.Request.ConversationRequest.IncomingCallNotification.CalleeToken.ToByteArray();

        serverResponseMessage = mbCallee.CreateIncomingCallNotificationResponse(serverRequestMessage);
        await clientCallee.SendMessageAsync(serverResponseMessage);



        // Connect to clAppService and send initialization message (FIRST connection).
        await clientCallee1AppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);

        PsProtocolMessage requestMessageAppServiceCallee = mbCallee1AppService.CreateApplicationServiceSendMessageRequest(calleeToken1, null);
        await clientCallee1AppService.SendMessageAsync(requestMessageAppServiceCallee);


        // Connect to clAppService and send initialization message (SECOND connection).
        await clientCallee2AppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);

        requestMessageAppServiceCallee = mbCallee2AppService.CreateApplicationServiceSendMessageRequest(calleeToken2, null);
        await clientCallee2AppService.SendMessageAsync(requestMessageAppServiceCallee);


        // Step 3 Acceptance
        bool step3Ok = incomingCallNotificationOk1 && incomingCallNotificationOk2;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        // Step 4
        log.Trace("Step 4");
        PsProtocolMessage responseMessage = await clientCaller1.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == initMessageCaller1Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        byte[] callerToken1 = responseMessage.Response.ConversationResponse.CallIdentityApplicationService.CallerToken.ToByteArray();

        bool callIdentityOk1 = idOk && statusOk;

        // Connect to clAppService and send initialization message.
        await clientCaller1AppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);
        PsProtocolMessage requestMessageAppServiceCaller = mbCaller1AppService.CreateApplicationServiceSendMessageRequest(callerToken1, null);
        await clientCaller1AppService.SendMessageAsync(requestMessageAppServiceCaller);

        PsProtocolMessage responseMessageAppServiceCaller = await clientCaller1AppService.ReceiveMessageAsync();

        idOk = responseMessageAppServiceCaller.Id == requestMessageAppServiceCaller.Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;

        bool initAppServiceMessageOk1 = idOk && statusOk;




        responseMessage = await clientCaller2.ReceiveMessageAsync();
        idOk = responseMessage.Id == initMessageCaller1Id;
        statusOk = responseMessage.Response.Status == Status.Ok;
        byte[] callerToken2 = responseMessage.Response.ConversationResponse.CallIdentityApplicationService.CallerToken.ToByteArray();

        bool callIdentityOk2 = idOk && statusOk;

        // Connect to clAppService and send initialization message.
        await clientCaller2AppService.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.ClAppService], true);
        requestMessageAppServiceCaller = mbCaller2AppService.CreateApplicationServiceSendMessageRequest(callerToken2, null);
        await clientCaller2AppService.SendMessageAsync(requestMessageAppServiceCaller);

        responseMessageAppServiceCaller = await clientCaller2AppService.ReceiveMessageAsync();

        idOk = responseMessageAppServiceCaller.Id == requestMessageAppServiceCaller.Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.Ok;

        bool initAppServiceMessageOk2 = idOk && statusOk;


        // Step 4 Acceptance
        bool step4Ok = callIdentityOk1 && initAppServiceMessageOk1 && callIdentityOk2 && initAppServiceMessageOk2;

        log.Trace("Step 4: {0}", step4Ok ? "PASSED" : "FAILED");



        // Step 5
        log.Trace("Step 5");
        PsProtocolMessage responseMessageAppServiceCallee = await clientCallee1AppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;

        bool typeOk = (responseMessageAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          && (responseMessageAppServiceCallee.Response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
          && (responseMessageAppServiceCallee.Response.SingleResponse.ResponseTypeCase == SingleResponse.ResponseTypeOneofCase.ApplicationServiceSendMessage);

        bool appServiceSendOk1 = idOk && statusOk && typeOk;


        responseMessageAppServiceCallee = await clientCallee2AppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseMessageAppServiceCallee.Response.Status == Status.Ok;

        typeOk = (responseMessageAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Response)
          && (responseMessageAppServiceCallee.Response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
          && (responseMessageAppServiceCallee.Response.SingleResponse.ResponseTypeCase == SingleResponse.ResponseTypeOneofCase.ApplicationServiceSendMessage);

        bool appServiceSendOk2 = idOk && statusOk && typeOk;

        // Step 5 Acceptance
        bool step5Ok = appServiceSendOk1 && appServiceSendOk2;

        log.Trace("Step 5: {0}", step5Ok ? "PASSED" : "FAILED");



        // Step 6
        log.Trace("Step 6");
        string caller1Message1 = "Message #1 to callee from caller1.";
        byte[] messageBytes = Encoding.UTF8.GetBytes(caller1Message1);
        requestMessageAppServiceCaller = mbCaller1AppService.CreateApplicationServiceSendMessageRequest(callerToken1, messageBytes);
        uint callerMessage1Id = requestMessageAppServiceCaller.Id;
        await clientCaller1AppService.SendMessageAsync(requestMessageAppServiceCaller);


        // Step 6 Acceptance
        bool step6Ok = true;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");
        // Receive message #1.
        PsProtocolMessage serverRequestAppServiceCallee = await clientCallee1AppService.ReceiveMessageAsync();
        SemVer receivedVersion = new SemVer(serverRequestAppServiceCallee.Request.SingleRequest.Version);
        bool versionOk = receivedVersion.Equals(SemVer.V100);

        typeOk = (serverRequestAppServiceCallee.MessageTypeCase == Message.MessageTypeOneofCase.Request)
          && (serverRequestAppServiceCallee.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
          && (serverRequestAppServiceCallee.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification);

        string receivedMessage = Encoding.UTF8.GetString(serverRequestAppServiceCallee.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray());
        bool messageOk = receivedMessage == caller1Message1;

        bool receiveMessageOk = versionOk && typeOk && messageOk;


        // ACK message #1.
        PsProtocolMessage serverResponseAppServiceCallee = mbCallee1AppService.CreateApplicationServiceReceiveMessageNotificationResponse(serverRequestAppServiceCallee);
        await clientCallee1AppService.SendMessageAsync(serverResponseAppServiceCallee);




        // Send invalid message - over the FIRST connection with token from the SECOND connection.
        await Task.Delay(3000);
        string calleeMessage = "Invalid Message";
        messageBytes = Encoding.UTF8.GetBytes(calleeMessage);
        requestMessageAppServiceCallee = mbCallee1AppService.CreateApplicationServiceSendMessageRequest(calleeToken2, messageBytes);
        await clientCallee1AppService.SendMessageAsync(requestMessageAppServiceCallee);

        PsProtocolMessage responseAppServiceCallee = await clientCallee1AppService.ReceiveMessageAsync();
        idOk = responseAppServiceCallee.Id == requestMessageAppServiceCallee.Id;
        statusOk = responseAppServiceCallee.Response.Status == Status.ErrorNotFound;

        bool sendMessageOk = idOk && statusOk;


        // Step 7 Acceptance
        bool step7Ok = sendMessageOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        // Step 8 
        log.Trace("Step 8");
        await Task.Delay(3000);
        string caller2Message1 = "Message #1 to callee from caller2.";
        messageBytes = Encoding.UTF8.GetBytes(caller2Message1);
        requestMessageAppServiceCaller = mbCaller2AppService.CreateApplicationServiceSendMessageRequest(callerToken2, messageBytes);

        // Either the third client is disconnected and this should prevent sending a message or receiving a response,
        // OR just the relay was destroyed and the client will receive error not found.
        bool disconnectOk = false;
        messageOk = false;
        try
        {
          await clientCaller2AppService.SendMessageAsync(requestMessageAppServiceCaller);
          responseMessage = await clientCaller2AppService.ReceiveMessageAsync();
          idOk = responseMessage.Id == requestMessageAppServiceCaller.Id;
          statusOk = responseMessage.Response.Status == Status.ErrorNotFound;
          messageOk = idOk && statusOk;
        }
        catch
        {
          log.Trace("Expected exception occurred.");
          disconnectOk = true;
        }

        // Step 8 Acceptance
        bool step8Ok = disconnectOk || messageOk;

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
      clientCallee1AppService.Dispose();
      clientCallee2AppService.Dispose();
      clientCaller1.Dispose();
      clientCaller1AppService.Dispose();
      clientCaller2.Dispose();
      clientCaller2AppService.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
