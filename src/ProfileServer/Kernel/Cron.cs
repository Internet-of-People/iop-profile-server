using System;
using ProfileServer.Kernel;
using ProfileServer.Kernel.Config;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using ProfileServer.Utils;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProfileServer.Kernel
{
  /// <summary>
  /// The Cron component is responsible for several maintanence tasks:
  ///  * periodically check follower servers to prevent expiration and deleting of shared profiles on follower servers,
  ///  * periodically check for expired hosted identities and delete them,
  ///  * periodically check for expired neighbor identities and delete them and the neighbors themselves,
  ///  * periodically check and delete unused profile images,
  ///  * periodically refresh data from LOC server.
  /// </summary>
  public class Cron : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Kernel.Cron");

    /// <summary>How quickly (in milliseconds) after the component start will checkFollowersRefreshTimer signal for the first time.</summary>
    private const int CheckFollowersRefreshTimerStartDelay = 19 * 1000;

    /// <summary>Interval (in milliseconds) for checkFollowersRefreshTimer.</summary>
    private const int CheckFollowersRefreshTimerInterval = 11 * 60 * 1000;


    /// <summary>How quickly (in milliseconds) after the component start will checkExpiredHostedIdentitiesRefreshTimer signal for the first time.</summary>
    private const int CheckExpiredHostedIdentitiesTimerStartDelay = 59 * 1000;

    /// <summary>Interval (in milliseconds) for checkExpiredHostedIdentitiesRefreshTimer.</summary>
    private const int CheckExpiredHostedIdentitiesTimerInterval = 119 * 60 * 1000;


    /// <summary>
    /// How quickly (in milliseconds) after the component start will checkExpiredNeighborIdentitiesRefreshTimer signal for the first time.
    /// <para>
    /// We want a certain delay here after the start of the server to allow getting fresh neighborhood information from the LOC server.
    /// But if LOC server is not initialized by then, it does not matter, cleanup will be postponed.
    /// </para>
    /// </summary>
    private const int CheckExpiredNeighborIdentitiesTimerStartDelay = 5 * 60 * 1000;

    /// <summary>Interval (in milliseconds) for checkExpiredNeighborIdentitiesRefreshTimer.</summary>
    private const int CheckExpiredNeighborIdentitiesTimerInterval = 31 * 60 * 1000;



    /// <summary>How quickly (in milliseconds) after the component start will checkUnusedImagesTimer signal for the first time.</summary>
    private const int CheckUnusedImagesTimerStartDelay = 200 * 1000;

    /// <summary>Interval (in milliseconds) for checkUnusedImagesTimer.</summary>
    private const int CheckUnusedImagesTimerInterval = 37 * 60 * 1000;


    /// <summary>How quickly (in milliseconds) after the component start will refreshLocDataTimer signal for the first time.</summary>
    private const int RefreshLocDataTimerStartDelay = 67 * 60 * 1000;

    /// <summary>Interval (in milliseconds) for refreshLocDataTimer.</summary>
    private const int RefreshLocDataTimerInterval = 601 * 60 * 1000;



    /// <summary>Timer that invokes checks of follower servers.</summary>
    private static Timer checkFollowersRefreshTimer;

    /// <summary>Event that is set by checkFollowersRefreshTimer.</summary>
    private static AutoResetEvent checkFollowersRefreshEvent = new AutoResetEvent(false);


    /// <summary>Timer that invokes checks of hosted identities.</summary>
    private static Timer checkExpiredHostedIdentitiesTimer;

    /// <summary>Event that is set by checkExpiredHostedIdentitiesTimer.</summary>
    private static AutoResetEvent checkExpiredHostedIdentitiesEvent = new AutoResetEvent(false);


    /// <summary>Timer that invokes checks of neighbor identities.</summary>
    private static Timer checkExpiredNeighborIdentitiesTimer;

    /// <summary>Event that is set by checkExpiredNeighborIdentitiesTimer.</summary>
    private static AutoResetEvent checkExpiredNeighborIdentitiesEvent = new AutoResetEvent(false);



    /// <summary>Timer that invokes checks of neighbor identities.</summary>
    private static Timer checkUnusedImagesTimer;

    /// <summary>Event that is set by checkUnusedImagesTimer.</summary>
    private static AutoResetEvent checkUnusedImagesEvent = new AutoResetEvent(false);


    /// <summary>Timer that invokes checks of neighbor identities.</summary>
    private static Timer refreshLocDataTimer;

    /// <summary>Event that is set by refreshLocDataTimer.</summary>
    private static AutoResetEvent refreshLocDataEvent = new AutoResetEvent(false);



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
        checkFollowersRefreshTimer = new Timer(SignalTimerCallback, checkFollowersRefreshEvent, CheckFollowersRefreshTimerStartDelay, CheckFollowersRefreshTimerInterval);
        checkExpiredHostedIdentitiesTimer = new Timer(SignalTimerCallback, checkExpiredHostedIdentitiesEvent, CheckExpiredHostedIdentitiesTimerStartDelay, CheckExpiredHostedIdentitiesTimerInterval);
        checkExpiredNeighborIdentitiesTimer = new Timer(SignalTimerCallback, checkExpiredNeighborIdentitiesEvent, CheckExpiredNeighborIdentitiesTimerStartDelay, CheckExpiredNeighborIdentitiesTimerInterval);
        checkUnusedImagesTimer = new Timer(SignalTimerCallback, checkUnusedImagesEvent, CheckUnusedImagesTimerStartDelay, CheckUnusedImagesTimerInterval);
        refreshLocDataTimer = new Timer(SignalTimerCallback, refreshLocDataEvent, RefreshLocDataTimerStartDelay, RefreshLocDataTimerInterval);

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

        if ((executiveThread != null) && !executiveThreadFinished.WaitOne(10000))
          log.Error("Executive thread did not terminated in 10 seconds.");

        if (checkFollowersRefreshTimer != null) checkFollowersRefreshTimer.Dispose();
        checkFollowersRefreshTimer = null;

        if (checkExpiredHostedIdentitiesTimer != null) checkExpiredHostedIdentitiesTimer.Dispose();
        checkExpiredHostedIdentitiesTimer = null;

        if (checkExpiredNeighborIdentitiesTimer != null) checkExpiredNeighborIdentitiesTimer.Dispose();
        checkExpiredNeighborIdentitiesTimer = null;

        if (checkUnusedImagesTimer != null) checkUnusedImagesTimer.Dispose();
        checkUnusedImagesTimer = null;

        if (refreshLocDataTimer != null) refreshLocDataTimer.Dispose();
        refreshLocDataTimer = null;
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      if (checkFollowersRefreshTimer != null) checkFollowersRefreshTimer.Dispose();
      checkFollowersRefreshTimer = null;

      if (checkExpiredHostedIdentitiesTimer != null) checkExpiredHostedIdentitiesTimer.Dispose();
      checkExpiredHostedIdentitiesTimer = null;

      if (checkExpiredNeighborIdentitiesTimer != null) checkExpiredNeighborIdentitiesTimer.Dispose();
      checkExpiredNeighborIdentitiesTimer = null;

      if (checkUnusedImagesTimer != null) checkUnusedImagesTimer.Dispose();
      checkUnusedImagesTimer = null;

      if (refreshLocDataTimer != null) refreshLocDataTimer.Dispose();
      refreshLocDataTimer = null;

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

      while (!ShutdownSignaling.IsShutdown)
      {
        log.Info("Waiting for event.");

        WaitHandle[] handles = new WaitHandle[] { ShutdownSignaling.ShutdownEvent, checkFollowersRefreshEvent, checkExpiredHostedIdentitiesEvent, checkExpiredNeighborIdentitiesEvent };

        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == ShutdownSignaling.ShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        if (handles[index] == checkFollowersRefreshEvent)
        {
          log.Trace("checkFollowersRefreshEvent activated.");

          Data.Database database = (Data.Database)Base.ComponentDictionary["Data.Database"];
          database.CheckFollowersRefresh();
        }
        else if (handles[index] == checkExpiredHostedIdentitiesEvent)
        {
          log.Trace("checkExpiredHostedIdentitiesEvent activated.");

          Data.Database database = (Data.Database)Base.ComponentDictionary["Data.Database"];
          database.CheckExpiredHostedIdentities();
        }
        else if (handles[index] == checkExpiredNeighborIdentitiesEvent)
        {
          log.Trace("checkExpiredNeighborIdentitiesEvent activated.");

          Network.LocationBasedNetwork locationBasedNetwork = (Network.LocationBasedNetwork)Base.ComponentDictionary["Network.LocationBasedNetwork"];
          if (locationBasedNetwork.LocServerInitialized)
          {
            Data.Database database = (Data.Database)Base.ComponentDictionary["Data.Database"];
            database.CheckExpiredNeighborIdentities();
          }
          else log.Debug("LOC component is not in sync with the LOC server yet, checking expired neighbors will not be executed now.");
        } 
        else if (handles[index] == checkUnusedImagesEvent)
        {
          log.Trace("checkUnusedImagesEvent activated.");

          ImageManager imageManager = (ImageManager)Base.ComponentDictionary["Data.ImageManager"];
          imageManager.ProcessImageDeleteList();
        }
        else if (handles[index] == refreshLocDataEvent)
        {
          log.Trace("refreshLocDataEvent activated.");

          Network.LocationBasedNetwork locationBasedNetwork = (Network.LocationBasedNetwork)Base.ComponentDictionary["Network.LocationBasedNetwork"];
          locationBasedNetwork.RefreshLoc();
        }
      }

      executiveThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Callback routine of checkFollowersRefreshTimer.
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
  }
}
