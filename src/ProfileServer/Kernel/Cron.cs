using System;
using System.Collections.Generic;
using System.Threading;
using ProfileServer.Data;
using System.Linq;

namespace ProfileServer.Kernel
{
  /// <summary>
  /// Description of a job to execute periodically.
  /// </summary>
  public class CronJob
  {
    /// <summary>Name of the job.</summary>
    public string Name;

    /// <summary>How quickly (in milliseconds) after the component start will its timer signal for the first time.</summary>
    public int StartDelay;

    /// <summary>Interval (in milliseconds) of the job timer.</summary>
    public int Interval;

    /// <summary>Timer that signals the event when it triggers.</summary>
    public Timer Timer;

    /// <summary>Event that is signalled when the job should be executed.</summary>
    public AutoResetEvent Event = new AutoResetEvent(false);
  }


  /// <summary>
  /// The Cron component is responsible for executing jobs in periodical fashion.
  /// </summary>
  public class Cron : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Kernel.Cron");


    /// <summary>
    /// List of cron jobs mapped by their names.
    /// </summary>
    private static Dictionary<string, CronJob> jobs = new Dictionary<string, CronJob>();

    /// <summary>
    /// List of cron jobs and their parameters.
    /// </summary>
    private static List<CronJob> jobDefinitions = new List<CronJob>()
    {
      // Checks if any of the followers need to be refreshed.
      { new CronJob() { Name = "checkFollowersRefresh", StartDelay = 19 * 1000, Interval = 11 * 60 * 1000, } },

      // Checks if any of the hosted identities expired and if so, it deletes them.
      { new CronJob() { Name = "checkExpiredHostedIdentities", StartDelay = 59 * 1000, Interval = 119 * 60 * 1000, } },

      // Checks if any of the neighbors expired and if so, it deletes them.
      // We want a certain delay here after the start of the server to allow getting fresh neighborhood information from the LOC server.
      // But if LOC server is not initialized by then, it does not matter, cleanup will be postponed.
      { new CronJob() { Name = "checkExpiredNeighbors", StartDelay = 5 * 60 * 1000, Interval = 31 * 60 * 1000, } },

      // Deletes unused images from the images folder.
      { new CronJob() { Name = "deleteUnusedImages", StartDelay = 200 * 1000, Interval = 37 * 60 * 1000, } },

      // Obtains fresh data from LOC server.
      { new CronJob() { Name = "refreshLocData", StartDelay = 67 * 60 * 1000, Interval = 601 * 60 * 1000, } },

      // Checks if any of the opened TCP connections are inactive and if so, it closes them.
      { new CronJob() { Name = "checkInactiveClientConnections", StartDelay = 2 * 60 * 1000, Interval = 2 * 60 *1000, } },

      // Checks if there are any neighborhood actions to process.
      { new CronJob() { Name = "checkNeighborhoodActionList", StartDelay = 20 * 1000, Interval = 20 * 1000, } },

      // Refreshes profile server's IPNS record.
      { new CronJob() { Name = "ipnsRecordRefresh", StartDelay = 2 * 60 * 60 * 1000, Interval = 7 * 60 * 60 * 1000, } },
    };


    /// <summary>Event that is set when executiveThread is not running.</summary>
    private ManualResetEvent executiveThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for signals to perform checks.</summary>
    private Thread executiveThread;


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        foreach (CronJob job in jobDefinitions)
        {
          job.Timer = new Timer(SignalTimerCallback, job.Event, job.StartDelay, job.Interval);
          jobs.Add(job.Name, job);
        }

        executiveThread = new Thread(new ThreadStart(ExecutiveThread));
        executiveThread.Start();

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

        foreach (CronJob job in jobs.Values)
        {
          if (job.Timer != null) job.Timer.Dispose();
          job.Timer = null;
        }

        if ((executiveThread != null) && !executiveThreadFinished.WaitOne(10000))
          log.Error("Executive thread did not terminated in 10 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      foreach (CronJob job in jobs.Values)
      {
        if (job.Timer != null) job.Timer.Dispose();
        job.Timer = null;
      }

      if ((executiveThread != null) && !executiveThreadFinished.WaitOne(10000))
        log.Error("Executive thread did not terminated in 10 seconds.");

      log.Info("(-)");
    }


    /// <summary>
    /// Thread that is waiting for signals to perform checks.
    /// </summary>
    private void ExecutiveThread()
    {
      log.Info("()");

      executiveThreadFinished.Reset();

      List<WaitHandle> handleList = new List<WaitHandle>();
      handleList.Add(ShutdownSignaling.ShutdownEvent);
      foreach (CronJob job in jobs.Values)
        handleList.Add(job.Event);

      WaitHandle[] handles = handleList.ToArray();

      Data.Database database = (Data.Database)Base.ComponentDictionary["Data.Database"];
      Network.LocationBasedNetwork locationBasedNetwork = (Network.LocationBasedNetwork)Base.ComponentDictionary["Network.LocationBasedNetwork"];
      ImageManager imageManager = (ImageManager)Base.ComponentDictionary["Data.ImageManager"];
      Network.Server server = (Network.Server)Base.ComponentDictionary["Network.Server"];
      Network.NeighborhoodActionProcessor neighborhoodActionProcessor = (Network.NeighborhoodActionProcessor)Base.ComponentDictionary["Network.NeighborhoodActionProcessor"];
      Network.CAN.ContentAddressNetwork contentAddressNetwork = (Network.CAN.ContentAddressNetwork)Base.ComponentDictionary["Network.ContentAddressNetwork"];

      while (!ShutdownSignaling.IsShutdown)
      {
        log.Info("Waiting for event.");

        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == ShutdownSignaling.ShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        CronJob job = null;
        foreach (CronJob cronJob in jobs.Values)
        {
          if (handles[index] == cronJob.Event)
          {
            job = cronJob;
            break;
          }
        }

        log.Trace("Job '{0}' activated.", job.Name);
        switch (job.Name)
        {
          case "checkFollowersRefresh":
            database.CheckFollowersRefresh();
            break;

          case "checkExpiredHostedIdentities":
            database.CheckExpiredHostedIdentities();
            break;

          case "checkExpiredNeighbors":
            if (locationBasedNetwork.LocServerInitialized)
            {
              database.CheckExpiredNeighbors();
            }
            else log.Debug("LOC component is not in sync with the LOC server yet, checking expired neighbors will not be executed now.");
            break;

          case "deleteUnusedImages":
            imageManager.ProcessImageDeleteList();
            break;

          case "refreshLocData":
            locationBasedNetwork.RefreshLoc();
            break;

          case "checkInactiveClientConnections":
            server.CheckInactiveClientConnections();
            break;

          case "checkNeighborhoodActionList":
            neighborhoodActionProcessor.CheckActionList();
            break;

          case "ipnsRecordRefresh":
            contentAddressNetwork.IpnsRecordRefresh().Wait();
            break;
        }
      }

      executiveThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Callback routine of cron timers.
    /// We simply set an event to be handled by maintenance thread, not to occupy the timer for a long time.
    /// </summary>
    /// <param name="State">Event to signal.</param>
    private void SignalTimerCallback(object State)
    {
      log.Trace("()");

      AutoResetEvent eventToSignal = (AutoResetEvent)State;
      eventToSignal.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Signals an event of the given cron job.
    /// </summary>
    /// <param name="JobName"></param>
    public void SignalEvent(string JobName)
    {
      log.Trace("(JobName:'{0}')", JobName);

      AutoResetEvent signalEvent = jobs[JobName].Event;
      signalEvent.Set();

      log.Trace("(-)");
    }
  }
}
