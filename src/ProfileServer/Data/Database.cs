using System;
using ProfileServer.Kernel;
using System.Collections.Generic;
using ProfileServer.Config;
using System.Net;
using System.Threading;
using ProfileServer.Utils;
using ProfileServer.Data.Models;
using System.Linq;

namespace ProfileServer.Data
{
  /// <summary>
  /// Database component is responsible for initialization of the database during the startup and cleanup during shutdown.
  /// </summary>
  public class Database : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.Database");


    public override bool Init()
    {
      log.Info("()");

      bool res = false;
      try
      {
        if (DeleteUninitializedFollowers())
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
        ShutdownSignaling.SignalShutdown();
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      log.Info("(-)");
    }


    /// <summary>
    /// Removes follower servers from database that failed to finish the neighborhood initialization process.
    /// </summary>
    private bool DeleteUninitializedFollowers()
    {
      log.Info("()");

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.FollowerLock;
        unitOfWork.AcquireLock(lockObject);
        try
        {
          List<Follower> followers = unitOfWork.FollowerRepository.Get(f => f.LastRefreshTime == null).ToList();
          if (followers.Count > 0)
          {
            log.Debug("Removing {0} uninitialized followers.", followers.Count);
            foreach (Follower follower in followers)
              unitOfWork.FollowerRepository.Delete(follower);

            res = unitOfWork.Save();
          }
          else log.Debug("No uninitialized followers found.");
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Info("(-):{0}", res);
      return res;
    }
  }
}
