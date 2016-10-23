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
  /// HN05003 - Application Service Call - Extensive Test 2
  /// https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md#hn05003---application-service-call---extensive-test-2
  /// </summary>
  public class HN05003 : ProtocolTest
  {
    public const string TestName = "HN05003";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Node IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Number of data messages each client sends to the other party.</summary>
    public const int DataMessageCountPerClient = 100;

    /// <summary>Minimal number of bytes per data message.</summary>
    public const int DataMessageSizeMin = 4;

    /// <summary>Maximal number of bytes per data message.</summary>
    public const int DataMessageSizeMax = 10000;

    /// <summary>Maximal delay between sending two messages in milliseconds.</summary>
    public const int DataMessageDelayMaxMs = 200;


    /// <summary>Number of client pairs to execute.</summary>
    public const int ClientPairCount = 3;

    /// <summary>Test results for all client pairs.</summary>
    public static bool[] PassedArray = new bool[ClientPairCount];



    /// <summary>
    /// Runs two instances of RunAsyncInternal in parallel.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      log.Trace("()");

      Task<bool>[] tasks = new Task<bool>[ClientPairCount];
      for (int i = 0; i < ClientPairCount; i++)
        tasks[i] = RunAsyncInternal(i);

      bool res = true;
      for (int i = 0; i < ClientPairCount; i++)
        if (!await tasks[i])
          res = false;

      Passed = true;
      for (int i = 0; i < ClientPairCount; i++)
      {
        if (!PassedArray[i])
        {
          Passed = false;
          break;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public async Task<bool> RunAsyncInternal(int Index)
    {
      IPAddress NodeIp = (IPAddress)ArgumentValues["Node IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      log.Trace("(NodeIp:'{0}',PrimaryPort:{1},Index:{2})", NodeIp, PrimaryPort, Index);

      bool res = false;
      PassedArray[Index] = false;

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
        UnfinishedRequestCounter callerCounter = new UnfinishedRequestCounter() { Name = string.Format("Caller-{0}", Index) };
        UnfinishedRequestCounter calleeCounter = new UnfinishedRequestCounter() { Name = string.Format("Callee-{0}", Index) };
        SemaphoreSlim callerWriteLock = new SemaphoreSlim(1);
        SemaphoreSlim calleeWriteLock = new SemaphoreSlim(1);

        Task<byte[]> messageReceivingTaskCaller = MessageReceivingLoop(clientCallerAppService, mbCallerAppService, "CallerReceiving", callerWriteLock, callerCounter);
        Task<byte[]> messageReceivingTaskCallee = MessageReceivingLoop(clientCalleeAppService, mbCalleeAppService, "CalleeReceiving", calleeWriteLock, calleeCounter);

        Task<byte[]> messageSendingTaskCaller = MessageSendingLoop(clientCallerAppService, mbCallerAppService, "CallerSending", callerToken, callerWriteLock, callerCounter);
        Task<byte[]> messageSendingTaskCallee = MessageSendingLoop(clientCalleeAppService, mbCalleeAppService, "CalleeSending", calleeToken, calleeWriteLock, calleeCounter);

        byte[] callerSendMessageHash = messageSendingTaskCaller.Result;
        byte[] calleeSendMessageHash = messageSendingTaskCallee.Result;

        byte[] callerReceiveMessageHash = messageReceivingTaskCaller.Result;
        byte[] calleeReceiveMessageHash = messageReceivingTaskCallee.Result;


        bool callerMessageHashOk = StructuralComparisons.StructuralComparer.Compare(callerSendMessageHash, calleeReceiveMessageHash) == 0;
        bool calleeMessageHashOk = StructuralComparisons.StructuralComparer.Compare(calleeSendMessageHash, callerReceiveMessageHash) == 0;


        // Step 6 Acceptance
        bool step6Ok = callerMessageHashOk && calleeMessageHashOk;

        log.Trace("Step 6: {0}", step6Ok ? "PASSED" : "FAILED");


        PassedArray[Index] = step1Ok && step2Ok && step3Ok && step4Ok && step5Ok && step6Ok;
        log.Trace("PassedArray[{0}] = {1}", Index, PassedArray[Index]);

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


    /// <summary>Random number generator.</summary>
    public static Random rng = new Random();

    /// <summary>
    /// Sends data messages to the other client.
    /// </summary>
    /// <param name="Client">Client connected to clAppService port with an initialized relay.</param>
    /// <param name="Builder">Client's message builder.</param>
    /// <param name="Name">Log prefix.</param>
    /// <param name="Token">Client's open relay token.</param>
    /// <param name="WriteLock">Lock object to protect write access to client's stream.</param>
    /// <param name="UnfinishedRequestCounter">Unfinished request counter object.</param>
    /// <returns></returns>
    public static async Task<byte[]> MessageSendingLoop(ProtocolClient Client, MessageBuilder Builder, string Name, byte[] Token, SemaphoreSlim WriteLock, UnfinishedRequestCounter UnfinishedRequestCounter)
    {
      string prefix = string.Format("[{0}] ", Name);
      log.Trace(prefix + "()");

      List<byte[]> chunks = new List<byte[]>();
      int totalSize = 0;

      for (int i = 0; i < DataMessageCountPerClient; i++)
      {
        // Generate message data.
        int dataLength = rng.Next(DataMessageSizeMin, DataMessageSizeMax);
        byte[] msg = new byte[dataLength];
        Crypto.Rng.GetBytes(msg);
        totalSize += dataLength;
        chunks.Add(msg);


        // Send message.
        await UnfinishedRequestCounter.WaitAndIncrement();
        await WriteLock.WaitAsync();

        Message appServiceMessageRequest = Builder.CreateApplicationServiceSendMessageRequest(Token, msg);
        await Client.SendMessageAsync(appServiceMessageRequest);

        WriteLock.Release();

        int delay = rng.Next(DataMessageDelayMaxMs);
        if (delay != 0) await Task.Delay(delay);
      }


      // Count final hash.
      int offset = 0;
      byte[] data = new byte[totalSize];
      foreach (byte[] chunk in chunks)
      {
        Array.Copy(chunk, 0, data, offset, chunk.Length);
        offset += chunk.Length;
      }
        
      byte[] finalHash = Crypto.Sha1(data);

      log.Trace(prefix + "(-):{0}", Crypto.ToHex(finalHash));
      return finalHash;
    }



    /// <summary>
    /// Receives messages from the open relay and sends confirmations for the incoming messages back to the node. 
    /// </summary>
    /// <param name="Client">Client connected to clAppService port with an initialized relay.</param>
    /// <param name="Builder">Client's message builder.</param>
    /// <param name="Name">Log prefix.</param>
    /// <param name="WriteLock">Lock object to protect write access to client's stream.</param>
    /// <param name="UnfinishedRequestCounter">Unfinished request counter object.</param>
    /// <returns></returns>
    public static async Task<byte[]> MessageReceivingLoop(ProtocolClient Client, MessageBuilder Builder, string Name, SemaphoreSlim WriteLock, UnfinishedRequestCounter UnfinishedRequestCounter)
    {
      string prefix = string.Format("[{0}] ", Name);
      log.Trace(prefix + "()");

      List<byte[]> chunksReceived = new List<byte[]>();
      int totalSize = 0;
      int ackReceived = 0;

      while ((chunksReceived.Count < DataMessageCountPerClient) || (ackReceived < DataMessageCountPerClient))
      {
        Message incomingMessage = await Client.ReceiveMessageAsync();

        if (incomingMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request)
        {
          // We have received message from the other client.
          if ((incomingMessage.Request.ConversationTypeCase == Request.ConversationTypeOneofCase.SingleRequest)
            && (incomingMessage.Request.SingleRequest.RequestTypeCase == SingleRequest.RequestTypeOneofCase.ApplicationServiceReceiveMessageNotification))
          {
            byte[] receivedData = incomingMessage.Request.SingleRequest.ApplicationServiceReceiveMessageNotification.Message.ToByteArray();
            chunksReceived.Add(receivedData);
            log.Trace(prefix + "Received data message #{0} - {1} bytes.", chunksReceived.Count, receivedData.Length);
            totalSize += receivedData.Length;

            // Sending ACK back to the node.
            await WriteLock.WaitAsync();

            Message ackResponse = Builder.CreateApplicationServiceReceiveMessageNotificationResponse(incomingMessage);
            await Client.SendMessageAsync(ackResponse);

            WriteLock.Release();
          }
          else
          {
            log.Trace(prefix + "Received invalid message, terminating loop.");
            break;
          }
        }
        else
        {
          // We have received confirmation of our message being delivered.
          if ((incomingMessage.Response.ConversationTypeCase == Response.ConversationTypeOneofCase.SingleResponse)
            && (incomingMessage.Response.SingleResponse.ResponseTypeCase == SingleResponse.ResponseTypeOneofCase.ApplicationServiceSendMessage))
          {
            ackReceived++;
            UnfinishedRequestCounter.Decrement();
            log.Trace(prefix + "Received ACK to message ID {0}, ack count {1}.", incomingMessage.Id, ackReceived);
          }
          else
          {
            log.Trace(prefix + "Received invalid message, terminating loop.");
            break;
          }
        }
      }


      // Count final hash.
      int offset = 0;
      byte[] data = new byte[totalSize];
      foreach (byte[] chunk in chunksReceived)
      {
        Array.Copy(chunk, 0, data, offset, chunk.Length);
        offset += chunk.Length;
      }

      byte[] finalHash = Crypto.Sha1(data);

      log.Trace(prefix + "(-):{0}", Crypto.ToHex(finalHash));
      return finalHash;
    }


    /// <summary>
    /// Holds number of unfinished requests sent by a client and protects the sender from having too many pending requests.
    /// </summary>
    public class UnfinishedRequestCounter
    {
      /// <summary>Name for the counter for logging purposes.</summary>
      public string Name;

      /// <summary>Number of unfinished requests.</summary>
      public int Counter = 0;

      /// <summary>
      /// Lock for access protection to Counter field.
      /// </summary>
      public object Lock = new object();

      /// <summary>
      /// Increments the counter if it is below limit, or waits until it can be incremented and then increments it.
      /// </summary>
      public async Task WaitAndIncrement()
      {
        log.Trace("[{0}]()", Name);
        bool done = false;

        int newCnt = 0;
        while (!done)
        {
          lock (Lock)
          {
            if (Counter < 20)
            {
              Counter++;
              newCnt = Counter;
              done = true;
            }
          }

          if (!done)
            await Task.Delay(500);
        }

        log.Trace("[{0}](-):{1}", Name, newCnt);
      }

      /// <summary>
      /// Decrements the counter. 
      /// </summary>
      public void Decrement()
      {
        log.Trace("[{0}]()", Name);
        int newCnt = 0;
        lock (Lock)
        {
          newCnt = Counter--;
        }
        log.Trace("[{0}](-):{1}", Name, newCnt);
      }
    }
  }
}
