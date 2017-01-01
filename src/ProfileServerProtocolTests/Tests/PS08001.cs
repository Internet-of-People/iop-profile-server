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
  /// PS08001 - Neighborhood Related Calls - Unauthorized
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08001---neighborhood-related-calls---unauthorized
  /// </summary>
  public class PS08001 : ProtocolTest
  {
    public const string TestName = "PS08001";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("srNeighbor Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress ServerIp = (IPAddress)ArgumentValues["Server IP"];
      int NeighborPort = (int)ArgumentValues["srNeighbor Port"];
      log.Trace("(ServerIp:'{0}',NeighborPort:{1})", ServerIp, NeighborPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        await client.ConnectAsync(ServerIp, NeighborPort, true);

        Message requestMessage = mb.CreateStartNeighborhoodInitializationRequest(1, 1);
        await client.SendMessageAsync(requestMessage);
        Message responseMessage = await client.ReceiveMessageAsync();

        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorUnauthorized;
        bool startNeighborhoodInitializationOk = idOk && statusOk;


        requestMessage = mb.CreateFinishNeighborhoodInitializationRequest();
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorUnauthorized;
        bool finishNeighborhoodInitializationOk = idOk && statusOk;


        requestMessage = mb.CreateNeighborhoodSharedProfileUpdateRequest(null);
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorUnauthorized;
        bool neighborhoodSharedProfileUpdateOk = idOk && statusOk;


        requestMessage = mb.CreateStopNeighborhoodUpdatesRequest();
        await client.SendMessageAsync(requestMessage);
        responseMessage = await client.ReceiveMessageAsync();

        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorUnauthorized;
        bool stopNeighborhoodUpdatesOk = idOk && statusOk;

        // Step 1 Acceptance

        Passed = startNeighborhoodInitializationOk && finishNeighborhoodInitializationOk && neighborhoodSharedProfileUpdateOk && stopNeighborhoodUpdatesOk;
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
