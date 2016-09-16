using System;
using HomeNet.Kernel;
using System.Collections.Generic;
using HomeNet.Config;
using System.Net;
using System.Threading;

namespace HomeNet.Network
{
  /// <summary>
  /// Network server component is responsible managing the node's TCP servers.
  /// </summary>
  public class Server : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Network.Server");

    /// <summary>Interval for role servers inactive client connection checks.</summary>
    private const int CheckInactiveClientConnectionsTimerInterval = 120000;



    /// <summary>Collection of running TCP role servers sorted by their port.</summary>
    private Dictionary<int, TcpRoleServer> tcpServers = new Dictionary<int, TcpRoleServer>();


    /// <summary>Timer that invokes checks of role servers client connections.</summary>
    private static Timer checkInactiveClientConnectionsTimer;

    /// <summary>Event that is set by checkInactiveClientConnectionsTimer.</summary>
    private static AutoResetEvent checkInactiveClientConnectionsEvent = new AutoResetEvent(false);



    /// <summary>Event that is set when clientMaintenanceThread is not running.</summary>
    private ManualResetEvent serversMaintenanceThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for maintenance of servers - e.g. closing inactive connections to their clients.</summary>
    private Thread serversMaintenanceThread;



    public override bool Init()
    {
      log.Info("()");

      bool res = false;
      bool error = false;

      try
      {
        checkInactiveClientConnectionsTimer = new Timer(CheckInactiveClientConnectionsTimerCallback, null, CheckInactiveClientConnectionsTimerInterval, CheckInactiveClientConnectionsTimerInterval);

        serversMaintenanceThread = new Thread(new ThreadStart(ServersMaintenanceThread));
        serversMaintenanceThread.Start();

        foreach (RoleServerConfiguration roleServer in Base.Configuration.ServerRoles.RoleServers.Values)
        {
          if (roleServer.IsTcpServer)
          {
            IPEndPoint endPoint = new IPEndPoint(Base.Configuration.ServerInterface, roleServer.Port);
            TcpRoleServer server = new TcpRoleServer(endPoint, roleServer.Encrypted, roleServer.Roles);
            tcpServers.Add(server.EndPoint.Port, server);
          }
          else
          {
            log.Fatal("UDP servers are not implemented.");
            error = true;
            break;
          }
        }


        foreach (TcpRoleServer server in tcpServers.Values)
        {
          if (!server.Start())
          {
            log.Error("Unable to start TCP server {0}.", server.EndPoint);
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
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        SignalLocalShutdown();

        if (checkInactiveClientConnectionsTimer != null) checkInactiveClientConnectionsTimer.Dispose();
        checkInactiveClientConnectionsTimer = null;

        foreach (TcpRoleServer server in tcpServers.Values)
        {
          if (server.IsRunning)
            server.Stop();
        }
        tcpServers.Clear();
      }

      log.Info("(-):{0}", res);
      return res;
    }

    public override void Shutdown()
    {
      log.Info("()");

      SignalLocalShutdown();

      if (checkInactiveClientConnectionsTimer != null) checkInactiveClientConnectionsTimer.Dispose();
      checkInactiveClientConnectionsTimer = null;

      if ((serversMaintenanceThread != null) && !serversMaintenanceThreadFinished.WaitOne(10000))
        log.Error("Servers maintenance thread did not terminated in 10 seconds.");

      foreach (TcpRoleServer server in tcpServers.Values)
      {
        List<Client> clients = server.GetClientListCopy();
        try
        {
          log.Info("Closing {0} existing client connections of server {1}.", clients.Count, server.EndPoint);
          foreach (Client client in clients)
            client.Dispose();
        } catch
        {
        }

        if (server.IsRunning)
          server.Stop();
      }
      tcpServers.Clear();

      log.Info("(-)");
    }


    /// <summary>
    /// Thread that is responsible for maintenance tasks invoked by event timers.
    /// </summary>
    private void ServersMaintenanceThread()
    {
      log.Info("()");

      serversMaintenanceThreadFinished.Reset();

      while (!IsShutdown)
      {
        log.Info("Waiting for event.");

        WaitHandle[] handles = new WaitHandle[] { localShutdownEvent, checkInactiveClientConnectionsEvent };

        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == localShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        if (handles[index] == checkInactiveClientConnectionsEvent)
        {
          log.Trace("checkInactiveClientConnectionsEvent activated.");
          CheckInactiveClientConnections();
        }
      }

      serversMaintenanceThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Callback routine of checkInactiveClientConnectionsTimer.
    /// We simply set an event to be handled by maintenance thread, not to occupy the timer for a long time.
    /// </summary>
    /// <param name="State">Not used.</param>
    private void CheckInactiveClientConnectionsTimerCallback(object State)
    {
      log.Trace("()");

      checkInactiveClientConnectionsEvent.Set();

      log.Trace("(-)");
    }

    /// <summary>
    /// This method is responsible for going through all existing client connections 
    /// and closing those that are inactive for more time than allowed for a particular client type.
    /// Note that we are touching resources from different threads, so we have to expect the object are being 
    /// disposed at any time.
    /// </summary>
    private void CheckInactiveClientConnections()
    {
      log.Trace("()");

      List<TcpRoleServer> servers = new List<TcpRoleServer>(tcpServers.Values);
      try
      {
        foreach (TcpRoleServer server in servers)
        {
          List<Client> clients = server.GetClientListCopy();
          foreach (Client client in clients)
          {
            uint id = 0;
            try
            {
              id = client.Id;
              log.Trace("Client ID 0x{0:X8} has NextKeepAliveTime set to {1}.", id, client.NextKeepAliveTime.ToString("yyyy-MM-dd HH:mm:ss"));
              if (client.NextKeepAliveTime < DateTime.UtcNow)
              {
                // Client's connection is now considered inactive. 
                // We want to disconnect the client and remove it from the list.
                // If we dispose the client this will terminate the read loop in TcpRoleServer.ClientHandlerAsync,
                // which will then remove the client from the list, so we do not need to care about that.
                log.Debug("Client ID 0x{0:X8} did not send any requests before {1} and is now considered as inactive. Disposing client.", id, client.NextKeepAliveTime);
                client.Dispose();
              }
            }
            catch (Exception e)
            {
              log.Info("Exception occurred while working with client ID {0}: {1}", id, e.ToString());
            }
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-)");
    }
  }
}
