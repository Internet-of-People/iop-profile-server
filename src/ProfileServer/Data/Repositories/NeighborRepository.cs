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
  /// Repository of profile server neighbors.
  /// </summary>
  public class NeighborRepository : RemoteServerRepositoryBase<Neighbor>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Repositories.NeighborRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public NeighborRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }


    public override DatabaseLock GetTableLock()
    {
      return UnitOfWork.NeighborLock;
    }



    /// <summary>
    /// Deletes neighbor server, all its profiles and all neighborhood actions for it from the database.
    /// </summary>
    /// <param name="NeighborId">Identifier of the neighbor server to delete.</param>
    /// <param name="ActionId">If there is a neighborhood action that should NOT be deleted, this is its ID, otherwise it is -1.</param>
    /// <param name="HoldingLocks">true if the caller is holding NeighborLock and NeighborhoodActionLock.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> DeleteNeighborAsync(byte[] NeighborId, int ActionId = -1, bool HoldingLocks = false)
    {
      log.Trace("(NeighborId:'{0}',ActionId:{1},HoldingLocks:{2})", NeighborId.ToHex(), ActionId, HoldingLocks);

      bool res = false;
      List<byte[]> imagesToDelete = new List<byte[]>();
      bool success = false;


      // Delete neighbor from the list of neighbors.
      DatabaseLock lockObject = UnitOfWork.NeighborLock;
      if (!HoldingLocks) await unitOfWork.AcquireLockAsync(lockObject);
      try
      {
        Neighbor neighbor = (await GetAsync(n => n.NetworkId == NeighborId)).FirstOrDefault();
        if (neighbor != null)
        {
          Delete(neighbor);
          await unitOfWork.SaveThrowAsync();
          log.Debug("Neighbor ID '{0}' deleted from database.", NeighborId.ToHex());
        }
        else
        {
          // If the neighbor does not exist, we set success to true as the result of the operation is as we want it 
          // and we gain nothing by trying to repeat the action later.
          log.Warn("Neighbor ID '{0}' not found.", NeighborId.ToHex());
        }

        success = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }
      if (!HoldingLocks) unitOfWork.ReleaseLock(lockObject);

      // Delete neighbor's profiles from the database.
      if (success)
      {
        success = false;

        // Disable change tracking for faster multiple deletes.
        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        lockObject = UnitOfWork.NeighborIdentityLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          List<NeighborIdentity> identities = (await unitOfWork.NeighborIdentityRepository.GetAsync(i => i.HostingServerId == NeighborId)).ToList();
          if (identities.Count > 0)
          {
            log.Debug("There are {0} identities of removed neighbor ID '{1}'.", identities.Count, NeighborId.ToHex());
            foreach (NeighborIdentity identity in identities)
            {
              if (identity.ThumbnailImage != null) imagesToDelete.Add(identity.ThumbnailImage);

              unitOfWork.NeighborIdentityRepository.Delete(identity);
            }

            await unitOfWork.SaveThrowAsync();
            log.Debug("{0} identities hosted on neighbor ID '{1}' deleted from database.", identities.Count, NeighborId.ToHex());
          }
          else log.Trace("No profiles hosted on neighbor ID '{0}' found.", NeighborId.ToHex());

          success = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);

        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = true;
      }

      if (success)
      {
        success = false;
        lockObject = UnitOfWork.NeighborhoodActionLock;
        if (!HoldingLocks) await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          // Do not delete the current action, it will be deleted just after this method finishes.
          List<NeighborhoodAction> actions = unitOfWork.NeighborhoodActionRepository.Get(a => (a.ServerId == NeighborId) && (a.Id != ActionId)).ToList();
          if (actions.Count > 0)
          {
            foreach (NeighborhoodAction action in actions)
            {
              log.Debug("Action ID {0}, type {1}, serverId '{2}' will be removed from the database.", action.Id, action.Type, NeighborId.ToHex());
              unitOfWork.NeighborhoodActionRepository.Delete(action);
            }

            await unitOfWork.SaveThrowAsync();
          }
          else log.Debug("No neighborhood actions for neighbor ID '{0}' found.", NeighborId.ToHex());

          success = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!HoldingLocks) unitOfWork.ReleaseLock(lockObject);
      }

      res = success;


      if (imagesToDelete.Count > 0)
      {
        ImageManager imageManager = (ImageManager)Base.ComponentDictionary[ImageManager.ComponentName];

        foreach (byte[] hash in imagesToDelete)
          imageManager.RemoveImageReference(hash);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Saves profiles of a neighbor from the memory to the database. This is done when the neighborhood initialization process is finished.
    /// </summary>
    /// <param name="IdentityDatabase">List of identities received from the neighbor.</param>
    /// <param name="NeighborId">Network ID of the neighbor.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveNeighborhoodInitializationProfilesAsync(Dictionary<byte[], NeighborIdentity> IdentityDatabase, byte[] NeighborId)
    {
      log.Trace("(IdentityDatabase.Count:{0},NeighborId:'{1}')", IdentityDatabase.Count, NeighborId.ToHex());

      bool error = false;
      bool success = false;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborIdentityLock, UnitOfWork.NeighborLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          Neighbor neighbor = (await GetAsync(n => n.NetworkId == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            // The neighbor is now initialized and is allowed to send us updates.
            neighbor.LastRefreshTime = DateTime.UtcNow;
            neighbor.Initialized = true;
            neighbor.SharedProfiles = IdentityDatabase.Count;
            Update(neighbor);

            // Insert all its activities.
            foreach (NeighborIdentity identity in IdentityDatabase.Values)
              await unitOfWork.NeighborIdentityRepository.InsertAsync(identity);

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            success = true;
          }
          else log.Error("Unable to find neighbor ID '{0}'.", NeighborId.ToHex());
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!success)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
          error = true;
        }

        unitOfWork.ReleaseLock(lockObjects);
      }


      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
