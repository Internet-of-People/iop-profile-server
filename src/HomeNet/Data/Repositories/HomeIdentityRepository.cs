using HomeNet.Data.Models;
using Iop.Homenode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;


namespace HomeNet.Data.Repositories
{
  /// <summary>
  /// Repository for hosted identities.
  /// </summary>
  public class HomeIdentityRepository : GenericRepository<Identity>
  {
    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public HomeIdentityRepository(Context context)
      : base(context)
    {
    }

    /// <summary>
    /// Obtains hosted identities type statistics.
    /// </summary>
    /// <returns>List of statistics of hosted profile types.</returns>
    public async Task<List<ProfileStatsItem>> GetProfileStats()
    {
      return await context.Identities.GroupBy(i => i.Type).Select(g => new ProfileStatsItem { IdentityType = g.Key, Count = (uint)g.Count() }).ToListAsync();
    }
  }
}
