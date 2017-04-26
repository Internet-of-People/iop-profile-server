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
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;
using Iop.Shared;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository of profile server followers.
  /// </summary>
  public class FollowerRepository : GenericRepository<Follower>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Repositories.FollowerRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public FollowerRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }


    /// <summary>
    /// Deletes follower server and all neighborhood actions for it from the database.
    /// </summary>
    /// <param name="FollowerId">Identifier of the follower server to delete.</param>
    /// <param name="ActionId">If there is a neighborhood action that should NOT be deleted, this is its ID, otherwise it is -1.</param>
    /// <returns>Status.Ok if the function succeeds, Status.ErrorNotFound if the function fails because a follower of the given ID was not found, 
    /// Status.ErrorInternal if the function fails for any other reason.</returns>
    public async Task<Status> DeleteFollowerAsync(byte[] FollowerId, int ActionId = -1)
    {
      log.Trace("(FollowerId:'{0}',ActionId:{1})", FollowerId.ToHex(), ActionId);

      Status res = Status.ErrorInternal;
      bool dbSuccess = false;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          Follower existingFollower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == FollowerId)).FirstOrDefault();
          if (existingFollower != null)
          {
            Delete(existingFollower);

            List<NeighborhoodAction> actions = (await unitOfWork.NeighborhoodActionRepository.GetAsync(a => (a.ServerId == FollowerId) && (a.Id != ActionId))).ToList();
            if (actions.Count > 0)
            {
              foreach (NeighborhoodAction action in actions)
              {
                if (action.IsProfileAction())
                {
                  log.Debug("Action ID {0}, type {1}, serverId '{2}' will be removed from the database.", action.Id, action.Type, FollowerId.ToHex());
                  unitOfWork.NeighborhoodActionRepository.Delete(action);
                }
              }
            }
            else log.Debug("No neighborhood actions for follower ID '{0}' found.", FollowerId.ToHex());

            await unitOfWork.SaveThrowAsync();
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
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObjects);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Sets srNeighborPort of a follower to null.
    /// </summary>
    /// <param name="FollowerId">Identifier of the follower server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ResetSrNeighborPortAsync(byte[] FollowerId)
    {
      log.Trace("(FollowerId:'{0}')", FollowerId.ToHex());

      bool res = false;
      bool dbSuccess = false;
      DatabaseLock lockObject = UnitOfWork.FollowerLock;
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObject))
      {
        try
        {
          Follower follower = (await GetAsync(f => f.FollowerId == FollowerId)).FirstOrDefault();
          if (follower != null)
          {
            follower.SrNeighborPort = null;
            Update(follower);

            await unitOfWork.SaveThrowAsync();
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
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
