using System;
using ProfileServer.Kernel;
using System.Collections.Generic;
using ProfileServer.Config;
using System.Net;
using System.Threading;
using ProfileServer.Utils;
using ProfileServer.Data.Models;
using System.Linq;
using System.Threading.Tasks;

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
        if (DeleteUninitializedNeighbors()
          && DeleteUninitializedFollowers()
          && DeleteInvalidNeighborIdentities()
          && DeleteInvalidNeighborhoodActions())
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
    /// <returns>true if the function succeeds, false otherwise.</returns>
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
          else
          {
            res = true;
            log.Debug("No uninitialized followers found.");
          }
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


    /// <summary>
    /// Removes neighbor servers from database that we failed to finish the neighborhood initialization process with.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private bool DeleteUninitializedNeighbors()
    {
      log.Info("()");

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        List<Neighbor> neighborsToDelete = null;

        DatabaseLock lockObject = UnitOfWork.NeighborLock;
        unitOfWork.AcquireLock(lockObject);
        try
        {
          neighborsToDelete = unitOfWork.NeighborRepository.Get(n => n.LastRefreshTime == null).ToList();
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
        unitOfWork.ReleaseLock(lockObject);

        // Delete neighbor completely.
        if (neighborsToDelete.Count > 0)
        {
          bool error = false;
          log.Debug("Removing {0} uninitialized neighbors.", neighborsToDelete.Count);
          foreach (Neighbor neighbor in neighborsToDelete)
          {
            Task<bool> task = unitOfWork.NeighborRepository.DeleteNeighbor(unitOfWork, neighbor.NeighborId);
            if (!task.Result)
            {
              log.Error("Unable to delete neighbor ID '{0}' from the database.", neighbor.NeighborId.ToHex());
              error = true;
              break;
            }
          }

          res = !error;
        }
        else
        {
          res = true;
          log.Debug("No uninitialized neighbors found.");
        }
      }

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Finds neighbor identities for which there is no existing neighbor.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private bool DeleteInvalidNeighborIdentities()
    {
      log.Info("()");

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        // Disable change tracking for faster multiple deletes.
        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborIdentityLock, UnitOfWork.NeighborLock };
        unitOfWork.AcquireLock(lockObjects);
        try
        {
          List<byte[]> neighborIds = unitOfWork.NeighborRepository.Get(null, null, true).Select(n => n.NeighborId).ToList();
          HashSet<byte[]> neighborIdsHashSet = new HashSet<byte[]>(neighborIds, StructuralEqualityComparer<byte[]>.Default); 
          List<NeighborIdentity> identities = unitOfWork.NeighborIdentityRepository.Get(null, null, true).ToList();

          bool saveDb = false;
          int deleteCounter = 0;
          foreach (NeighborIdentity identity in identities)
          {
            if (!neighborIdsHashSet.Contains(identity.HostingServerId))
            {
              // Do not delete images here, ImageManager will delete them during its initialization.

              unitOfWork.NeighborIdentityRepository.Delete(identity);
              saveDb = true;
              deleteCounter++;
            }
          }

          if (saveDb)
          {
            log.Debug("Removing {0} identities without existing neighbor server.", deleteCounter);

            unitOfWork.SaveThrow();
          }
          else log.Debug("No identities without existing neighbor server found.");
          res = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObjects);
      }

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Removes neighborhood actions whose target servers do not exist in our database.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private bool DeleteInvalidNeighborhoodActions()
    {
      log.Info("()");

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
        unitOfWork.AcquireLock(lockObjects);
        try
        {
          List<byte[]> neighborIds = unitOfWork.NeighborRepository.Get().Select(n => n.NeighborId).ToList();
          HashSet<byte[]> neighborIdsHashSet = new HashSet<byte[]>(neighborIds, StructuralEqualityComparer<byte[]>.Default);

          List<byte[]> followerIds = unitOfWork.FollowerRepository.Get().Select(f => f.FollowerId).ToList();
          HashSet<byte[]> followerIdsHashSet = new HashSet<byte[]>(followerIds, StructuralEqualityComparer<byte[]>.Default);

          List<NeighborhoodAction> actions = unitOfWork.NeighborhoodActionRepository.Get().ToList();
          bool saveDb = false;
          foreach (NeighborhoodAction action in actions)
          {
            bool actionValid = false;
            if (action.IsProfileAction())
            {
              // Action's serverId should be our follower.
              actionValid = followerIdsHashSet.Contains(action.ServerId);
            }
            else
            {
              // Action's serverId should be our neighbor.
              actionValid = neighborIdsHashSet.Contains(action.ServerId);
            }

            if (!actionValid)
            {
              log.Debug("Removing invalid action ID {0}, type {1}, server ID '{2}'.", action.Id, action.Type, action.ServerId.ToHex());
              unitOfWork.NeighborhoodActionRepository.Delete(action);
              saveDb = true;
            }
          }

          if (saveDb)
          {
            res = unitOfWork.Save();
          }
          else
          {
            log.Debug("No invalid neighborhood actions found.");
            res = true;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObjects);
      }

      log.Info("(-):{0}", res);
      return res;
    }

  }
}
