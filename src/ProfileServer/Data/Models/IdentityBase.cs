using ProfileServer.Utils;
using ProfileServerProtocol;
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
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.Models.IdentityBase");

    /// <summary>Maximum number of identities that a profile server can host.</summary>
    public const int MaxHostedIdentities = 20000;

    /// <summary>Maximum number of bytes that identity name can occupy.</summary>
    public const int MaxProfileNameLengthBytes = 64;

    /// <summary>Maximum number of bytes that identity type can occupy.</summary>
    public const int MaxProfileTypeLengthBytes = 64;

    /// <summary>Maximum number of bytes that profile image can occupy.</summary>
    public const int MaxProfileImageLengthBytes = 20 * 1024;

    /// <summary>Maximum number of bytes that thumbnail image can occupy.</summary>
    public const int MaxThumbnailImageLengthBytes = 5 * 1024;

    /// <summary>Maximum number of bytes that profile extra data can occupy.</summary>
    public const int MaxProfileExtraDataLengthBytes = 200;

    /// <summary>Length in bytes of node/identity identifiers.</summary>
    public const int IdentifierLength = 32;

    /// <summary>Maximum number of bytes that public key can occupy.</summary>
    public const int MaxPublicKeyLengthBytes = 128;


    /// <summary>Unique primary key for the database.</summary>
    /// <remarks>This is primary key - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public int DbId { get; set; }

    /// <summary>Identity identifier is SHA256 hash of identity's public key.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(IdentifierLength)]
    public byte[] IdentityId { get; set; }

    /// <summary>Identifier of the server that hosts the identity profile, or empty array if the identity is hosted by this profile server.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [MaxLength(IdentifierLength)]
    public byte[] HostingServerId { get; set; }

    /// <summary>Cryptographic public key that represents the identity.</summary>
    [Required]
    [MaxLength(MaxPublicKeyLengthBytes)]
    public byte[] PublicKey { get; set; }

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
    [Required]
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

    /// <summary>User defined extra data that serve for satisfying search queries in ProfileServer.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(200)]
    public string ExtraData { get; set; }

    /// <summary>
    /// Expiration date after which this whole record can be deleted.
    /// 
    /// <para>
    /// In the HostedIdentityRepository, this is used when the profile server clients change their profile server
    /// and the server holds the redirection information to their new hosting server. The redirect is maintained 
    /// only until the expiration date.
    /// </para>
    /// 
    /// <para>
    /// In the HostedIdentityRepository, if ExpirationDate is null, the identity's contract is valid. 
    /// If it is not null, it has been cancelled.
    /// </para>
    /// 
    /// <para>
    /// In the NeighborIdentityRepository, ExpirationDate is not used. Instead, Neighbor.LastRefreshTime is used 
    /// to track when the identities shared by a neighbor should expire.
    /// </para>
    /// </summary>
    /// <remarks>This is index in HostedIdentityRepository - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    public DateTime? ExpirationDate { get; set; }




    // Profile images are stored on the disk and it can rarely happen that these images 
    // are not deleted with the identity record that used them. If this is a problem, 
    // we should implement a garbage collector for these files, which would first create 
    // a snapshot of all image files in the images folder and then get the list of images 
    // of all identities in the database. Image files that are not referenced from database 
    // can be deleted. This is not implemented at this moment. An alternative solution to that 
    // garbage collector approach would be to ensure deletion of the images. However, 
    // the additional complexity would probably not justify the rare frequency of occurance
    // of this problem.

    /// <summary>Guid of user defined profile picture, which data is stored on disk.</summary>
    public Guid? ProfileImage { get; set; }

    /// <summary>Guid of thumbnail profile picture, which data is stored on disk.</summary>
    public Guid? ThumbnailImage { get; set; }



    /// <summary>Profile image binary data that are not stored into database.</summary>
    private byte[] profileImageData { get; set; }
    
    /// <summary>Thumbnail image binary data that are not stored into database.</summary>
    private byte[] thumbnailImageData { get; set; }




    /// <summary>
    /// Loads profile image data to profileImageData field.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> LoadProfileImageDataAsync()
    {
      if (ProfileImage == null)
        return false;

      profileImageData = await Utils.ImageHelper.GetImageDataAsync(ProfileImage.Value);
      return profileImageData != null;
    }

    /// <summary>
    /// Loads thumbnail image data to thumbnailImageData field.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> LoadThumbnailImageDataAsync()
    {
      if (ThumbnailImage == null)
        return false;

      thumbnailImageData = await Utils.ImageHelper.GetImageDataAsync(ThumbnailImage.Value);
      return thumbnailImageData != null;
    }


    /// <summary>
    /// Returns profile image data if it exists. If it is not loaded, the data is loaded from disk.
    /// </summary>
    /// <returns>Profile image data if it exists, or null if the identity has no profile image set.</returns>
    public async Task<byte[]> GetProfileImageDataAsync()
    {
      // If no profile image is set for the identity, return null.
      if (ProfileImage == null)
        return null;

      // If the image data is loaded, return it.
      if (profileImageData != null)
        return profileImageData;

      // Otherwise load the image data and return it.
      await LoadProfileImageDataAsync();
      return profileImageData;
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
    /// Saves profile image data to a file provided that profileImageData field is initialized.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveProfileImageDataAsync()
    {
      if (ProfileImage == null)
        return false;

      return await Utils.ImageHelper.SaveImageDataAsync(ProfileImage.Value, profileImageData);
    }

    /// <summary>
    /// Sets and saves profile image data to a file provided.
    /// </summary>
    /// <param name="Data">Binary image data to set and save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveProfileImageDataAsync(byte[] Data)
    {
      if (ProfileImage == null)
        return false;

      profileImageData = Data;
      return await Utils.ImageHelper.SaveImageDataAsync(ProfileImage.Value, profileImageData);
    }


    /// <summary>
    /// Saves thumbnail image data to a file provided that thumbnailImageData field is initialized.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveThumbnailImageDataAsync()
    {
      if (ThumbnailImage == null)
        return false;

      return await Utils.ImageHelper.SaveImageDataAsync(ThumbnailImage.Value, thumbnailImageData);
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
      return await Utils.ImageHelper.SaveImageDataAsync(ThumbnailImage.Value, thumbnailImageData);
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
  }
}
