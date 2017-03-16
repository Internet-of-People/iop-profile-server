using ProfileServer.Data.Models;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopProtocol;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository for locally hosted identities.
  /// </summary>
  public class HostedIdentityRepository : IdentityRepository<HostedIdentity>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Repositories.HostedIdentityRepository");

    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public HostedIdentityRepository(Context context)
      : base(context)
    {
    }

    /// <summary>
    /// Obtains hosted identities type statistics.
    /// </summary>
    /// <returns>List of statistics of hosted profile types.</returns>
    public async Task<List<ProfileStatsItem>> GetProfileStatsAsync()
    {
      byte[] invalidVersion = SemVer.Invalid.ToByteArray();
      return await context.Identities.Where(i => (i.ExpirationDate == null) && (i.Version != invalidVersion)).GroupBy(i => i.Type)
        .Select(g => new ProfileStatsItem { IdentityType = g.Key, Count = (uint)g.Count() }).ToListAsync();
    }

    /// <summary>
    /// Obtains identity profile by its ID.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the identity profile to get.</param>
    /// <returns>Identity profile or null if the function fails.</returns>
    public async Task<HostedIdentity> GetHostedIdentityByIdAsync(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());
      HostedIdentity res = null;

      try
      {
        res = (await GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res != null ? "HostedIdentity" : "null");
      return res;
    }



    /// <summary>
    /// Obtains CanObjectHash from specific identity's profile.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the identity to get the data from.</param>
    /// <returns>CanObjectHash of the given identity or null if the function fails.</returns>
    public async Task<byte[]> GetCanObjectHashAsync(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());
      byte[] res = null;

      try
      {
        HostedIdentity identity = (await GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
        if (identity != null) res = identity.CanObjectHash;
        else log.Error("Identity ID '{0}' not found.", IdentityId.ToHex());
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):'{0}'", res != null ? res.ToBase58() : "");
      return res;
    }


    /// <summary>
    /// Sets a new value to CanObjectHash of an identity's profile.
    /// </summary>
    /// <param name="UunitOfWork">Unit of work instance.</param>
    /// <param name="IdentityId">Network identifier of the identity to set the value to.</param>
    /// <param name="NewValue">Value to set identity's CanObjectHash to.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SetCanObjectHashAsync(UnitOfWork UnitOfWork, byte[] IdentityId, byte[] NewValue)
    {
      log.Trace("(IdentityId:'{0}',NewValue:'{1}')", IdentityId.ToHex(), NewValue != null ? NewValue.ToBase58() : "");

      bool res = false;

      DatabaseLock lockObject = UnitOfWork.HostedIdentityLock;
      using (IDbContextTransaction transaction = await UnitOfWork.BeginTransactionWithLockAsync(lockObject))
      {
        try
        {
          HostedIdentity identity = (await GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
          if (identity != null)
          {
            identity.CanObjectHash = NewValue;
            Update(identity);
            await UnitOfWork.SaveThrowAsync();
            transaction.Commit();
            res = true;
          }
          else log.Error("Identity ID '{0}' not found.", IdentityId.ToHex());
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!res)
        {
          log.Warn("Rolling back transaction.");
          UnitOfWork.SafeTransactionRollback(transaction);
        }
      }

      UnitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
