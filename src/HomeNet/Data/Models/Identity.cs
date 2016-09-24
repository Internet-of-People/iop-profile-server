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
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Data.Models.Identity");

    /// <summary>Maximum number of bytes that identity name can occupy.</summary>
    public const int MaxProfileNameLengthBytes = 64;

    /// <summary>Maximum number of bytes that profile image can occupy.</summary>
    public const int MaxProfileImageLengthBytes = 20 * 1024;

    /// <summary>Maximum number of bytes that thumbnail image can occupy.</summary>
    public const int MaxThumbnailImageLengthBytes = 5 * 1024;

    /// <summary>Maximum number of bytes that profile extra data can occupy.</summary>
    public const int MaxProfileExtraDataLengthBytes = 200;


    /// <summary>Identity identifier is SHA1 hash of identity's public key.</summary>
    [MaxLength(20)]
    public byte[] IdentityId { get; set; }

    /// <summary>Identifier of the home node or empty array if the identity is hosted by this node.</summary>
    [MaxLength(20)]
    public byte[] HomeNodeId { get; set; }

    /// <summary>Cryptographic public key that represents the identity.</summary>
    [Required]
    [MaxLength(256)]
    public byte[] PublicKey { get; set; }

    /// <summary>
    /// Profile version according to http://semver.org/. First byte is MAJOR, second byte is MINOR, third byte is PATCH.
    /// </summary>
    /// <remarks>Value of 0,0,0 is reserved for uninitialized profile.</remarks>
    [Required]
    [MaxLength(3)]
    public byte[] Version { get; set; }

    /// <summary>User defined profile name.</summary>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(MaxProfileNameLengthBytes)]
    public string Name { get; set; }

    /// <summary>Profile type.</summary>
    [Required]
    [MaxLength(32)]
    public string Type { get; set; }

    /// <summary>Guid of user defined profile picture, which data is stored on disk.</summary>
    public Guid? ProfileImage { get; set; }

    /// <summary>Guid of thumbnail profile picture, which data is stored on disk.</summary>
    public Guid? ThumbnailImage { get; set; }

    /// <summary>Encoded representation of the user's initial GPS location.</summary>
    public uint InitialLocationEncoded { get; set; }

    /// <summary>User defined extra data that serve for satisfying search queries in HomeNet.</summary>
    [MaxLength(200)]
    public string ExtraData { get; set; }

    /// <summary>Profile image binary data that are not stored into database.</summary>
    private byte[] profileImageData { get; set; }
    
    /// <summary>Thumbnail image binary data that are not stored into database.</summary>
    private byte[] thumbnailImageData { get; set; }



    /// <summary>
    /// Loads profile image data to profileImageData field.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool LoadProfileImageData()
    {
      if (ProfileImage == null)
        return false;

      profileImageData = Utils.ImageHelper.GetImageData(ProfileImage.Value);
      return profileImageData != null;
    }

    /// <summary>
    /// Loads thumbnail image data to thumbnailImageData field.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool LoadThumbnailImageData()
    {
      if (ThumbnailImage == null)
        return false;

      thumbnailImageData = Utils.ImageHelper.GetImageData(ThumbnailImage.Value);
      return thumbnailImageData != null;
    }

    /// <summary>
    /// Saves profile image data to a file provided that profileImageData field is initialized.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool SaveProfileImageData()
    {
      if (ProfileImage == null)
        return false;

      return Utils.ImageHelper.SaveImageData(ProfileImage.Value, profileImageData);
    }

    /// <summary>
    /// Sets and saves profile image data to a file provided.
    /// </summary>
    /// <param name="Data">Binary image data to set and save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool SaveProfileImageData(byte[] Data)
    {
      if (ProfileImage == null)
        return false;

      profileImageData = Data;
      return Utils.ImageHelper.SaveImageData(ProfileImage.Value, profileImageData);
    }


    /// <summary>
    /// Saves thumbnail image data to a file provided that thumbnailImageData field is initialized.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool SaveThumbnailImageData()
    {
      if (ThumbnailImage == null)
        return false;

      return Utils.ImageHelper.SaveImageData(ThumbnailImage.Value, thumbnailImageData);
    }

    /// <summary>
    /// Sets and saves thumbnail image data to a file provided.
    /// </summary>
    /// <param name="Data">Binary image data to set and save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool SaveThumbnailImageData(byte[] Data)
    {
      if (ThumbnailImage == null)
        return false;

      thumbnailImageData = Data;
      return Utils.ImageHelper.SaveImageData(ThumbnailImage.Value, thumbnailImageData);
    }
  }
}
