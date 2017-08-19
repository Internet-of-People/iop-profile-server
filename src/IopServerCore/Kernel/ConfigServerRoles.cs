using IopCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IopServerCore.Kernel
{
  /// <summary>
  /// Class that implements with logic of the role server configuration.
  /// </summary>
  public class ConfigServerRoles
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("IopServerCore.Kernel.ServerRolesConfig");

    /// <summary>Mapping of opened server service ports to configured role servers.</summary>
    public Dictionary<int, RoleServerConfiguration> RoleServers = new Dictionary<int, RoleServerConfiguration>();

    /// <summary>
    /// Adds new server role to a specific port.
    /// </summary>
    /// <param name="Port">Port, on which the server role services are provided.</param>
    /// <param name="Role">Role to add to the port.</param>
    /// <param name="Encrypted">true if the communication of the port should be encrypted, false otherwise.</param>
    /// <param name="ClientKeepAliveTimeoutMs">Number of milliseconds after which the server's client is considered inactive and its connection can be terminated for the particular role.</param>
    /// <returns>true, if the function succeeds, false otherwise.</returns>
    /// <remarks>One port can be used to serve multiple roles, but only if their encryption settings are compatible.
    /// This means that an unencrypted server role can be set to the same port as another unencrypted server role,
    /// but can not be added to the port that an encrypted server role is used for. Similarly, two encrypted roles 
    /// can be put on the same port.</remarks>
    public bool AddRoleServer(int Port, uint Role, bool Encrypted, int ClientKeepAliveTimeoutMs)
    {
      log.Trace("(Port:{0},Role:{1},ClientKeepAliveTimeoutMs:{2})", Port, Role, ClientKeepAliveTimeoutMs);

      bool error = false;

      if (RoleServers.ContainsKey(Port))
      {
        RoleServerConfiguration server = RoleServers[Port];
        // One port can only combine service that are either both encrypted or unencrypted.
        if (server.Encrypted == Encrypted)
        {
          server.Roles |= Role;
          server.ClientKeepAliveTimeoutMs = Math.Max(server.ClientKeepAliveTimeoutMs, ClientKeepAliveTimeoutMs);
        }
        else
        {
          log.Error("Unable to put {0} server role '{1}' to port {2}, which is {3}.", Encrypted ? "encrypted" : "unencrypted", Role, Port, server.Encrypted ? "encrypted" : "unencrypted");
          error = true;
        }
      }
      else
      {
        RoleServerConfiguration server = new RoleServerConfiguration()
        {
          Encrypted = Encrypted,
          IsTcpServer = true,
          Port = Port,
          Roles = Role,
          ClientKeepAliveTimeoutMs = ClientKeepAliveTimeoutMs
        };
        RoleServers.Add(Port, server);
      }

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Gets a port number for a specific server role.
    /// </summary>
    /// <param name="Role">Server role to get port number for.</param>
    /// <returns>Port number on which the server role is served, or 0 if no port servers the role.</returns>
    public int GetRolePort(uint Role)
    {
      log.Trace("(Role:{0})", Role);

      int res = 0;
      foreach (RoleServerConfiguration rsc in RoleServers.Values)
      {
        if ((rsc.Roles & Role) != 0)
        {
          res = rsc.Port;
          break;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
