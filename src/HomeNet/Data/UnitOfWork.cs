using HomeNet.Data.Models;
using HomeNet.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Data
{
  /// <summary>
  /// Coordinates the work of multiple repositories by creating a single database context class shared by all of them.
  /// </summary>
  public class UnitOfWork : IDisposable
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    private Context context = null;
    /// <summary>Database context.</summary>
    public Context Context
    {
      get
      {
        if (context == null)
          context = new Context();

        return context;
      }
    }


    private SettingsRepository settingsRepository;
    private GenericRepository<Identity> identityRepository;


    /// <summary>Settings repository.</summary>
    public SettingsRepository SettingsRepository
    {
      get
      {
        if (settingsRepository == null)
          settingsRepository = new SettingsRepository(Context);

        return settingsRepository;
      }
    }


    /// <summary>Identity repository.</summary>
    public GenericRepository<Identity> IdentityRepository
    {
      get
      {
        if (identityRepository == null)
          identityRepository = new GenericRepository<Identity>(Context);

        return identityRepository;
      }
    }



    /// <summary>
    /// Saves all changes in the context to the database.
    /// If an exception occurs during the operation, this function does not propagate the exception to the caller.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Save()
    {
      Task<bool> task = SaveAsync();
      return task.Result;
    }

    /// <summary>
    /// Asynchronously saves all changes in the context to the database.
    /// If an exception occurs during the operation, this function does not propagate the exception to the caller.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveAsync()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        await Context.SaveChangesAsync();
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
        if (e.InnerException != null)
        {
          log.Error("Inner exception level 1: {0}", e.InnerException.ToString());

          if (e.InnerException.InnerException != null)
            log.Error("Inner exception level 2: {0}", e.InnerException.InnerException.ToString());
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Asynchronously saves all changes in the context to the database. 
    /// If an exception occurs during the operation, this function propagates the exception to the caller.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveThrowAsync()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        await Context.SaveChangesAsync();
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
        if (e.InnerException != null)
        {
          log.Error("Inner exception level 1: {0}", e.InnerException.ToString());

          if (e.InnerException.InnerException != null)
            log.Error("Inner exception level 2: {0}", e.InnerException.InnerException.ToString());
        }
        throw e;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Starts the database transaction.
    /// </summary>
    /// <returns>Transaction object </returns>
    /// <remarks>
    /// In .NET Core, the transaction automatically commits unless it is explicitly rolled back.
    /// This means that one does not need to call DbContextTransaction.Commit() to commit the transaction,
    /// but one does need to call DbContextTransaction.Rollback() for a rollback.
    /// </remarks>
    public async Task<IDbContextTransaction> BeginTransactionAsync()
    {
      return await context.Database.BeginTransactionAsync();
    }

    
    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>
    /// Disposes the instance of the class.
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected virtual void Dispose(bool Disposing)
    {
      if (disposed) return;

      if (Disposing)
      {
        context.Dispose();
        context = null;
        disposed = true;
      }
    }
  }
}
