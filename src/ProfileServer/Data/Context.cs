using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProfileServer.Data.Models;
using ProfileServer.Kernel;
using ProfileServer.Utils;

namespace ProfileServer.Data
{
  /// <summary>
  /// Database context that is used everytime a component reads from or writes to the database.
  /// </summary>
  public class Context : DbContext
  {
    /// <summary>Name of the database file.</summary>
    public const string DatabaseFileName = "ProfileServer.db";

    /// <summary>Access to profile server's settings in the database.</summary>
    public DbSet<Setting> Settings { get; set; }

    /// <summary>Access to IoP locally hosted identities in the database.</summary>
    public DbSet<HostedIdentity> Identities { get; set; }

    /// <summary>Access to IoP identities, which are not hosted on this server, but are hosted in this profile server's neighborhood.</summary>
    public DbSet<NeighborIdentity> NeighborIdentities { get; set; }

    /// <summary>Related identities announced by hosted identities.</summary>
    public DbSet<RelatedIdentity> RelatedIdentities { get; set; }

    
    /// <summary>Neighbor profile servers.</summary>
    public DbSet<Neighbor> Neighbors { get; set; }
    
    /// <summary>Planned actions related to the neighborhood.</summary>
    public DbSet<NeighborhoodAction> NeighborhoodActions { get; set; }


    /// <summary>Follower servers.</summary>
    public DbSet<Follower> Followers { get; set; }


    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
      string currentDirectory = Directory.GetCurrentDirectory();
      optionsBuilder.UseSqlite(string.Format("Filename={0}", Path.Combine(currentDirectory, DatabaseFileName)));

      Microsoft.Extensions.Logging.LoggerFactory loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
      loggerFactory.AddProvider(new DbLoggerProvider());
      optionsBuilder.UseLoggerFactory(loggerFactory);
      optionsBuilder.EnableSensitiveDataLogging();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      base.OnModelCreating(modelBuilder);

      modelBuilder.Entity<HostedIdentity>().HasKey(i => new { i.DbId });
      modelBuilder.Entity<HostedIdentity>().HasIndex(i => new { i.IdentityId }).IsUnique();
      modelBuilder.Entity<HostedIdentity>().HasIndex(i => new { i.Name });
      modelBuilder.Entity<HostedIdentity>().HasIndex(i => new { i.Type });
      modelBuilder.Entity<HostedIdentity>().HasIndex(i => new { i.InitialLocationLatitude, i.InitialLocationLongitude });
      modelBuilder.Entity<HostedIdentity>().HasIndex(i => new { i.ExtraData });
      modelBuilder.Entity<HostedIdentity>().HasIndex(i => new { i.ExpirationDate });
      modelBuilder.Entity<HostedIdentity>().HasIndex(i => new { i.ExpirationDate, i.InitialLocationLatitude, i.InitialLocationLongitude, i.Type, i.Name });

      modelBuilder.Entity<HostedIdentity>().Property(i => i.InitialLocationLatitude).HasColumnType("decimal(9,6)").IsRequired(true);
      modelBuilder.Entity<HostedIdentity>().Property(i => i.InitialLocationLongitude).HasColumnType("decimal(9,6)").IsRequired(true);

      // In case of neighbors, it is possible that a single identity is hosted on multiple profile servers.
      // Therefore IdentityId on itself does not form a unique key.
      modelBuilder.Entity<NeighborIdentity>().HasKey(i => new { i.DbId });
      modelBuilder.Entity<NeighborIdentity>().HasIndex(i => new { i.HostingServerId, i.IdentityId }).IsUnique();
      modelBuilder.Entity<NeighborIdentity>().HasIndex(i => new { i.HostingServerId });
      modelBuilder.Entity<NeighborIdentity>().HasIndex(i => new { i.Name });
      modelBuilder.Entity<NeighborIdentity>().HasIndex(i => new { i.Type });
      modelBuilder.Entity<NeighborIdentity>().HasIndex(i => new { i.InitialLocationLatitude, i.InitialLocationLongitude });
      modelBuilder.Entity<NeighborIdentity>().HasIndex(i => new { i.ExtraData });
      modelBuilder.Entity<NeighborIdentity>().HasIndex(i => new { i.InitialLocationLatitude, i.InitialLocationLongitude, i.Type, i.Name });

      modelBuilder.Entity<NeighborIdentity>().Property(i => i.InitialLocationLatitude).HasColumnType("decimal(9,6)").IsRequired(true);
      modelBuilder.Entity<NeighborIdentity>().Property(i => i.InitialLocationLongitude).HasColumnType("decimal(9,6)").IsRequired(true);
      modelBuilder.Entity<NeighborIdentity>().Property(e => e.HostingServerId).IsRequired(true);


      modelBuilder.Entity<RelatedIdentity>().HasKey(i => new { i.DbId });
      modelBuilder.Entity<RelatedIdentity>().HasIndex(i => new { i.IdentityId, i.ApplicationId }).IsUnique();
      modelBuilder.Entity<RelatedIdentity>().HasIndex(i => new { i.Type });
      modelBuilder.Entity<RelatedIdentity>().HasIndex(i => new { i.ValidFrom, i.ValidTo });
      modelBuilder.Entity<RelatedIdentity>().HasIndex(i => new { i.RelatedToIdentityId });
      modelBuilder.Entity<RelatedIdentity>().HasIndex(i => new { i.IdentityId, i.Type, i.RelatedToIdentityId, i.ValidFrom, i.ValidTo });


      modelBuilder.Entity<Neighbor>().HasKey(i => i.DbId);
      modelBuilder.Entity<Neighbor>().HasIndex(i => new { i.NeighborId }).IsUnique();
      modelBuilder.Entity<Neighbor>().HasIndex(i => new { i.IpAddress, i.PrimaryPort });
      modelBuilder.Entity<Neighbor>().HasIndex(i => new { i.LastRefreshTime });


      modelBuilder.Entity<Neighbor>().Property(i => i.LocationLatitude).HasColumnType("decimal(9,6)").IsRequired(true);
      modelBuilder.Entity<Neighbor>().Property(i => i.LocationLongitude).HasColumnType("decimal(9,6)").IsRequired(true);


      modelBuilder.Entity<Follower>().HasKey(i => i.DbId);
      modelBuilder.Entity<Follower>().HasIndex(i => new { i.FollowerId }).IsUnique();
      modelBuilder.Entity<Follower>().HasIndex(i => new { i.IpAddress, i.PrimaryPort });
      modelBuilder.Entity<Follower>().HasIndex(i => new { i.LastRefreshTime });


      modelBuilder.Entity<NeighborhoodAction>().HasKey(i => i.Id);
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.Id }).IsUnique();
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.ServerId });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.Timestamp });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.ExecuteAfter });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.Type });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.TargetIdentityId });
      modelBuilder.Entity<NeighborhoodAction>().HasIndex(i => new { i.ServerId, i.Type, i.TargetIdentityId });
      modelBuilder.Entity<NeighborhoodAction>().Property(e => e.TargetIdentityId).IsRequired(false);
    }
  }
}
