using ProfileServer.Data.Models;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopProtocol;
using ProfileServer.Network;
using ProfileServer.Kernel;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;
using IopServerCore.Kernel;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository of planned actions in the neighborhood.
  /// </summary>
  public class NeighborhoodActionRepository : GenericRepository<NeighborhoodAction>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Repositories.NeighborhoodActionRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public NeighborhoodActionRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }


    /// <summary>
    /// Adds neighborhood actions that will announce a change in a specific identity profile to all followers of the profile server.
    /// </summary>
    /// <param name="ActionType">Type of action on the identity profile.</param>
    /// <param name="IdentityId">Identifier of the identity which caused the action.</param>
    /// <param name="AdditionalData">Additional data to store with the action.</param>
    /// <returns>
    /// true if at least one new action was added to the database, false otherwise.
    /// <para>
    /// This function can throw database exception and the caller is expected to call it within try/catch block.
    /// </para>
    /// </returns>
    /// <remarks>The caller of this function is responsible starting a database transaction with FollowerLock and NeighborhoodActionLock locks.</remarks>
    public async Task<bool> AddIdentityProfileFollowerActionsAsync(NeighborhoodActionType ActionType, byte[] IdentityId, string AdditionalData = null)
    {
      log.Trace("(ActionType:{0},IdentityId:'{1}')", ActionType, IdentityId.ToHex());

      bool res = false;
      List<Follower> followers = (await unitOfWork.FollowerRepository.GetAsync()).ToList();
      if (followers.Count > 0)
      {
        // Disable change tracking for faster multiple inserts.
        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        DateTime now = DateTime.UtcNow;
        foreach (Follower follower in followers)
        {
          NeighborhoodAction neighborhoodAction = new NeighborhoodAction()
          {
            ServerId = follower.FollowerId,
            ExecuteAfter = null,
            TargetIdentityId = IdentityId,
            Timestamp = now,
            Type = ActionType,
            AdditionalData = AdditionalData
          };
          await InsertAsync(neighborhoodAction);

          res = true;
          log.Trace("Profile action with identity ID '{0}' added for follower ID '{1}'.", IdentityId.ToHex(), follower.FollowerId.ToHex());
        }
      }
      else log.Trace("No followers found to propagate identity profile change to.");

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Safely deletes action from the database.
    /// </summary>
    /// <param name="ActionId">Database ID of the action to delete.</param>
    /// <returns>true if the action was deleted, false otherwise.</returns>
    public async Task<bool> DeleteAsync(int ActionId)
    {
      log.Trace("(ActionId:{0})", ActionId);

      bool res = false;
      DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
      await unitOfWork.AcquireLockAsync(lockObject);
      try
      {
        NeighborhoodAction action = (await GetAsync(a => a.Id == ActionId)).FirstOrDefault();
        if (action != null)
        {
          Delete(action);
          log.Trace("Action ID {0} will be removed from database.", ActionId);

          if (await unitOfWork.SaveAsync())
            res = true;
        }
        else log.Info("Unable to find action ID {0} in the database, it has probably been removed already.", ActionId);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      unitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Installs InitializationProcessInProgress neighborhood action that will prevent 
    /// the profile server from sending updates to a new follower.
    /// </summary>
    /// <param name="FollowerId">Identifier of the follower to block updates to.</param>
    /// <returns>Action ID of the newly installed action, or -1 if the function fails.</returns>
    public async Task<int> InstallInitializationProcessInProgressAsync(byte[] FollowerId)
    {
      log.Trace("(FollowerId:'{0}')", FollowerId.ToHex());

      int res = -1;

      DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
      await unitOfWork.AcquireLockAsync(lockObject);

      try
      {
        // This action will make sure the profile server will not send updates to the new follower
        // until the neighborhood initialization process is complete.
        NeighborhoodAction action = new NeighborhoodAction()
        {
          ServerId = FollowerId,
          Type = NeighborhoodActionType.InitializationProcessInProgress,
          TargetIdentityId = null,
          Timestamp = DateTime.UtcNow,
          AdditionalData = null,

          // This will cause other actions to this follower to be postponed for 20 minutes from now.
          ExecuteAfter = DateTime.UtcNow.AddMinutes(20)
        };
        await InsertAsync(action);
        await unitOfWork.SaveThrowAsync();
        res = action.Id;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      unitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Uninstalls InitializationProcessInProgress neighborhood action that was installed by InstallInitializationProcessInProgress.
    /// </summary>
    /// <param name="FollowerId">Identifier of the follower.</param>
    /// <returns>true if the function suceeds, false otherwise.</returns>
    public async Task<bool> UninstallInitializationProcessInProgressAsync(int ActionId)
    {
      log.Trace("(ActionId:{0})", ActionId);

      bool res = false;

      DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
      await unitOfWork.AcquireLockAsync(lockObject);

      try
      {
        NeighborhoodAction action = (await GetAsync(a => a.Id == ActionId)).FirstOrDefault();
        if (action != null)
        {
          Delete(action);
          await unitOfWork.SaveThrowAsync();
          res = true;
        }
        else log.Error("Action ID {0} not found.", ActionId);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      unitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }


  }
}
