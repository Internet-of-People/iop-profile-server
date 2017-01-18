using ProfileServer.Data.Models;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProfileServerProtocol;
using ProfileServer.Network;
using ProfileServer.Utils;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository of profile server followers.
  /// </summary>
  public class FollowerRepository : GenericRepository<Follower>
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.Repositories.FollowerRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public FollowerRepository(Context context)
      : base(context)
    {
    }


    /// <summary>
    /// Deletes follower server and all neighborhood actions for it from the database.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="FollowerId">Identifier of the follower server to delete.</param>
    /// <param name="ActionId">If there is a neighborhood action that should NOT be deleted, this is its ID, otherwise it is -1.</param>
    /// <returns>Status.Ok if the function succeeds, Status.ErrorNotFound if the function fails because a follower of the given ID was not found, 
    /// Status.ErrorInternal if the function fails for any other reason.</returns>
    public async Task<Status> DeleteFollower(UnitOfWork UnitOfWork, byte[] FollowerId, int ActionId = -1)
    {
      log.Trace("(FollowerId:'{0}',ActionId:{1})", FollowerId.ToHex(), ActionId);

      Status res = Status.ErrorInternal;
      bool dbSuccess = false;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await UnitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          Follower existingFollower = (await UnitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == FollowerId)).FirstOrDefault();
          if (existingFollower != null)
          {
            UnitOfWork.FollowerRepository.Delete(existingFollower);

            List<NeighborhoodAction> actions = (await UnitOfWork.NeighborhoodActionRepository.GetAsync(a => (a.ServerId == FollowerId) && (a.Id != ActionId))).ToList();
            if (actions.Count > 0)
            {
              foreach (NeighborhoodAction action in actions)
              {
                if (action.IsProfileAction())
                {
                  log.Debug("Action ID {0}, type {1}, serverId '{2}' will be removed from the database.", action.Id, action.Type, FollowerId.ToHex());
                  UnitOfWork.NeighborhoodActionRepository.Delete(action);
                }
              }
            }
            else log.Debug("No neighborhood actions for follower ID '{0}' found.", FollowerId.ToHex());

            await UnitOfWork.SaveThrowAsync();
            transaction.Commit();
            res = Status.Ok;
          }
          else
          {
            log.Warn("Follower ID '{0}' not found.", FollowerId.ToHex());
            res = Status.ErrorNotFound;
          }

          dbSuccess = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }


        if (!dbSuccess)
        {
          log.Warn("Rolling back transaction.");
          UnitOfWork.SafeTransactionRollback(transaction);
        }

        UnitOfWork.ReleaseLock(lockObjects);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Sets srNeighborPort of a follower to null.
    /// </summary>
    /// <param name="UnitOfWork">Unit of work instance.</param>
    /// <param name="FollowerId">Identifier of the follower server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ResetSrNeighborPort(UnitOfWork UnitOfWork, byte[] FollowerId)
    {
      log.Trace("(FollowerId:'{0}')", FollowerId.ToHex());

      bool res = false;
      bool dbSuccess = false;
      DatabaseLock lockObject = UnitOfWork.FollowerLock;
      using (IDbContextTransaction transaction = await UnitOfWork.BeginTransactionWithLockAsync(lockObject))
      {
        try
        {
          Follower follower = (await GetAsync(f => f.FollowerId == FollowerId)).FirstOrDefault();
          if (follower != null)
          {
            follower.SrNeighborPort = null;
            Update(follower);

            await UnitOfWork.SaveThrowAsync();
            transaction.Commit();
            res = true;
          }
          else log.Error("Unable to find follower ID '{0}'.", FollowerId.ToHex());

          dbSuccess = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!dbSuccess)
        {
          log.Warn("Rolling back transaction.");
          UnitOfWork.SafeTransactionRollback(transaction);
        }

        UnitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
