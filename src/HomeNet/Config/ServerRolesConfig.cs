using HomeNet.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Config
{
  /// <summary>
  /// Class that implements with logic of the role server configuration.
  /// </summary>
  public class ServerRolesConfig
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Config.ServerRolesConfig");

    /// <summary>Mapping of opened server service ports to configured role servers.</summary>
    public Dictionary<int, RoleServerConfiguration> RoleServers = new Dictionary<int, RoleServerConfiguration>();

    /// <summary>
    /// Adds new server role to a specific port.
    /// </summary>
    /// <param name="Port">Port, on which the server role services are provided.</param>
    /// <param name="Role">Role to add to the port.</param>
    /// <returns>true, if the function succeeds, false otherwise.</returns>
    /// <remarks>One port can be used to serve multiple roles, but only if their encryption settings are compatible.
    /// This means that an unencrypted server role can be set to the same port as another unencrypted server role,
    /// but can not be added to the port that an encrypted server role is used for. Similarly, two encrypted roles 
    /// can be put on the same port.</remarks>
    public bool AddRoleServer(int Port, ServerRole Role)
    {
      log.Trace("(Port:{0},Role:{1})", Port, Role);

      bool error = false;

      bool encrypted = TcpRoleServer.ServerRoleEncryption[Role];

      if (RoleServers.ContainsKey(Port))
      {
        RoleServerConfiguration server = RoleServers[Port];
        // One port can only combine service that are either both encrypted or unencrypted.
        if (server.Encrypted == encrypted)
        {
          server.Roles |= Role;
        }
        else
        {
          log.Error("Unable to put {0} server role '{1}' to port {2}, which is {3}.", encrypted ? "encrypted" : "unencrypted", Role, Port, server.Encrypted ? "encrypted" : "unencrypted");
          error = true;
        }
      }
      else
      {
        RoleServerConfiguration server = new RoleServerConfiguration()
        {
          Encrypted = encrypted,
          IsTcpServer = true,
          Port = Port,
          Roles = Role
        };
        RoleServers.Add(Port, server);
      }

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
