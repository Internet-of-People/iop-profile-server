using ProfileServer.Data.Models;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;


namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository for identities hosted within this node's neighborhood.
  /// </summary>
  public class NeighborIdentityRepository : IdentityRepository<NeighborIdentity>
  {
    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="context">Database context.</param>
    public NeighborIdentityRepository(Context context)
      : base(context)
    {
    }
  }
}
