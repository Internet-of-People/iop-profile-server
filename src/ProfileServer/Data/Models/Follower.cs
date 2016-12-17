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
  /// Database representation of so called Follower server, which is a remote profile server, for which the profile server acts as a neighbor. 
  /// Follower is a server that asked the profile server to share its profile database with it and the profile server is sending 
  /// updates of the database to the follower.
  /// <para>
  /// The opposite direction relation is represented by <see cref="Neighbor"/> class.
  /// </para>
  /// </summary>
  public class Follower
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.Models.Follower");

    /// <summary>Unique primary key for the database.</summary>
    /// <remarks>This is primary key - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public int DbId { get; set; }

    /// <summary>Network identifier of the profile server is SHA256 hash of identity's public key.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(IdentityBase.IdentifierLength)]
    public byte[] FollowerId { get; set; }

    /// <summary>IP address of the profile server.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public string IpAddress { get; set; }

    /// <summary>TCP port of the profile server's primary interface.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(1, 65535)]
    public int PrimaryPort { get; set; }

    /// <summary>TCP port of the profile server's neighbors interface.</summary>
    [Range(1, 65535)]
    public int? SrNeighborPort { get; set; }

    /// <summary>
    /// Time of the last refresh message sent to the follower server.
    /// <para>
    /// A null value means that the follower server is in the middle of the initialization process 
    /// and the full synchronization has not been done yet. Once the initialization process is completed 
    /// this field is initialized.
    /// </para>
    /// </summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    public DateTime? LastRefreshTime { get; set; }
  }
}
