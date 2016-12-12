using ProfileServer.Data.Models;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProfileServerProtocol;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository for locally hosted identities.
  /// </summary>
  public class HostedIdentityRepository : IdentityRepository<HostedIdentity>
  {
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
  }
}
