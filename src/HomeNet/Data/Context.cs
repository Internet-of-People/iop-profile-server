using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using HomeNet.Data.Models;
using HomeNet.Kernel;

namespace HomeNet.Data
{
  /// <summary>
  /// Database context that is used everytime a component reads from or writes to the database.
  /// </summary>
  public class Context : DbContext
  {
    /// <summary>Name of the database file.</summary>
    public const string DatabaseFileName = "HomeNet.db";

    /// <summary>Access to node's settings in the database.</summary>
    public DbSet<Setting> Settings { get; set; }

    /// <summary>Access to IoP identities in the database.</summary>
    public DbSet<Identity> Identities { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
      optionsBuilder.UseSqlite(string.Format("Filename={0}", DatabaseFileName));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<Identity>()
          .HasIndex(i => new { i.IdentityId, i.HomeNodeId, i.Name, i.Type, i.ExtraData });
    }
  }
}
