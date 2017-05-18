using Iop.Profileserver;
using IopCommon;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Database representation of IoP Identity profile. This is base class for HomeIdentity and NeighborIdentity classes
  /// and must not be used on its own.
  /// </summary>
  public abstract class IdentityBase 
  {
    private static Logger log = new Logger("ProfileServer.Data.Models.IdentityBase");

    /// <summary>Maximum number of identities that a profile server can host.</summary>
    public const int MaxHostedIdentities = 20000;

    /// <summary>Maximum number of bytes that identity name can occupy.</summary>
    public const int MaxProfileNameLengthBytes = 64;

    /// <summary>Maximum number of bytes that identity type can occupy.</summary>
    public const int MaxProfileTypeLengthBytes = 64;

    /// <summary>Maximum number of bytes that thumbnail image can occupy.</summary>
    public const int MaxThumbnailImageLengthBytes = 5 * 1024;

    /// <summary>Maximum number of bytes that profile extra data can occupy.</summary>
    public const int MaxProfileExtraDataLengthBytes = 200;

    /// <summary>Special type of profile that is used internally and should not be displayed to users.</summary>
    public const string InternalInvalidProfileType = "<INTERNAL>";


    /// <summary>Unique primary key for the database.</summary>
    /// <remarks>This is primary key - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public int DbId { get; set; }

    /// <summary>Identity identifier is SHA256 hash of identity's public key.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] IdentityId { get; set; }

    /// <summary>
    /// Identifier of the server that hosts the identity profile, or empty array if the identity is hosted by this profile server.
    /// <para>For NeighborIdentity this is identifer of the profile server where the profile is registered.</para>
    /// <para>For HostedIdentity this is usually null unless the hosting agreement is cancelled and the client provided the information about its new profile server,
    /// in which case this holds the redirection information for some time for the purpose of providing the information to other clients.</para>
    /// </summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] HostingServerId { get; set; }

    /// <summary>Cryptographic public key that represents the identity.</summary>
    [Required]
    [MaxLength(ProtocolHelper.MaxPublicKeyLengthBytes)]
    public byte[] PublicKey { get; set; }

#warning TODO: create separated boolean value for profile initialization
    /// <summary>
    /// Profile version according to http://semver.org/. First byte is MAJOR, second byte is MINOR, third byte is PATCH.
    /// </summary>
    /// <remarks>Value of 0,0,0 is reserved for uninitialized profile.</remarks>
    [Required]
    [MaxLength(3)]
    public byte[] Version { get; set; }

    /// <summary>User defined profile name.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(MaxProfileNameLengthBytes)]
    public string Name { get; set; }

    /// <summary>Profile type.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(MaxProfileTypeLengthBytes)]
    public string Type { get; set; }


    /// <summary>User's initial GPS location latitude.</summary>
    /// <remarks>For precision definition see ProfileServer.Data.Context.OnModelCreating.</remarks>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-90, 90)]
    public decimal InitialLocationLatitude { get; set; }

    /// <summary>User's initial GPS location longitude.</summary>
    /// <remarks>For precision definition see ProfileServer.Data.Context.OnModelCreating.</remarks>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [Range(-180, 180)]
    public decimal InitialLocationLongitude { get; set; }

    /// <summary>User defined extra data that serve for satisfying search queries in profile server network.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(MaxProfileExtraDataLengthBytes)]
    public string ExtraData { get; set; }

#warning TODO: create separated boolean value for valid contract, use expiration date only for removal from database
    /// <summary>
    /// Expiration date after which this whole record can be deleted. 
    /// It is used only in HostedIdentity class.
    /// 
    /// <para>
    /// This is used when the profile server clients change their profile server and the server holds 
    /// the redirection information to their new hosting server. The redirect is maintained only until 
    /// the expiration date.
    /// </para>
    /// 
    /// <para>
    /// If ExpirationDate is null, the identity's contract is valid. If it is not null, it has been cancelled.
    /// </para>
    /// 
    /// </summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>
    /// Cryptographic signature of the profile information when represented with a ProfileInformation structure.
    /// <para>This can be null only before the profile initialization.</para>
    /// </summary>
    [MaxLength(ProtocolHelper.MaxSignatureLengthBytes)]
    public byte[] Signature { get; set; }


    /// <summary>
    /// SHA256 hash of profile image data, which is stored on disk, or null if the identity has no profile image.
    /// <para>In case of NeighborIdentity, the local profile server does not store the profile image data.</para>
    /// </summary>
    [MaxLength(ProtocolHelper.HashLengthBytes)]
    public byte[] ProfileImage { get; set; }

    /// <summary>SHA256 hash of thumbnail image data, which is stored on disk, or null if the identity has no thumbnail image.</summary>
    [MaxLength(ProtocolHelper.HashLengthBytes)]
    public byte[] ThumbnailImage { get; set; }



   
    /// <summary>Thumbnail image binary data that are not stored into database.</summary>
    private byte[] thumbnailImageData { get; set; }




    /// <summary>
    /// Loads thumbnail image data to thumbnailImageData field.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> LoadThumbnailImageDataAsync()
    {
      if (ThumbnailImage == null)
        return false;

      thumbnailImageData = await ImageManager.GetImageDataAsync(ThumbnailImage);
      return thumbnailImageData != null;
    }





    /// <summary>
    /// Returns thumbnail image data if it exists. If it is not loaded, the data is loaded from disk.
    /// </summary>
    /// <returns>Thumbnail image data if it exists, or null if the identity has no thumbnail image set.</returns>
    public async Task<byte[]> GetThumbnailImageDataAsync()
    {
      // If no thumbnail image is set for the identity, return null.
      if (ThumbnailImage == null)
        return null;

      // If the image data is loaded, return it.
      if (thumbnailImageData != null)
        return thumbnailImageData;

      // Otherwise load the image data and return it.
      await LoadThumbnailImageDataAsync();
      return thumbnailImageData;
    }


    /// <summary>
    /// Sets and saves thumbnail image data to a file provided.
    /// </summary>
    /// <param name="Data">Binary image data to set and save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveThumbnailImageDataAsync(byte[] Data)
    {
      if (ThumbnailImage == null)
        return false;

      thumbnailImageData = Data;
      return await ImageManager.SaveImageDataAsync(ThumbnailImage, thumbnailImageData);
    }


    /// <summary>
    /// Checks whether the profile was fully initialized.
    /// </summary>
    /// <returns>true if the identity's profile was initialized properly, false otherwise.</returns>
    public bool IsProfileInitialized()
    {
      SemVer ver = new SemVer(Version);
      return ver.IsValid();
    }


    /// <summary>
    /// Creates GPS location from identity's latitude and longitude.
    /// </summary>
    /// <returns>GPS location information.</returns>
    public GpsLocation GetInitialLocation()
    {
      return new GpsLocation(InitialLocationLatitude, InitialLocationLongitude);
    }

    /// <summary>
    /// Sets identity's GPS location information.
    /// </summary>
    /// <param name="Location">Location information.</param>
    public void SetInitialLocation(GpsLocation Location)
    {
      InitialLocationLatitude = Location.Latitude;
      InitialLocationLongitude = Location.Longitude;
    }


    /// <summary>
    /// Creates SignedProfileInformation representation of the identity's profile.
    /// </summary>
    /// <returns>SignedProfileInformation structure describing the profile.</returns>
    public SignedProfileInformation ToSignedProfileInformation()
    {
      GpsLocation location = this.GetInitialLocation();
      SignedProfileInformation res = new SignedProfileInformation()
      {
        Profile = new ProfileInformation()
        {
          Version = new SemVer(this.Version).ToByteString(),
          PublicKey = ProtocolHelper.ByteArrayToByteString(this.PublicKey),
          Type = this.Type,
          Name = this.Name,
          ExtraData = this.ExtraData,
          Latitude = location.GetLocationTypeLatitude(),
          Longitude = location.GetLocationTypeLongitude(),
          ProfileImageHash = ProtocolHelper.ByteArrayToByteString(this.ProfileImage != null ? this.ProfileImage : new byte[0]),
          ThumbnailImageHash = ProtocolHelper.ByteArrayToByteString(this.ThumbnailImage != null ? this.ThumbnailImage: new byte[0])
        },
        Signature = ProtocolHelper.ByteArrayToByteString(this.Signature != null ? this.Signature : new byte[0])
      };
      return res;
    }

    /// <summary>
    /// Creates a new instance of identity from SignedProfileInformation data structure.
    /// </summary>
    /// <param name="SignedProfile">Signed information about the profile.</param>
    /// <param name="HostingServerId">In case of NeighborhIdentity, this is set to network identifier of the hosting server.</param>
    /// <returns>New identity instance.</returns>
    public static T FromSignedProfileInformation<T>(SignedProfileInformation SignedProfile, byte[] HostingServerId) where T: IdentityBase, new()
    {
      T res = new T();
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
      this.ExpirationDate = null;
    }
  }
}
