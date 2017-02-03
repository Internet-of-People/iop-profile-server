using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Identity profile information.
  /// </summary>
  public class ClientProfile
  {
    /// <summary>Profile version.</summary>
    public SemVer Version;

    /// <summary>Identity public key.</summary>
    public byte[] PublicKey;

    /// <summary>Profile name.</summary>
    public string Name;

    /// <summary>Profile type.</summary>
    public string Type;

    /// <summary>Profile image.</summary>
    public byte[] ProfileImage;

    /// <summary>Thumbnail image.</summary>
    public byte[] ThumbnailImage;

    /// <summary>Initial profile location.</summary>
    public GpsLocation Location;

    /// <summary>Extra data information.</summary>
    public string ExtraData;
  }
}
