using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Data.Models
{
  /// <summary>
  /// Database representation of IoP Identity profile.
  /// </summary>
  public class Identity
  {
    /// <summary>Identity identifier is SHA1 hash of identity's public key.</summary>
    [MaxLength(20)]
    public byte[] IdentityId { get; set; }

    /// <summary>Cryptographic public key that represents the identity.</summary>
    [Required]
    [MaxLength(256)]
    public byte[] PublicKey { get; set; }

    /// <summary>Profile version in http://semver.org/ format.</summary>
    [MaxLength(3)]
    [Required]
    public byte[] Version { get; set; }

    /// <summary>User defined profile name.</summary>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(64)]
    public string Name { get; set; }

    /// <summary>Profile type.</summary>
    [Required]
    [MaxLength(32)]
    public string Type { get; set; }

    /// <summary>User defined profile picture.</summary>
    [MaxLength(20 * 1024)]
    public byte[] Picture { get; set; }

    /// <summary>Encoded representation of the user's initial GPS location.</summary>
    public uint InitialLocationEncoded { get; set; }

    /// <summary>User defined extra data that serve for satisfying search queries in HomeNet.</summary>
    [MaxLength(200)]
    public string ExtraData { get; set; }
  }
}
