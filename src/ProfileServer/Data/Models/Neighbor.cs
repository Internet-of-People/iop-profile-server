using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IopCommon;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Database representation of a profile server neighbor. A neighbor is another profile server within the profile server's neighborhood,
  /// which was announced to the profile server by its LOC server. There are two directions of a neighborhood relationship,
  /// this one represents only the servers that are neighbors to this profile server, but not necessarily vice versa. The profile server 
  /// asks its neighbors to share their profile databases with it. This allows the profile server to include profiles hosted 
  /// on the neighbors in its own search queries.
  /// <para>
  /// The opposite direction relation is represented by <see cref="Follower"/> class.
  /// </para>
  /// </summary>
  public class Neighbor: RemoteServerBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Models.Neighbor");

    /// <summary>Minimal time that a profile server has to keep the information about its neighbor's profiles without refresh.</summary>
    public const int MinNeighborhoodExpirationTimeSeconds = 86400;

    /// <summary>Minimal allowed value for the limit of the size of the profile server's neighborhood.</summary>
    public const int MinMaxNeighborhoodSize = 105;

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

    /// <summary>Number of shared profiles that the profile server received from this neighbor.</summary>
    public int SharedProfiles { get; set; }
  }
}
