using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ProfileServerSimulator
{
  /// <summary>
  /// Simulator of LBN server. With each profile server we spawn a LBN server 
  /// which will provide information about the neighborhood to the profile server.
  /// </summary>
  public class LbnServer
  {
    private static PrefixLogger log;

    /// <summary>Interface IP address to listen on.</summary>
    public IPAddress IpAddress;

    /// <summary>TCP port to listen on.</summary>
    public int Port;

    /// <summary>Associated profile server.</summary>
    public ProfileServer ProfileServer;

    /// <summary>List of profile servers that are neighbors of ProfileServer.</summary>
    public Dictionary<string, ProfileServer> Neighbors = new Dictionary<string, ProfileServer>(StringComparer.Ordinal);

    /// <summary>TCP server that provides information about the neighborhood via LocNet protocol.</summary>
    public TcpListener Server;


    /// <summary>
    /// Initializes the LBN server instance.
    /// </summary>
    /// <param name="ProfileServer">Associated profile server.</param>
    public LbnServer(ProfileServer ProfileServer)
    {
      log = new PrefixLogger("ProfileServerSimulator.LbnServer", ProfileServer.Name);
      log.Trace("()");

      this.ProfileServer = ProfileServer;
      IpAddress = ProfileServer.IpAddress;
      Port = ProfileServer.LbnPort;

      log.Trace("(-)");
    }

    /// <summary>
    /// Starts the TCP server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Start()
    {
      log.Trace("()");

      bool res = false;

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Stops the TCP server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Stop()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (Server != null)
        {
          Server.Stop();
          Server.Server.Dispose();
          res = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources used by the LBN server.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      Stop();

      log.Trace("(-)");
    }
  }
}
