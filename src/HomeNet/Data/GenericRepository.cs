using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace HomeNet.Data
{
  /// <summary>
  /// Generic repository pattern allows us to easy implement our repositories.
  /// It implements basic and ordinary methods to prevent repeating same code over and over again.
  /// </summary>
  /// <typeparam name="TEntity">Entity type.</typeparam>
  public class GenericRepository<TEntity> where TEntity : class
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Data.GenericRepository");

    /// <summary>Database context.</summary>
    internal Context context;

    /// <summary>Access to entities in the database.</summary>
    internal DbSet<TEntity> dbSet;

    /// <summary>
    /// Sets up a database context of the repository and initializes DbSet for access to entities.
    /// </summary>
    /// <param name="context">Database context.</param>
    public GenericRepository(Context context)
    {
      this.context = context;
      this.dbSet = context.Set<TEntity>();
    }

    /// <summary>
    /// Obtains a list of entities based on the specified criteria.
    /// </summary>
    /// <param name="Filter">Specifies which entities should be returned from the database. If null, all entities are returned.</param>
    /// <param name="OrderBy">Specifies the order in which the matching entities are returned. If null, the default ordering is used.</param>
    /// <param name="NoTracking">If true, the returned entities are not tracked.</param>
    /// <returns></returns>
    public virtual IEnumerable<TEntity> Get(Expression<Func<TEntity, bool>> Filter = null, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> OrderBy = null, bool NoTracking = false)
    {
      log.Trace("()");
      IQueryable<TEntity> query = dbSet;

      if (NoTracking)
        query = query.AsNoTracking();

      if (Filter != null)
        query = query.Where(Filter);

      List<TEntity> result = OrderBy != null ? OrderBy(query).ToList() : query.ToList();
      log.Trace("(-):{0}", result != null ? "*Count=" + result.Count.ToString() : "null");
      return result;
    }

    /// <summary>
    /// Asynchronously obtains a list of entities based on the specified criteria.
    /// </summary>
    /// <param name="Filter">Specifies which entities should be returned from the database. If null, all entities are returned.</param>
    /// <param name="OrderBy">Specifies the order in which the matching entities are returned. If null, the default ordering is used.</param>
    /// <param name="NoTracking">If true, the returned entities are not tracked.</param>
    /// <returns></returns>
    public virtual async Task<IEnumerable<TEntity>> GetAsync(Expression<Func<TEntity, bool>> Filter = null, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> OrderBy = null, bool NoTracking = false)
    {
      log.Trace("()");
      IQueryable<TEntity> query = dbSet;

      if (NoTracking)
        query = query.AsNoTracking();

      if (Filter != null)
        query = query.Where(Filter);

      List<TEntity> result = await (OrderBy != null ? OrderBy(query).ToListAsync() : query.ToListAsync());
      log.Trace("(-):{0}", result != null ? "*Count=" + result.Count.ToString() : "null");
      return result;
    }

    /// <summary>
    /// Counts number of entities that match certain criteria.
    /// </summary>
    /// <param name="filter">Specifies matching criteria, can be null to count all entities.</param>
    /// <returns>Number of entities that match the criteria.</returns>
    public virtual int Count(Expression<Func<TEntity, bool>> filter)
    {
      IQueryable<TEntity> query = dbSet;
      int result = filter != null ? query.Count(filter) : query.Count();
      return result;
    }

    /// <summary>
    /// Asynchronously counts number of entities that match certain criteria.
    /// </summary>
    /// <param name="filter">Specifies matching criteria, can be null to count all entities.</param>
    /// <returns>Number of entities that match the criteria.</returns>
    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> filter)
    {
      IQueryable<TEntity> query = dbSet;
      int result = await (filter != null ? query.CountAsync(filter) : query.CountAsync());
      return result;
    }

    /// <summary>
    /// Obtains a list of entities based on the specified criteria with a limit on number of returned entities.
    /// </summary>
    /// <param name="filter">Specifies which entities should be returned from the database. If null, all entities are returned.</param>
    /// <param name="takeLimit">Number of entities to return at maximum. If set to 0, no limit is defined.</param>
    /// <param name="orderBy">Specifies the order in which the matching entities are returned. If null, the default ordering is used.</param>
    /// <returns></returns>
    public virtual IEnumerable<TEntity> GetLimit(Expression<Func<TEntity, bool>> filter = null, int takeLimit = 0, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null)
    {
      log.Trace("()");
      IQueryable<TEntity> query = dbSet;

      if (filter != null)
      {
        query = query.Where(filter);
      }

      if (takeLimit != 0)
      {
        query = query.Take(takeLimit);
      }

      List<TEntity> result = orderBy != null ? orderBy(query).ToList() : query.ToList();
      log.Trace("(-):{0}", result != null ? "*Count=" + result.Count.ToString() : "null");
      return result;
    }

    /// <summary>
    /// Asynchronously obtains a list of entities based on the specified criteria with a limit on number of returned entities.
    /// </summary>
    /// <param name="filter">Specifies which entities should be returned from the database. If null, all entities are returned.</param>
    /// <param name="takeLimit">Number of entities to return at maximum. If set to 0, no limit is defined.</param>
    /// <param name="orderBy">Specifies the order in which the matching entities are returned. If null, the default ordering is used.</param>
    /// <returns></returns>
    public virtual async Task<IEnumerable<TEntity>> GetLimitAsync(Expression<Func<TEntity, bool>> filter = null, int takeLimit = 0, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy = null)
    {
      log.Trace("()");
      IQueryable<TEntity> query = dbSet;

      if (filter != null)
      {
        query = query.Where(filter);
      }

      if (takeLimit != 0)
      {
        query = query.Take(takeLimit);
      }

      List<TEntity> result = await (orderBy != null ? orderBy(query).ToListAsync() : query.ToListAsync());
      log.Trace("(-):{0}", result != null ? "*Count=" + result.Count.ToString() : "null");
      return result;
    }

    /// <summary>
    /// Obtains a certain number of entities from the collection.
    /// </summary>
    /// <param name="Count">Number of entities to return at maximum.</param>
    /// <returns>Collection of entities that contains at most <paramref name="Count"/> items.</returns>
    public virtual IEnumerable<TEntity> Take(int Count)
    {
      log.Trace("(Count:{0})", Count);

      List<TEntity> result = dbSet.Take(Count).ToList();

      log.Trace("(-):{0}", result != null ? "*Count=" + result.Count.ToString() : "null");
      return result;
    }

    /// <summary>
    /// Asynchronously obtains a certain number of entities from the collection.
    /// </summary>
    /// <param name="Count">Number of entities to return at maximum.</param>
    /// <returns>Collection of entities that contains at most <paramref name="Count"/> items.</returns>
    public virtual async Task<IEnumerable<TEntity>> TakeAsync(int Count)
    {
      log.Trace("(Count:{0})", Count);

      List<TEntity> result = await dbSet.Take(Count).ToListAsync();

      log.Trace("(-):{0}", result != null ? "*Count=" + result.Count.ToString() : "null");
      return result;
    }

    /// <summary>
    /// Obtains entity by its primary identifier.
    /// </summary>
    /// <param name="id">Primary identifier of the entity.</param>
    /// <returns>Entity with the given primary identifier or null if no such entity exists.</returns>
    public virtual TEntity GetById(object id)
    {
      log.Trace("(id:{0})", id);
      TEntity result = dbSet.Find(id);

      log.Trace("(-):{0}", result);
      return result;
    }

    /// <summary>
    /// Asynchronously obtains entity by its primary identifier.
    /// </summary>
    /// <param name="id">Primary identifier of the entity.</param>
    /// <returns>Entity with the given primary identifier or null if no such entity exists.</returns>
    public virtual async Task<TEntity> GetByIdAsync(object id)
    {
      log.Trace("(id:{0})", id);

      TEntity result = await dbSet.FindAsync(id);

      log.Trace("(-):{0}", result);
      return result;
    }

    /// <summary>
    /// Adds entity to the database.
    /// </summary>
    /// <param name="entity">Entity to add.</param>
    public virtual void Insert(TEntity entity)
    {
      log.Trace("()");

      dbSet.Add(entity);

      log.Trace("(-)");
    }

    /// <summary>
    /// Finds and deletes an entity from the database based on its primary identifier.
    /// </summary>
    /// <param name="id">Primary identifier of the entity to delete.</param>
    public virtual void Delete(object id)
    {
      log.Trace("(id:{0})", id);

      throw new NotImplementedException("Wait for .NET Core 1.1.0");
      /*TEntity entityToDelete = dbSet.Find(id);
      Delete(entityToDelete);

      log.Trace("(-)");*/
    }

    /// <summary>
    /// Deletes an entity from the database.
    /// </summary>
    /// <param name="entityToDelete">Entity to delete.</param>
    public virtual void Delete(TEntity entityToDelete)
    {
      log.Trace("()");

      if (context.Entry(entityToDelete).State == EntityState.Detached)
        dbSet.Attach(entityToDelete);
      dbSet.Remove(entityToDelete);

      log.Trace("(-)");
    }

    /// <summary>
    /// Updates an entity in the database.
    /// </summary>
    /// <param name="entityToUpdate">Entity to update.</param>
    public virtual void Update(TEntity entityToUpdate)
    {
      log.Trace("()");

      dbSet.Attach(entityToUpdate);
      context.Entry(entityToUpdate).State = EntityState.Modified;

      log.Trace("(-)");
    }
  }
}

