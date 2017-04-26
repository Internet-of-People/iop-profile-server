using ProfileServer.Data.Models;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopCommon;
using IopServerCore.Data;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository of relations between hosted identities and other identities.
  /// </summary>
  public class RelatedIdentityRepository : GenericRepository<RelatedIdentity>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Repositories.RelatedIdentityRepository");


    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public RelatedIdentityRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }



    /// <summary>
    /// Obtains list of identity's relations.
    /// </summary>
    /// <param name="IdentityId">Identity of which relations are about to be obtained.</param>
    /// <param name="TypeFilter">Wildcard filter for identity type, or empty string if identity type filtering is not required.</param>
    /// <param name="IncludeInvalid">If true, the result can include expired or not yet valid relations. If false, only relations with valid cards are returned.</param>
    /// <param name="IssuerId">Network identifier of the relationship card issuer, or null if issuer based filtering is not required.</param>
    /// <returns>List of identity's relations that match the given criteria.</returns>
    public async Task<List<RelatedIdentity>> GetRelationsAsync(byte[] IdentityId, string TypeFilter, bool IncludeInvalid, byte[] IssuerId)
    {
      log.Trace("(IdentityId:'{0}',TypeFilter:'{1}',IncludeInvalid:{2},IssuerId:'{3}')", IdentityId.ToHex(), TypeFilter, IncludeInvalid, IssuerId.ToHex());

      IQueryable<RelatedIdentity> query = dbSet;

      query = query.Where(ri => ri.IdentityId == IdentityId);

      // Apply type filter if any.
      if (!string.IsNullOrEmpty(TypeFilter) && (TypeFilter != "*") && (TypeFilter != "**"))
      {
        Expression<Func<RelatedIdentity, bool>> typeFilterExpression = GetTypeFilterExpression<RelatedIdentity>(TypeFilter);
        query = query.Where(typeFilterExpression);
      }

      // Handle IncludeInvalid filter.
      if (!IncludeInvalid)
      {
        DateTime now = DateTime.UtcNow;
        query = query.Where(ri => (ri.ValidFrom <= now) && (now <= ri.ValidTo));
      }

      // Specific issuer filter.
      if (IssuerId != null)
      {
        query = query.Where(ri => ri.RelatedToIdentityId == IssuerId);
      }

      // Execute query.
      List<RelatedIdentity> res = await query.ToListAsync();

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Creates filter expression for type.
    /// </summary>
    /// <param name="WildcardFilter">Wildcard filter.</param>
    /// <returns>Filter expression for the database query.</returns>
    public static Expression<Func<RelatedIdentity, bool>> GetTypeFilterExpression<T>(string WildcardFilter) 
    {
      log.Trace("(WildcardFilter:'{0}')", WildcardFilter);
      string wildcardFilter = WildcardFilter.ToLowerInvariant();
      Expression<Func<RelatedIdentity, bool>> res = i => i.Type.ToLower() == wildcardFilter;

      // Example: WildcardFilter = "*abc"
      // This means that when filter STARTS with '*', we want the property value to END with "abc".
      // Note that WildcardFilter == "*" case is handled elsewhere.
      bool valueStartsWith = wildcardFilter.EndsWith("*");
      bool valueEndsWith = wildcardFilter.StartsWith("*");
      bool valueContains = valueStartsWith && valueEndsWith;

      if (valueContains)
      {
        wildcardFilter = wildcardFilter.Substring(1, wildcardFilter.Length - 2);
        res = i => i.Type.ToLower().Contains(wildcardFilter);
      }
      else if (valueStartsWith)
      {
        wildcardFilter = wildcardFilter.Substring(0, wildcardFilter.Length - 1);
        res = i => i.Type.ToLower().StartsWith(wildcardFilter);
      }
      else if (valueEndsWith)
      {
        wildcardFilter = wildcardFilter.Substring(1);
        res = i => i.Type.ToLower().EndsWith(wildcardFilter);
      }

      log.Trace("(-)");
      return res;
    }

  }
}
