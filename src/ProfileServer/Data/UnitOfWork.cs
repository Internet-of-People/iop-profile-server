using ProfileServer.Data.Models;
using ProfileServer.Data.Repositories;
using System;
using IopCommon;
using IopServerCore.Data;

namespace ProfileServer.Data
{
  /// <summary>
  /// Coordinates the work of multiple repositories by creating a single database context class shared by all of them.
  /// </summary>
  public class UnitOfWork : UnitOfWorkBase<Context>, IDisposable
  {
    /// <summary>Lock for SettingsRepository.</summary>
    public static DatabaseLock SettingsLock = new DatabaseLock("SETTINGS");

    /// <summary>Lock for HostedIdentityRepository.</summary>
    public static DatabaseLock HostedIdentityLock = new DatabaseLock("HOSTED_IDENTITY");

    /// <summary>Lock for NeighborIdentityRepository.</summary>
    public static DatabaseLock NeighborIdentityLock = new DatabaseLock("NEIGHBORHOOD_IDENTITY");

    /// <summary>Lock for RelatedIdentityRepository.</summary>
    public static DatabaseLock RelatedIdentityLock = new DatabaseLock("RELATED_IDENTITY");

    /// <summary>Lock for NeighborRepository.</summary>
    public static DatabaseLock NeighborLock = new DatabaseLock("NEIGHBOR");

    /// <summary>Lock for NeighborhoodActionRepository.</summary>
    public static DatabaseLock NeighborhoodActionLock = new DatabaseLock("NEIGHBORHOOD_ACTION");

    /// <summary>Lock for FollowerRepository.</summary>
    public static DatabaseLock FollowerLock = new DatabaseLock("FOLLOWER");


    /// <summary>Settings repository.</summary>
    private SettingsRepository settingsRepository;
    /// <summary>Settings repository.</summary>
    public SettingsRepository SettingsRepository
    {
      get
      {
        if (settingsRepository == null)
          settingsRepository = new SettingsRepository(Context, this);

        return settingsRepository;
      }
    }


    /// <summary>Identity repository for the profile server customers.</summary>
    private HostedIdentityRepository hostedIdentityRepository;
    /// <summary>Identity repository for the profile server customers.</summary>
    public HostedIdentityRepository HostedIdentityRepository
    {
      get
      {
        if (hostedIdentityRepository == null)
          hostedIdentityRepository = new HostedIdentityRepository(Context, this);

        return hostedIdentityRepository;
      }
    }

    /// <summary>Identity repository for identities hosted in the profile server's neighborhood.</summary>
    private NeighborIdentityRepository neighborIdentityRepository;
    /// <summary>Identity repository for identities hosted in the profile server's neighborhood.</summary>
    public NeighborIdentityRepository NeighborIdentityRepository
    {
      get
      {
        if (neighborIdentityRepository == null)
          neighborIdentityRepository = new NeighborIdentityRepository(Context, this);

        return neighborIdentityRepository;
      }
    }

    /// <summary>Repository of relations of hosted identities.</summary>
    private RelatedIdentityRepository relatedIdentityRepository;
    /// <summary>Repository of relations of hosted identities.</summary>
    public RelatedIdentityRepository RelatedIdentityRepository
    {
      get
      {
        if (relatedIdentityRepository == null)
          relatedIdentityRepository = new RelatedIdentityRepository(Context, this);

        return relatedIdentityRepository;
      }
    }


    /// <summary>Repository of profile server neighbors.</summary>
    private NeighborRepository neighborRepository;
    /// <summary>Repository of profile server neighbors.</summary>
    public NeighborRepository NeighborRepository
    {
      get
      {
        if (neighborRepository == null)
          neighborRepository = new NeighborRepository(Context, this);

        return neighborRepository;
      }
    }

    /// <summary>Repository of planned actions in the neighborhood.</summary>
    private NeighborhoodActionRepository neighborhoodActionRepository;
    /// <summary>Repository of planned actions in the neighborhood.</summary>
    public NeighborhoodActionRepository NeighborhoodActionRepository
    {
      get
      {
        if (neighborhoodActionRepository == null)
          neighborhoodActionRepository = new NeighborhoodActionRepository(Context, this);

        return neighborhoodActionRepository;
      }
    }


    /// <summary>Repository of profile server followers.</summary>
    private FollowerRepository followerRepository;
    /// <summary>Repository of profile server followers.</summary>
    public FollowerRepository FollowerRepository
    {
      get
      {
        if (followerRepository == null)
          followerRepository = new FollowerRepository(Context, this);

        return followerRepository;
      }
    }
  }
}
