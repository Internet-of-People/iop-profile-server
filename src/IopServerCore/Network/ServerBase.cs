using IopCommon;
using IopCrypto;
using IopServerCore.Kernel;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace IopServerCore.Network
{
  /// <summary>
  /// Network server component is responsible managing the role TCP servers.
  /// </summary>
  public abstract class ServerBase<TIncomingClient, TMessage> : Component
    where TIncomingClient : IncomingClientBase<TMessage>
  {
    public delegate TcpRoleServer<TIncomingClient, TMessage> RoleServerFactoryDelegate(IPAddress ipAddress, RoleServerConfiguration config);

    /// <summary>Name of the component. This must match the name of the derived component.</summary>
    public const string ComponentName = "Network.Server";

    /// <summary>Class logger.</summary>
    private static Logger _log = new Logger("IopServerCore." + ComponentName);

    private RoleServerFactoryDelegate _roleServerFactory;

    /// <summary>Collection of running TCP role servers sorted by their port.</summary>
    private Dictionary<int, TcpRoleServer<TIncomingClient, TMessage>> _servers = new Dictionary<int, TcpRoleServer<TIncomingClient, TMessage>>();

    /// <summary>List of network peers and clients across all role servers.</summary>
    private IncomingClientList<TMessage> _clients;

    /// <summary>Server's network identifier.</summary>
    public byte[] ServerId { get; private set; }

    /// <summary>
    /// Obtains the client list.
    /// </summary>
    /// <returns>List of all server's clients.</returns>
    public IncomingClientList<TMessage> Clients
    {
      get
      {
        _log.Trace("()");

        var res = _clients;

        _log.Trace("(-):*.Count={0}", res.Count);
        return res;
      }
    }

    /// <summary>
    /// Obtains list of running role servers.
    /// </summary>
    /// <returns>List of running role servers.</returns>
    public List<TcpRoleServer<TIncomingClient, TMessage>> Servers
    {
      get
      {
        _log.Trace("()");

        var res = new List<TcpRoleServer<TIncomingClient, TMessage>>(_servers.Values);

        _log.Trace("(-):*.Count={0}", res.Count);
        return res;
      }
    }

    /// <summary>
    /// Initializes the component.
    /// </summary>
    public ServerBase(RoleServerFactoryDelegate factory):
      base(ComponentName)
    {
      this._roleServerFactory = factory;
    }

    public override bool Init()
    {
      _log.Info("()");

      bool res = false;
      bool error = false;

      try
      {
        ConfigBase config = (ConfigBase)Base.ComponentDictionary[ConfigBase.ComponentName];
        ServerId = Crypto.Sha256(((KeysEd25519)config.Settings["Keys"]).PublicKey);
        _clients = new IncomingClientList<TMessage>();
        IPAddress ipAddress = (IPAddress)config.Settings["BindToInterface"];

        foreach (RoleServerConfiguration roleServer in ((ConfigServerRoles)config.Settings["ServerRoles"]).RoleServers.Values)
        {
          if (roleServer.IsTcpServer)
          {
            TcpRoleServer<TIncomingClient, TMessage> server = _roleServerFactory(ipAddress, roleServer);
            _servers.Add(server.EndPoint.Port, server);
          }
          else
          {
            _log.Fatal("UDP servers are not supported.");
            error = true;
            break;
          }
        }


        foreach (var server in _servers.Values)
        {
          if (!server.Start())
          {
            _log.Error("Unable to start TCP server {0}.", server.EndPoint);
            error = true;
            break;
          }
        }

        if (!error)
        {
          res = true;
          Initialized = true;
        }
      }
      catch (Exception e)
      {
        _log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();

        foreach (var server in _servers.Values)
        {
          if (server.IsRunning)
            server.Stop();
        }
        _servers.Clear();
      }

      _log.Info("(-):{0}", res);
      return res;
    }

    public override void Shutdown()
    {
      _log.Info("()");

      ShutdownSignaling.SignalShutdown();

      var clients = _clients.GetNetworkClientList();
      try
      {
        _log.Info("Closing {0} existing client connections of role servers.", clients.Count);
        foreach (var client in clients)
          client.CloseConnection();
      }
      catch
      {
      }

      foreach (var server in _servers.Values)
      {
        if (server.IsRunning)
          server.Stop();
      }
      _servers.Clear();

      _log.Info("(-)");
    }

    /// <summary>
    /// This method is responsible for going through all existing client connections 
    /// and closing those that are inactive for more time than allowed for a particular client type.
    /// Note that we are touching resources from different threads, so we have to expect the object are being 
    /// disposed at any time.
    /// </summary>
    public async Task CheckInactiveClientConnectionsAsync()
    {
      _log.Trace("()");

      try
      {
        var clients = _clients.GetNetworkClientList();
        foreach (var client in clients)
        {
          ulong id = 0;
          try
          {
            id = client.Id;
            _log.Trace("Client ID {0} has NextKeepAliveTime set to {1}.", id.ToHex(), client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));
            if (client.NextKeepAliveTime < DateTime.UtcNow)
            {
              // Client's connection is now considered inactive. 
              // We want to disconnect the client and remove it from the list.
              // If we dispose the client this will terminate the read loop in TcpRoleServer.ClientHandlerAsync,
              // which will then remove the client from the list, so we do not need to care about that.
              _log.Debug("Client ID {0} did not send any requests before {1} and is now considered as inactive. Closing client's connection.", id.ToHex(), client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));
              await client.CloseConnectionAsync();
            }
          }
          catch (Exception e)
          {
            _log.Info("Exception occurred while working with client ID {0}: {1}", id, e.ToString());
          }
        }
      }
      catch (Exception e)
      {
        _log.Error("Exception occurred: {0}", e.ToString());
      }

      _log.Trace("(-)");
    }
  }
}
