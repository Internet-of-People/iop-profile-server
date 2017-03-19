using System;
using ProfileServer.Kernel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IopProtocol;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Network.LOC;

namespace ProfileServer.Network
{
  /// <summary>
  /// Location based network (LOC) is a part of IoP that the profile server relies on.
  /// When the profile server starts, this component connects to LOC and obtains information about the server's neighborhood.
  /// Then it keeps receiving updates from LOC about changes in the neighborhood structure.
  /// The profile server needs to share its database of hosted identities with its neighbors and it also accepts 
  /// requests to share foreign profiles and consider them during its own search queries.
  /// </summary>
  public class LocationBasedNetwork : Component
  {
#warning Regtest needed - LOC server refresh
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Network.LocationBasedNetwork";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer." + ComponentName);

    /// <summary>Event that is set when LocConnectionThread is not running.</summary>
    private ManualResetEvent locConnectionThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for communication with LOC.</summary>
    private Thread locConnectionThread;

    /// <summary>TCP client to connect with LOC server.</summary>
    private LocClient client;

    /// <summary>true if the component received current information about the server's neighborhood from the LOC server.</summary>
    private bool locServerInitialized = false;
    /// <summary>true if the component received current information about the server's neighborhood from the LOC server.</summary>
    public bool LocServerInitialized { get { return locServerInitialized; } }

    /// <summary>Lock object to protect write access to locServerInitialized.</summary>
    private object locServerInitializedLock = new object();


    /// <summary>
    /// Initializes the component.
    /// </summary>
    public LocationBasedNetwork():
      base(ComponentName)
    {
    }


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        client = new LocClient(Config.Configuration.LocEndPoint, new LocMessageProcessor(), ShutdownSignaling);

        locConnectionThread = new Thread(new ThreadStart(LocConnectionThread));
        locConnectionThread.Start();

        RegisterCronJobs();

        res = true;
        Initialized = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();

        if ((locConnectionThread != null) && !locConnectionThreadFinished.WaitOne(10000))
          log.Error("LOC connection thread did not terminated in 10 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      if (client != null) client.Dispose();

      if ((locConnectionThread != null) && !locConnectionThreadFinished.WaitOne(10000))
        log.Error("LOC connection thread did not terminated in 10 seconds.");

      log.Info("(-)");
    }


    /// <summary>
    /// Registers component's cron jobs.
    /// </summary>
    public void RegisterCronJobs()
    {
      log.Trace("()");

      List<CronJob> cronJobDefinitions = new List<CronJob>()
      {
        // Obtains fresh data from LOC server.
        { new CronJob() { Name = "refreshLocData", StartDelay = 67 * 60 * 1000, Interval = 601 * 60 * 1000, HandlerAsync = CronJobHandlerRefreshLocDataAsync } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for "refreshLocData" cron job.
    /// </summary>
    public async void CronJobHandlerRefreshLocDataAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await RefreshLocAsync();

      log.Trace("(-)");
    }




    /// <summary>
    /// Thread that is responsible for connection to LOC and processing LOC updates.
    /// If the LOC is not reachable, the thread will wait until it is reachable.
    /// If connection to LOC is established and closed for any reason, the thread will try to reconnect.
    /// </summary>
    private async void LocConnectionThread()
    {
      LogDiagnosticContext.Start();

      log.Info("()");

      locConnectionThreadFinished.Reset();

      try
      {
        while (!ShutdownSignaling.IsShutdown)
        {
          // Connect to LOC server.
          if (await client.ConnectAsync())
          {
            // Announce our primary server interface to LOC.
            if (await client.RegisterPrimaryServerRoleAsync(Config.Configuration.ServerRoles.GetRolePort((uint)ServerRole.Primary)))
            {
              // Ask LOC server about initial set of neighborhood nodes.
              if (await GetNeighborhoodInformationAsync())
              {
                // Receive and process updates.
                await client.ReceiveMessageLoopAsync();
              }

              await client.DeregisterPrimaryServerRoleAsync();
            }
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred (and rethrowing): {0}", e.ToString());
        await Task.Delay(5000);
        throw e;
      }

      if (client != null) client.Dispose();
      locConnectionThreadFinished.Set();

      log.Info("(-)");

      LogDiagnosticContext.Stop();
    }



    /// <summary>
    /// Sends a request to the LOC server to obtain an initial neighborhood information and then reads the response and processes it.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> GetNeighborhoodInformationAsync()
    {
      log.Info("()");

      bool res = false;

      LocProtocolMessage response = await client.GetNeighborhoodInformationAsync();
      if (response != null)
      {
        LocMessageProcessor locMessageProcessor = (LocMessageProcessor)client.MessageProcessor;
        res = await locMessageProcessor.ProcessMessageGetNeighbourNodesByDistanceResponseAsync(response, true);
      }

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Safely changes value of locServerInitialized.
    /// </summary>
    /// <param name="Value">New value for locServerInitialized.</param>
    public void SetLocServerInitialized(bool Value)
    {
      lock (locServerInitializedLock)
      {
        locServerInitialized = true;
      }
    }


    /// <summary>
    /// If the component is connected to LOC server, it sends a new GetNeighbourNodesByDistanceLocalRequest request 
    /// to get fresh information about the profile server's neighborhood.
    /// </summary>
    public async Task RefreshLocAsync()
    {
      log.Trace("()");

      if (locServerInitialized)
      {
        try
        {
          LocProtocolMessage request = client.MessageBuilder.CreateGetNeighbourNodesByDistanceLocalRequest();
          if (await client.SendMessageAsync(request))
          {
            log.Trace("GetNeighbourNodesByDistanceLocalRequest sent to LOC server to get fresh neighborhood data.");
          }
          else log.Warn("Unable to send message to LOC server.");
        }
        catch (Exception e)
        {
          log.Warn("Exception occurred: {0}", e.ToString());
        }
      }
      else log.Debug("LOC server not initialized yet, can not refresh.");

      log.Trace("(-)");
    }

  }
}
