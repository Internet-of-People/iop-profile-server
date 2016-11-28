using HomeNetProtocol;
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

namespace HomeNetProtocolTests.Tests
{
  /// <summary>
  /// PS01004 - List Roles
  /// https://github.com/Internet-of-People/message-protocol/blob/master/tests/PS01.md#ps01004---list-roles
  /// </summary>
  public class HN01004 : ProtocolTest
  {
    public const string TestName = "PS01004";
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test." + TestName);

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
        await client.ConnectAsync(ServerIp, PrimaryPort, false);

        Message requestMessage = mb.CreateListRolesRequest();
        await client.SendMessageAsync(requestMessage);

        Message responseMessage = await client.ReceiveMessageAsync();

        // Step 1 Acceptance
        bool idOk = responseMessage.Id == requestMessage.Id;
        bool statusOk = responseMessage.Response.Status == Status.Ok;
        bool primaryPortOk = false;
        bool srNeighborPortOk = false;
        bool ndColleaguePortOk = false;
        bool clNonCustomerPortOk = false;
        bool clCustomerPortOk = false;
        bool clAppServicePortOk = false;

        bool error = false;

        HashSet<uint> clientOnlyPorts = new HashSet<uint>();
        HashSet<uint> serverMixedPorts = new HashSet<uint>();

        foreach (ServerRole serverRole in responseMessage.Response.SingleResponse.ListRoles.Roles)
        {
          switch (serverRole.Role)
          {
            case ServerRoleType.Primary:
              serverMixedPorts.Add(serverRole.Port);
              primaryPortOk = serverRole.IsTcp && !serverRole.IsTls && !clientOnlyPorts.Contains(serverRole.Port);
              log.Trace("Primary port is {0}OK: TCP is {1}, TLS is {2}, Port no. is {3}, client only port list: {4}", primaryPortOk ? "" : "NOT ", serverRole.IsTcp, serverRole.IsTls, serverRole.Port, string.Join(",", clientOnlyPorts));
              break;

            case ServerRoleType.SrNeighbor:
              serverMixedPorts.Add(serverRole.Port);
              srNeighborPortOk = serverRole.IsTcp && serverRole.IsTls && !clientOnlyPorts.Contains(serverRole.Port);
              log.Trace("Server Neighbor port is {0}OK: TCP is {1}, TLS is {2}, Port no. is {3}, client only port list: {4}", srNeighborPortOk ? "" : "NOT ", serverRole.IsTcp, serverRole.IsTls, serverRole.Port, string.Join(",", clientOnlyPorts));
              break;

            
            case ServerRoleType.ClNonCustomer:
              clientOnlyPorts.Add(serverRole.Port);
              clNonCustomerPortOk = serverRole.IsTcp && serverRole.IsTls && !serverMixedPorts.Contains(serverRole.Port);
              log.Trace("Client Non-customer port is {0}OK: TCP is {1}, TLS is {2}, Port no. is {3}, server/mixed port list: {4}", clNonCustomerPortOk ? "" : "NOT ", serverRole.IsTcp, serverRole.IsTls, serverRole.Port, string.Join(",", serverMixedPorts));
              break;

            case ServerRoleType.ClCustomer:
              clientOnlyPorts.Add(serverRole.Port);
              clCustomerPortOk = serverRole.IsTcp && serverRole.IsTls && !serverMixedPorts.Contains(serverRole.Port);
              log.Trace("Client Customer port is {0}OK: TCP is {1}, TLS is {2}, Port no. is {3}, server/mixed port list: {4}", clCustomerPortOk ? "" : "NOT ", serverRole.IsTcp, serverRole.IsTls, serverRole.Port, string.Join(",", serverMixedPorts));
              break;

            case ServerRoleType.ClAppService:
              clientOnlyPorts.Add(serverRole.Port);
              clAppServicePortOk = serverRole.IsTcp && serverRole.IsTls && !serverMixedPorts.Contains(serverRole.Port);
              log.Trace("Client AppService port is {0}OK: TCP is {1}, TLS is {2}, Port no. is {3}, server/mixed port list: {4}", clAppServicePortOk ? "" : "NOT ", serverRole.IsTcp, serverRole.IsTls, serverRole.Port, string.Join(",", serverMixedPorts));
              break;

            default:
              log.Error("Unknown server role {0}.", serverRole.Role);
              error = true;
              break;
          }
        }

        bool portsOk = primaryPortOk
          && srNeighborPortOk
          && ndColleaguePortOk
          && clNonCustomerPortOk
          && clCustomerPortOk
          && clAppServicePortOk;


        Passed = !error && idOk && statusOk && portsOk;

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
