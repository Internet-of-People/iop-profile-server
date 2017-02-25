
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
  /// PS08009 - Neighborhood Initialization Process - Invalid Ports
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08009---neighborhood-initialization-process---invalid-ports
  /// </summary>
  public class PS08009 : ProtocolTest
  {
    public const string TestName = "PS08009";
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

      ProtocolClient client = new ProtocolClient();
      try
      {
        MessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");
        // Get port list.
        await client.ConnectAsync(ServerIp, PrimaryPort, false);
        Dictionary<ServerRoleType, uint> rolePorts = new Dictionary<ServerRoleType, uint>();
        bool listPortsOk = await client.ListServerPorts(rolePorts);
        client.CloseConnection();


        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);

        bool verifyIdentityOk = await client.VerifyIdentityAsync();

        Message requestMessage = mb.CreateStartNeighborhoodInitializationRequest(0, 1);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        bool detailsOk = responseMessage.Response.Details == "primaryPort";
        bool startNeighborhoodInitializationOk1 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateStartNeighborhoodInitializationRequest(1, 0);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "srNeighborPort";
        bool startNeighborhoodInitializationOk2 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateStartNeighborhoodInitializationRequest(100000, 1);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "primaryPort";
        bool startNeighborhoodInitializationOk3 = idOk && statusOk && detailsOk;


        requestMessage = mb.CreateStartNeighborhoodInitializationRequest(1, 100000);
        await client.SendMessageAsync(requestMessage);

        responseMessage = await client.ReceiveMessageAsync();
        idOk = responseMessage.Id == requestMessage.Id;
        statusOk = responseMessage.Response.Status == Status.ErrorInvalidValue;
        detailsOk = responseMessage.Response.Details == "srNeighborPort";
        bool startNeighborhoodInitializationOk4 = idOk && statusOk && detailsOk;


        bool step1Ok = listPortsOk && startNeighborhoodInitializationOk1 && startNeighborhoodInitializationOk2 && startNeighborhoodInitializationOk3 && startNeighborhoodInitializationOk4;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");

        Passed = step1Ok;

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
