﻿using IopCommon;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace IopServerCore.Data
{
  /// <summary>
  /// Synchronization object that is used to prevent race conditions while accessing database.
  /// </summary>
  public class DatabaseLock : IComparable
  {
    /// <summary>Lock object itself.</summary>
    public SemaphoreSlim Lock;

    /// <summary>Lock name for debugging purposes.</summary>
    public string Name;

    /// <summary>
    /// Creates an instance of the synchronization object.
    /// </summary>
    /// <param name="Name">Name to be assigned for debugging purposes.</param>
    public DatabaseLock(string Name)
    {
      Lock = new SemaphoreSlim(1);
      this.Name = Name;
    }

    public override string ToString()
    {
      return Name;
    }

    /// <summary>
    /// Compares the a database lock to another database lock by its name.
    /// </summary>
    /// <param name="Other">Database lock to compare with the current instance.</param>
    /// <returns>Less than zero if this instance precedes value. Zero if this instance has the same position in the sort order as value.
    /// Greater than zero if this instance follows value or the other instance is null.</returns>
    public int CompareTo(object Other)
    {
      if (Other == null) return 1;
      return Other != null ? string.CompareOrdinal(this.Name, ((DatabaseLock)Other).Name) : 1;
    }
  }

  /// <summary>
  /// Base class for unit of work pattern class.
  /// </summary>
  public class UnitOfWorkBase<TDbContext> : IDisposable where TDbContext : DbContext, new()
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("IopServerCore.Data.UnitOfWork");

    /// <summary>Database context.</summary>
    protected TDbContext context = null;
    /// <summary>Database context.</summary>
    public TDbContext Context
    {
      get
      {
        if (context == null)
          context = new TDbContext();

        return context;
      }
    }


    /// <summary>
    /// Saves all changes in the context to the database.
    /// If an exception occurs during the operation, this function does not propagate the exception to the caller.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Save()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        SaveThrow();
        res = true;
      }
      catch
      {
      }

      log.Trace("(-):{0}", res);
      return res;
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
        await SaveThrowAsync();
        res = true;
      }
      catch
      {
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Saves all changes in the context to the database. 
    /// If an exception occurs during the operation, this function propagates the exception to the caller.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public void SaveThrow()
    {
      log.Trace("()");

      try
      {
        context.SaveChanges();
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

        log.Trace("(-):throw");
        throw e;
      }

      log.Trace("(-)");
    }



    /// <summary>
    /// Asynchronously saves all changes in the context to the database. 
    /// If an exception occurs during the operation, this function propagates the exception to the caller.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task SaveThrowAsync()
    {
      log.Trace("()");

      try
      {
        await context.SaveChangesAsync();
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

        log.Trace("(-):throw");
        throw e;
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Starts database transaction and acquires a lock.
    /// The transaction makes sure the whole operation is atomic,
    /// the lock is used to prevent race conditions among threads.
    /// </summary>
    /// <param name="Lock">Lock to protect the transaction.</param>
    /// <returns>Entity Framework transaction object.</returns>
    /// <remarks>The caller is responsible for releasing the lock by calling ReleaseLock.</remarks>
    public IDbContextTransaction BeginTransactionWithLock(DatabaseLock Lock)
    {
      log.Trace("(Lock:{0})", Lock);

      Lock.Lock.Wait();
      IDbContextTransaction result = Context.Database.BeginTransaction(IsolationLevel.Serializable);

      log.Trace("(-)");
      return result;
    }


    /// <summary>
    /// Asynchronously starts database transaction and acquires a lock.
    /// The transaction makes sure the whole operation is atomic,
    /// the lock is used to prevent race conditions among threads.
    /// </summary>
    /// <param name="Lock">Lock to protect the transaction.</param>
    /// <returns>Entity Framework transaction object.</returns>
    /// <remarks>The caller is responsible for releasing the lock by calling ReleaseLock.</remarks>
    public async Task<IDbContextTransaction> BeginTransactionWithLockAsync(DatabaseLock Lock)
    {
      log.Trace("(Lock:{0})", Lock);

      await Lock.Lock.WaitAsync();
      IDbContextTransaction result = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

      log.Trace("(-)");
      return result;
    }


    /// <summary>
    /// Starts database transaction and acquires multiple locks.
    /// The transaction makes sure the whole operation is atomic,
    /// the locks are used to prevent race conditions among threads.
    /// </summary>
    /// <param name="Locks">Locks to protect the transaction.</param>
    /// <returns>Entity Framework transaction object.</returns>
    /// <remarks>The caller is responsible for releasing the locks by calling ReleaseLock.</remarks>
    public IDbContextTransaction BeginTransactionWithLock(DatabaseLock[] Locks)
    {
      // We have to sort the locks before we try to enter them, otherwise we could deadlock.
      Array.Sort(Locks);

      log.Trace("(Locks:[{0}])", string.Join<DatabaseLock>(",", Locks));

      for (int i = 0; i < Locks.Length; i++)
        Locks[i].Lock.Wait();

      IDbContextTransaction result = Context.Database.BeginTransaction(IsolationLevel.Serializable);

      log.Trace("(-)");
      return result;
    }


    /// <summary>
    /// Asynchronously starts database transaction and acquires multiple locks.
    /// The transaction makes sure the whole operation is atomic,
    /// the locks are used to prevent race conditions among threads.
    /// </summary>
    /// <param name="Locks">Locks to protect the transaction.</param>
    /// <returns>Entity Framework transaction object.</returns>
    /// <remarks>The caller is responsible for releasing the locks by calling ReleaseLock.</remarks>
    public async Task<IDbContextTransaction> BeginTransactionWithLockAsync(DatabaseLock[] Locks)
    {
      // We have to sort the locks before we try to enter them, otherwise we could deadlock.
      Array.Sort(Locks);

      log.Trace("(Locks:[{0}])", string.Join<DatabaseLock>(",", Locks));

      for (int i = 0; i < Locks.Length; i++)
        await Locks[i].Lock.WaitAsync();

      IDbContextTransaction result = await Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

      log.Trace("(-)");
      return result;
    }


    /// <summary>
    /// Rollbacks transaction and prevents exceptions.
    /// </summary>
    /// <param name="Transaction">Transaction to rollback.</param>
    public void SafeTransactionRollback(IDbContextTransaction Transaction)
    {
      try
      {
        Transaction.Rollback();
      }
      catch
      {
      }
    }

    /// <summary>
    /// Acquires a lock to prevent race conditions.
    /// </summary>
    /// <param name="Lock">Lock object to acquire.</param>
    /// <remarks>The caller is responsible for releasing the lock by calling ReleaseLock.</remarks>
    public void AcquireLock(DatabaseLock Lock)
    {
      log.Trace("(Lock:{0})", Lock);

      Lock.Lock.Wait();

      log.Trace("(-)");
    }


    /// <summary>
    /// Asynchronously acquires a lock to prevent race conditions.
    /// </summary>
    /// <param name="Lock">Lock object to acquire.</param>
    /// <remarks>The caller is responsible for releasing the lock by calling ReleaseLock.</remarks>
    public async Task AcquireLockAsync(DatabaseLock Lock)
    {
      log.Trace("(Lock:{0})", Lock);

      await Lock.Lock.WaitAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Acquires locks to prevent race conditions.
    /// </summary>
    /// <param name="Locks">Lock objects to acquire.</param>
    /// <remarks>The caller is responsible for releasing the locks by calling ReleaseLock.</remarks>
    public void AcquireLock(DatabaseLock[] Locks)
    {
      // We have to sort the locks before we try to enter them, otherwise we could deadlock.
      Array.Sort(Locks);

      log.Trace("(Locks:[{0}])", string.Join<DatabaseLock>(",", Locks));

      for (int i = 0; i < Locks.Length; i++)
        Locks[i].Lock.Wait();

      log.Trace("(-)");
    }


    /// <summary>
    /// Asynchronously acquires locks to prevent race conditions.
    /// </summary>
    /// <param name="Locks">Lock objects to acquire.</param>
    /// <remarks>The caller is responsible for releasing the locks by calling ReleaseLock.</remarks>
    public async Task AcquireLockAsync(DatabaseLock[] Locks)
    {
      // We have to sort the locks before we try to enter them, otherwise we could deadlock.
      Array.Sort(Locks);

      log.Trace("(Locks:[{0}])", string.Join<DatabaseLock>(",", Locks));

      for (int i = 0; i < Locks.Length; i++)
        await Locks[i].Lock.WaitAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Releases an acquired lock.
    /// </summary>
    /// <param name="Lock">Lock object to release.</param>
    public void ReleaseLock(DatabaseLock Lock)
    {
      log.Trace("(Lock:{0})", Lock);

      Lock.Lock.Release();

      log.Trace("(-)");
    }


    /// <summary>
    /// Releases acquired locks.
    /// </summary>
    /// <param name="Locks">Lock objects to release.</param>
    public void ReleaseLock(DatabaseLock[] Locks)
    {
      // We have to sort the locks before we try to enter them, otherwise we could deadlock.
      Array.Sort(Locks);

      log.Trace("(Locks:[{0}])", string.Join<DatabaseLock>(",", Locks));

      for (int i = Locks.Length - 1; i >= 0; i--)
        Locks[i].Lock.Release();

      log.Trace("(-)");
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
        if (context != null) context.Dispose();
        context = null;
        disposed = true;
      }
    }
  }
}
