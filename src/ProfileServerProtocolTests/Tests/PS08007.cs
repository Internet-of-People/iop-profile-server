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
  /// PS08007 - Neighborhood Initialization Process - Already Exists
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS08.md#ps08007---neighborhood-initialization-process---already-exists
  /// </summary>
  public class PS08007 : ProtocolTest
  {
    public const string TestName = "PS08007";
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTests.Tests." + TestName);

    public override string Name { get { return TestName; } }

    /// <summary>List of test's arguments according to the specification.</summary>
    private List<ProtocolTestArgument> argumentDescriptions = new List<ProtocolTestArgument>()
    {
      new ProtocolTestArgument("Server IP", ProtocolTestArgumentType.IpAddress),
      new ProtocolTestArgument("primary Port", ProtocolTestArgumentType.Port),
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


        bool profileInitializationOk = true;
        byte[] testImageData = File.ReadAllBytes(Path.Combine("images", TestName + ".jpg"));

        int profileIndex = 1;
        int profileCount = 10;
        for (int i = 0; i < profileCount; i++)
        {
          ProtocolClient profileClient = new ProtocolClient();
          profileClient.InitializeRandomProfile(profileIndex, testImageData);
          profileIndex++;

          if (await profileClient.RegisterAndInitializeProfileAsync(ServerIp, (int)rolePorts[ServerRoleType.ClNonCustomer], (int)rolePorts[ServerRoleType.ClCustomer]))
          {
            TestProfiles.Add(profileClient.Profile.Name, profileClient);
          }
          else
          {
            profileClient.Dispose();
            profileInitializationOk = false;
            break;
          }
        }


        bool step1Ok = listPortsOk && profileInitializationOk;
        log.Trace("Step 1: {0}", step1Ok ? "PASSED" : "FAILED");



        // Step 2
        log.Trace("Step 2");
        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool neighborhoodInitializationProcessOk = await client.NeighborhoodInitializationProcessAsync(1, 1, ServerIp, TestProfiles);

        client.CloseConnection();

        bool step2Ok = neighborhoodInitializationProcessOk;

        log.Trace("Step 2: {0}", step2Ok ? "PASSED" : "FAILED");


        // Step 3
        log.Trace("Step 3");

        await client.ConnectAsync(ServerIp, (int)rolePorts[ServerRoleType.SrNeighbor], true);
        bool verifyIdentityOk = await client.VerifyIdentityAsync();

        // Start neighborhood initialization process.
        Message requestMessage = client.MessageBuilder.CreateStartNeighborhoodInitializationRequest(2, 2, ServerIp);
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.ErrorAlreadyExists;
        bool startNeighborhoodInitializationOk = idOk && statusOk;

        bool step3Ok = profileInitializationOk && startNeighborhoodInitializationOk;

        log.Trace("Step 3: {0}", step3Ok ? "PASSED" : "FAILED");



        Passed = step1Ok && step2Ok && step3Ok;

        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      client.Dispose();

      foreach (ProtocolClient protocolClient in TestProfiles.Values)
        protocolClient.Dispose();

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
