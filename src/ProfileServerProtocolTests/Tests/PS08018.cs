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
using System.Threading.Tasks;
using Iop.Locnet;
using IopCommon;

namespace ProfileServerProtocolTests.Tests
{
  /// <summary>
  /// PS08018 - New Neighbor - Empty Database
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08018---new-neighbor---empty-database
  /// </summary>
  public class PS08018 : ProtocolTest
  {
    public const string TestName = "PS08018";
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("Base Port", ProtocolTestArgumentType.Port),
      new ProtocolTestArgument("LOC Port", ProtocolTestArgumentType.Port),
    };

    public override List<ProtocolTestArgument> ArgumentDescriptions { get { return argumentDescriptions; } }


    /// <summary>Generated test profiles mapped by their name.</summary>
    public static Dictionary<string, ProtocolClient> TestProfiles = new Dictionary<string, ProtocolClient>(StringComparer.Ordinal);

    /// <summary>Random number generator.</summary>
    public static Random Rng = new Random();


    /// <summary>
    /// Implementation of the test itself.
    /// </summary>
    /// <returns>true if the test passes, false otherwise.</returns>
    public override async Task<bool> RunAsync()
    {
      IPAddress ServerIp = (IPAddress)ArgumentValues["Server IP"];
      int PrimaryPort = (int)ArgumentValues["primary Port"];
      int BasePort = (int)ArgumentValues["Base Port"];
      int LocPort = (int)ArgumentValues["LOC Port"];
      log.Trace("(ServerIp:'{0}',PrimaryPort:{1},BasePort:{2},LocPort:{3})", ServerIp, PrimaryPort, BasePort, LocPort);

      bool res = false;
      Passed = false;

      ProtocolClient client = new ProtocolClient();
      ProfileServer profileServer = null;
      LocServer locServer = null;
      try
      {
        PsMessageBuilder mb = client.MessageBuilder;

        // Step 1
        log.Trace("Step 1");

        profileServer = new ProfileServer("TestProfileServer", ServerIp, BasePort, client.GetIdentityKeys(), new IopProtocol.GpsLocation(1, 2));
        bool profileServerStartOk = profileServer.Start();


        locServer = new LocServer("TestLocServer", ServerIp, LocPort);
        bool locServerStartOk = locServer.Start();

        await locServer.WaitForProfileServerConnectionAsync();

        bool step1Ok = profileServerStartOk && locServerStartOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");


        // Step 2
        log.Trace("Step 2");

        NeighbourhoodChange change = new NeighbourhoodChange()
        {
          AddedNodeInfo = profileServer.GetNodeInfo(LocPort)
        };
        
        bool changeNotificationOk = await locServer.SendChangeNotification(change);

        IncomingServerMessage incomingServerMessage = await profileServer.WaitForConversationRequest(ServerRole.ServerNeighbor, ConversationRequest.RequestTypeOneofCase.StartNeighborhoodInitialization);

        PsProtocolMessage finishRequest = await profileServer.SendFinishNeighborhoodInitializationRequest(incomingServerMessage.Client);

        incomingServerMessage = await profileServer.WaitForResponse(ServerRole.ServerNeighbor, finishRequest);
        bool statusOk = incomingServerMessage.IncomingMessage.Response.Status == Iop.Profileserver.Status.Ok;

        bool step2Ok = changeNotificationOk && (finishRequest != null) && statusOk;
        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");


        Passed = step1Ok && step2Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();

      foreach (ProtocolClient protocolClient in TestProfiles.Values)
        protocolClient.Dispose();

      if (profileServer != null) profileServer.Shutdown();
      if (locServer != null) locServer.Shutdown();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
