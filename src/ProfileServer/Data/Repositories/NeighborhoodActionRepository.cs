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
  }
}
