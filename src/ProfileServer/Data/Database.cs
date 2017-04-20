using System;
using ProfileServer.Kernel;
using ProfileServer.Kernel.Config;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using ProfileServer.Utils;
using ProfileServer.Data.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProfileServer.Data
{
  /// <summary>
  /// Database component is responsible for initialization of the database during the startup and performing database cleanup tasks.
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



    /// <summary>
    /// Checks if any of the follower servers need refresh. If so, a neighborhood action is created.
    /// <para>This function also checks if there are unprocessed refresh neighborhood actions 
    /// and if there are 3 such requests already, the follower is deleted as it is considered as unresponsive for too long.</para>
    /// </summary>
    public void CheckFollowersRefresh()
    {
      log.Trace("()");

      // If a follower server's LastRefreshTime is lower than this limit, it should be refreshed.
      DateTime limitLastRefreshTime = DateTime.UtcNow.AddSeconds(-Base.Configuration.FollowerRefreshTimeSeconds);

      List<byte[]> followersToDeleteIds = new List<byte[]>();

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
        unitOfWork.AcquireLock(lockObjects);
        try
        {
          List<Follower> followersToRefresh = unitOfWork.FollowerRepository.Get(f => f.LastRefreshTime < limitLastRefreshTime, null, true).ToList();
          if (followersToRefresh.Count > 0)
          {
            bool saveDb = false;
            int actionsInserted = 0;

            log.Debug("There are {0} followers that need refresh.", followersToRefresh.Count);
            foreach (Follower follower in followersToRefresh)
            {
              int unprocessedRefreshProfileActions = unitOfWork.NeighborhoodActionRepository.Count(a => (a.ServerId == follower.FollowerId) && (a.Type == NeighborhoodActionType.RefreshProfiles));
              if (unprocessedRefreshProfileActions < 3)
              {
                NeighborhoodAction action = new NeighborhoodAction()
                {
                  ServerId = follower.FollowerId,
                  Type = NeighborhoodActionType.RefreshProfiles,
                  Timestamp = DateTime.UtcNow,
                  ExecuteAfter = DateTime.UtcNow,
                  TargetIdentityId = null,
                  AdditionalData = null
                };

                unitOfWork.NeighborhoodActionRepository.Insert(action);
                log.Debug("Refresh neighborhood action for follower ID '{0}' will be inserted to the database.", follower.FollowerId.ToHex());
                saveDb = true;
                actionsInserted++;
              }
              else
              {
                log.Debug("There are {0} unprocessed RefreshProfiles neighborhood actions for follower ID '{1}'. Follower will be deleted.", unprocessedRefreshProfileActions, follower.FollowerId.ToHex());
                followersToDeleteIds.Add(follower.FollowerId);
              }
            }

            if (saveDb)
            {
              unitOfWork.SaveThrow();
              log.Debug("{0} new neighborhood actions saved to the database.", actionsInserted);
            }
          }
          else log.Debug("No followers need refresh now.");
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObjects);


        if (followersToDeleteIds.Count > 0)
        {
          log.Debug("There are {0} followers to be deleted.", followersToDeleteIds.Count);
          foreach (byte[] followerToDeleteId in followersToDeleteIds)
          {
            Iop.Profileserver.Status status = unitOfWork.FollowerRepository.DeleteFollower(unitOfWork, followerToDeleteId).Result;
            if (status == Iop.Profileserver.Status.Ok) log.Debug("Follower ID '{0}' deleted.", followerToDeleteId.ToHex());
            else log.Warn("Unable to delete follower ID '{0}', error code {1}.", followerToDeleteId.ToHex(), status);
          }
        }
        else log.Debug("No followers to delete now.");
      }


      log.Trace("(-)");
    }


    /// <summary>
    /// Checks if any of the hosted identities expired.
    /// If so, it deletes them.
    /// </summary>
    public void CheckExpiredHostedIdentities()
    {
      log.Trace("()");

      DateTime now = DateTime.UtcNow;
      List<byte[]> imagesToDelete = new List<byte[]>();
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        // Disable change tracking for faster multiple deletes.
        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        DatabaseLock lockObject = UnitOfWork.HostedIdentityLock;
        unitOfWork.AcquireLock(lockObject);
        try
        {
          List<HostedIdentity> expiredIdentities = unitOfWork.HostedIdentityRepository.Get(i => i.ExpirationDate < now, null, true).ToList();
          if (expiredIdentities.Count > 0)
          {
            log.Debug("There are {0} expired hosted identities.", expiredIdentities.Count);
            foreach (HostedIdentity identity in expiredIdentities)
            {
              if (identity.ProfileImage != null) imagesToDelete.Add(identity.ProfileImage);
              if (identity.ThumbnailImage != null) imagesToDelete.Add(identity.ThumbnailImage);

              unitOfWork.HostedIdentityRepository.Delete(identity);
              log.Debug("Identity ID '{0}' expired and will be deleted.", identity.IdentityId.ToHex());
            }

            unitOfWork.SaveThrow();
            log.Debug("{0} expired hosted identities were deleted.", expiredIdentities.Count);
          }
          else log.Debug("No expired hosted identities found.");
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }


      if (imagesToDelete.Count > 0)
      {
        ImageManager imageManager = (ImageManager)Base.ComponentDictionary["Data.ImageManager"];

        foreach (byte[] hash in imagesToDelete)
          imageManager.RemoveImageReference(hash);
      }


      log.Trace("(-)");
    }



    /// <summary>
    /// Checks if any of the neighbors expired.
    /// If so, it starts the process of their removal.
    /// </summary>
    public void CheckExpiredNeighbors()
    {
      log.Trace("()");

      // If a neighbor server's LastRefreshTime is lower than this limit, it is expired.
      DateTime limitLastRefreshTime = DateTime.UtcNow.AddSeconds(-Base.Configuration.NeighborProfilesExpirationTimeSeconds);

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        bool success = false;
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborLock, UnitOfWork.NeighborhoodActionLock };
        using (IDbContextTransaction transaction = unitOfWork.BeginTransactionWithLock(lockObjects))
        {
          try
          {
            List<Neighbor> expiredNeighbors = unitOfWork.NeighborRepository.Get(n => n.LastRefreshTime < limitLastRefreshTime, null, true).ToList();
            if (expiredNeighbors.Count > 0)
            {
              log.Debug("There are {0} expired neighbors.", expiredNeighbors.Count);
              foreach (Neighbor neighbor in expiredNeighbors)
              {
                // This action will cause our profile server to erase all profiles of the neighbor that has been removed.
                NeighborhoodAction action = new NeighborhoodAction()
                {
                  ServerId = neighbor.NeighborId,
                  Timestamp = DateTime.UtcNow,
                  Type = NeighborhoodActionType.RemoveNeighbor,
                  TargetIdentityId = null,
                  AdditionalData = null
                };
                unitOfWork.NeighborhoodActionRepository.Insert(action);
              }

              unitOfWork.SaveThrow();
              transaction.Commit();
            }
            else log.Debug("No expired neighbors found.");

            success = true;
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }

          if (!success)
          {
            log.Warn("Rolling back transaction.");
            unitOfWork.SafeTransactionRollback(transaction);
          }

          unitOfWork.ReleaseLock(lockObjects);
        }
      }

      log.Trace("(-)");
    }

  }
}
