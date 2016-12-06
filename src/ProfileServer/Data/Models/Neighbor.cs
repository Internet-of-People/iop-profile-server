using ProfileServer.Utils;
using ProfileServerProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Database representation of profile server neighbor. A neighbor is another profile server within the profile server's neighborhood,
  /// which was announced to the profile server from its LBN server. There are two directions of neighborhood relationship,
  /// this represents only the servers that are this profile server's neighbors, but not necessarily vice versa. The profile server 
  /// asks its neighbors to share their profile databases with it. This allows to the profile server to include profiles hosted 
  /// on the neighbors in its own search queries.
  /// <para>
  /// The opposite direction relation is represented by <see cref="Follower"/> class.
  /// </para>
  /// </summary>
  public class Neighbor
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.Models.Neighbor");

    /// <summary>Minimal time that a profile server has to keep the information about its neighbor's profiles without refresh.</summary>
    public const int MinNeighborhoodExpirationTimeSeconds = 86400;

    /// <summary>Minimal allowed value for the limit of the size of the profile server's neighborhood.</summary>
    public const int MinMaxNeighborhoodSize = 105;

    /// <summary>Network identifier of the profile server is SHA256 hash of identity's public key.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(IdentityBase.IdentifierLength)]
    public byte[] Id { get; set; }

    /// <summary>IP address of the profile server.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public IPAddress IpAddress { get; set; }

    /// <summary>TCP port of the profile server's primary interface.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(1, 65535)]
    public int PrimaryPort { get; set; }

    /// <summary>TCP port of the profile server's neighbors interface.</summary>
    [Range(1, 65535)]
    public int? SrNeighborPort { get; set; }

    /// <summary>Profile server's GPS location latitude.</summary>
    /// <remarks>For precision definition see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-90, 90)]
    public decimal LocationLatitude { get; set; }

    /// <summary>Profile server's GPS location longitude.</summary>
    /// <remarks>For precision definition see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-180, 180)]
    public decimal LocationLongitude { get; set; }

    /// <summary>Time of the last refresh message received from the neighbor.</summary>
    public DateTime? LastRefreshTime { get; set; }
#warning TODO: Implement clean up of expired identities based on LastRefreshTime.
  }
}
