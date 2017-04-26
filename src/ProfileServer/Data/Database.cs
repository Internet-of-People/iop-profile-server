using System;
using ProfileServer.Kernel;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using ProfileServer.Data.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;

namespace ProfileServer.Data
{
  /// <summary>
  /// Database component is responsible for initialization of the database during the startup and performing database cleanup tasks.
  /// </summary>
  public class Database : Component
  {
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Data.Database";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer." + ComponentName);


    /// <summary>
    /// Initializes the component.
    /// </summary>
    public Database():
      base(ComponentName)
    {
    }


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
          RegisterCronJobs();

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
    /// Registers component's cron jobs.
    /// </summary>
    public void RegisterCronJobs()
    {
      log.Trace("()");

      List<CronJob> cronJobDefinitions = new List<CronJob>()
      {
        // Checks if any of the followers need to be refreshed.
        { new CronJob() { Name = "checkFollowersRefresh", StartDelay = 19 * 1000, Interval = 11 * 60 * 1000, HandlerAsync = CronJobHandlerCheckFollowersRefreshAsync } },

        // Checks if any of the hosted identities expired and if so, it deletes them.
        { new CronJob() { Name = "checkExpiredHostedIdentities", StartDelay = 59 * 1000, Interval = 119 * 60 * 1000, HandlerAsync = CronJobHandlerCheckExpiredHostedIdentitiesAsync } },
      
        // Checks if any of the neighbors expired and if so, it deletes them.
        // We want a certain delay here after the start of the server to allow getting fresh neighborhood information from the LOC server.
        // But if LOC server is not initialized by then, it does not matter, cleanup will be postponed.
        { new CronJob() { Name = "checkExpiredNeighbors", StartDelay = 5 * 60 * 1000, Interval = 31 * 60 * 1000, HandlerAsync = CronJobHandlerCheckExpiredNeighborsAsync } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for "checkFollowersRefresh" cron job.
    /// </summary>
    public async void CronJobHandlerCheckFollowersRefreshAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await CheckFollowersRefreshAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for "checkExpiredHostedIdentities" cron job.
    /// </summary>
    public async void CronJobHandlerCheckExpiredHostedIdentitiesAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await CheckExpiredHostedIdentitiesAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for "checkExpiredNeighbors" cron job.
    /// </summary>
    public async void CronJobHandlerCheckExpiredNeighborsAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      Network.LocationBasedNetwork locationBasedNetwork = (Network.LocationBasedNetwork)Base.ComponentDictionary[Network.LocationBasedNetwork.ComponentName];
      if (locationBasedNetwork.LocServerInitialized)
      {
        await CheckExpiredNeighborsAsync();
      }
      else log.Debug("LOC component is not in sync with the LOC server yet, checking expired neighbors will not be executed now.");

      log.Trace("(-)");
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
            Task<bool> task = unitOfWork.NeighborRepository.DeleteNeighborAsync(neighbor.NeighborId);
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
    /// Finds and deletes neighbor identities for which there is no existing neighbor.
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
    public async Task CheckFollowersRefreshAsync()
    {
      log.Trace("()");

      // If a follower server's LastRefreshTime is lower than this limit, it should be refreshed.
      DateTime limitLastRefreshTime = DateTime.UtcNow.AddSeconds(-Config.Configuration.FollowerRefreshTimeSeconds);

      List<byte[]> followersToDeleteIds = new List<byte[]>();

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
        await unitOfWork.AcquireLockAsync(lockObjects);
        try
        {
          List<Follower> followersToRefresh = (await unitOfWork.FollowerRepository.GetAsync(f => f.LastRefreshTime < limitLastRefreshTime, null, true)).ToList();
          if (followersToRefresh.Count > 0)
          {
            bool saveDb = false;
            int actionsInserted = 0;

            log.Debug("There are {0} followers that need refresh.", followersToRefresh.Count);
            foreach (Follower follower in followersToRefresh)
            {
              int unprocessedRefreshProfileActions = await unitOfWork.NeighborhoodActionRepository.CountAsync(a => (a.ServerId == follower.FollowerId) && (a.Type == NeighborhoodActionType.RefreshProfiles));
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

                await unitOfWork.NeighborhoodActionRepository.InsertAsync(action);
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
              await unitOfWork.SaveThrowAsync();
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
            Iop.Shared.Status status = unitOfWork.FollowerRepository.DeleteFollowerAsync(followerToDeleteId).Result;
            if (status == Iop.Shared.Status.Ok) log.Debug("Follower ID '{0}' deleted.", followerToDeleteId.ToHex());
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
    public async Task CheckExpiredHostedIdentitiesAsync()
    {
      log.Trace("()");

      DateTime now = DateTime.UtcNow;
      List<byte[]> imagesToDelete = new List<byte[]>();
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        // Disable change tracking for faster multiple deletes.
        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        DatabaseLock lockObject = UnitOfWork.HostedIdentityLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          List<HostedIdentity> expiredIdentities = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.ExpirationDate < now, null, true)).ToList();
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

            await unitOfWork.SaveThrowAsync();
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
        ImageManager imageManager = (ImageManager)Base.ComponentDictionary[ImageManager.ComponentName];

        foreach (byte[] hash in imagesToDelete)
          imageManager.RemoveImageReference(hash);
      }


      log.Trace("(-)");
    }



    /// <summary>
    /// Checks if any of the neighbors expired.
    /// If so, it starts the process of their removal.
    /// </summary>
    public async Task CheckExpiredNeighborsAsync()
    {
      log.Trace("()");

      // If a neighbor server's LastRefreshTime is lower than this limit, it is expired.
      DateTime limitLastRefreshTime = DateTime.UtcNow.AddSeconds(-Config.Configuration.NeighborProfilesExpirationTimeSeconds);

      List<byte[]> neighborsToDeleteIds = new List<byte[]>();

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        bool success = false;
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborLock, UnitOfWork.NeighborhoodActionLock };
        using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
        {
          try
          {
            List<Neighbor> expiredNeighbors = (await unitOfWork.NeighborRepository.GetAsync(n => n.LastRefreshTime < limitLastRefreshTime, null, true)).ToList();
            if (expiredNeighbors.Count > 0)
            {
              bool saveDb = false;

              log.Debug("There are {0} expired neighbors.", expiredNeighbors.Count);
              foreach (Neighbor neighbor in expiredNeighbors)
              {
                int unprocessedRemoveNeighborActions = await unitOfWork.NeighborhoodActionRepository.CountAsync(a => (a.ServerId == neighbor.NeighborId) && (a.Type == NeighborhoodActionType.RemoveNeighbor));

                if (unprocessedRemoveNeighborActions == 0)
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
                  await unitOfWork.NeighborhoodActionRepository.InsertAsync(action);
                  saveDb = true;
                }
                else
                {
                  log.Debug("There is an unprocessed RemoveNeighbor neighborhood action for neighbor ID '{0}'. Neighbor will be deleted.", neighbor.NeighborId.ToHex());
                  neighborsToDeleteIds.Add(neighbor.NeighborId);
                }
              }

              if (saveDb)
              {
                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
              }
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


        if (neighborsToDeleteIds.Count > 0)
        {
          log.Debug("There are {0} neighbors to be deleted.", neighborsToDeleteIds.Count);
          foreach (byte[] neighborToDeleteId in neighborsToDeleteIds)
          {
            if (await unitOfWork.NeighborRepository.DeleteNeighborAsync(neighborToDeleteId)) log.Debug("Neighbor ID '{0}' deleted.", neighborToDeleteId.ToHex());
            else log.Warn("Unable to delete neighbor ID '{0}'.", neighborToDeleteId.ToHex());
          }
        }
        else log.Debug("No neighbors to delete now.");
      }

      log.Trace("(-)");
    }

  }
}
