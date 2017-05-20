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
  /// Repository for identities hosted within this profile server's neighborhood.
  /// </summary>
  public class NeighborIdentityRepository : IdentityRepositoryBase<NeighborIdentity>
  {
    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public NeighborIdentityRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }
  }
}
