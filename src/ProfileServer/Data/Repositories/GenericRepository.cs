using IopServerCore.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Generic repository pattern for profile server database.
  /// </summary>
  /// <typeparam name="TEntity">Entity type.</typeparam>
  public class GenericRepository<TEntity> : GenericRepositoryBase<Context, TEntity> where TEntity : class
  {
    /// <summary>Unit of work instance that owns the repository.</summary>
    protected new UnitOfWork unitOfWork { get { return (UnitOfWork)base.unitOfWork; } }


    /// <summary>
    /// Creates instance of the setting repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public GenericRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }
  }
}
