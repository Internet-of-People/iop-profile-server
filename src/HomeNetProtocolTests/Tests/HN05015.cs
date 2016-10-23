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
  /// HN05015 - Application Service Caller Uses Invalid Token
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn05015---application-service-caller-uses-invalid-token
  /// </summary>
  public class HN05015 : ProtocolTest
  {
    public const string TestName = "HN05015";
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
        bool initializeProfileOk = await clientCallee.InitializeProfileAsync("Test Identity", null, 0x12345678, null);


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
        await clientCalleeAppService.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClAppService], true);

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
        await clientCallerAppService.ConnectAsync(NodeIp, (int)rolePorts[ServerRoleType.ClAppService], true);
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
        string testStr = "test";
        byte[] invalidCallerToken = Encoding.UTF8.GetBytes(testStr);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(invalidCallerToken, messageBytes);
        uint callerMessage1Id = requestMessageAppServiceCaller.Id;
        await clientCallerAppService.SendMessageAsync(requestMessageAppServiceCaller);

        responseMessageAppServiceCaller = await clientCallerAppService.ReceiveMessageAsync();
        idOk = responseMessageAppServiceCaller.Id == callerMessage1Id;
        statusOk = responseMessageAppServiceCaller.Response.Status == Status.ErrorNotFound;

        bool sendMessageOk = idOk && statusOk;


        // The caller should be disconnected by now, send or receive should throw.
        string callerMessage2 = "Message #2 to callee.";
        messageBytes = Encoding.UTF8.GetBytes(callerMessage2);
        requestMessageAppServiceCaller = mbCallerAppService.CreateApplicationServiceSendMessageRequest(callerToken, messageBytes);

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


        // Step 6 Acceptance
        bool step6Ok = sendMessageOk && disconnectedOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        // Step 7
        log.Trace("Step 7");
        string calleeMessage1 = "Message #1 to CALLER.";
        messageBytes = Encoding.UTF8.GetBytes(calleeMessage1);
        requestMessageAppServiceCallee = mbCalleeAppService.CreateApplicationServiceSendMessageRequest(calleeToken, messageBytes);

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


        // Step 7 Acceptance
        bool step7Ok = sendMessageOk && disconnectedOk;

        log.Trace("Step 7: {0}", step7Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok && step7Ok;

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
