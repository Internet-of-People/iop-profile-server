using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using IopCommon;
using Iop.Profileserver;
using IopCrypto;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Database representation of IoP Identity profile that is hosted in the profile server's neighborhood.
  /// </summary>
  public class NeighborIdentity : IdentityBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Models.NeighborIdentity");


    /// <summary>
    /// Creates a new instance of identity from SignedProfileInformation data structure.
    /// </summary>
    /// <param name="SignedProfile">Signed information about the profile.</param>
    /// <param name="HostingServerId">In case of NeighborhIdentity, this is set to network identifier of the hosting server.</param>
    /// <returns>New identity instance.</returns>
    public static NeighborIdentity FromSignedProfileInformation(SignedProfileInformation SignedProfile, byte[] HostingServerId) 
    {
      NeighborIdentity res = new NeighborIdentity();
      res.CopyFromSignedProfileInformation(SignedProfile, HostingServerId);

      return res;
    }

    /// <summary>
    /// Copies values from signed profile information to properties of this instance of the identity.
    /// </summary>
    /// <param name="SignedProfile">Signed information about the profile.</param>
    /// <param name="HostingServerId">In case of NeighborhIdentity, this is set to network identifier of the hosting server.</param>
    public void CopyFromSignedProfileInformation(SignedProfileInformation SignedProfile, byte[] HostingServerId)
    {
      if (HostingServerId == null) HostingServerId = new byte[0];

      ProfileInformation profile = SignedProfile.Profile;
      byte[] pubKey = profile.PublicKey.ToByteArray();
      byte[] identityId = Crypto.Sha256(pubKey);
      GpsLocation location = new GpsLocation(profile.Latitude, profile.Longitude);

      this.IdentityId = identityId;
      this.HostingServerId = HostingServerId;
      this.PublicKey = pubKey;
      this.Version = profile.Version.ToByteArray();
      this.Name = profile.Name;
      this.Type = profile.Type;
      this.InitialLocationLatitude = location.Latitude;
      this.InitialLocationLongitude = location.Longitude;
      this.ExtraData = profile.ExtraData;
      this.ProfileImage = profile.ProfileImageHash.Length != 0 ? profile.ProfileImageHash.ToByteArray() : null;
      this.ThumbnailImage = profile.ThumbnailImageHash.Length != 0 ? profile.ThumbnailImageHash.ToByteArray() : null;
      this.Signature = SignedProfile.Signature.ToByteArray();
    }
  }
}
