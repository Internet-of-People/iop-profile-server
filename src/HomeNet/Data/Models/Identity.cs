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

    /// <summary>Length in bytes of node/identity identifiers.</summary>
    public const int IdentifierLength = 20;

    /// <summary>Identity identifier is SHA1 hash of identity's public key.</summary>
    /// <remarks>This is index - see HomeNet.Data.Context.OnModelCreating.</remarks>
    [MaxLength(IdentifierLength)]
    public byte[] IdentityId { get; set; }

    /// <summary>Identifier of the home node or empty array if the identity is hosted by this node.</summary>
    /// <remarks>This is index - see HomeNet.Data.Context.OnModelCreating.</remarks>
    [MaxLength(IdentifierLength)]
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
    /// <remarks>This is index - see HomeNet.Data.Context.OnModelCreating.</remarks>
    [Required(AllowEmptyStrings = true)]
    [MaxLength(MaxProfileNameLengthBytes)]
    public string Name { get; set; }

    /// <summary>Profile type.</summary>
    /// <remarks>This is index - see HomeNet.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(32)]
    public string Type { get; set; }


    /// <summary>Encoded representation of the user's initial GPS location.</summary>
    public uint InitialLocationEncoded { get; set; }

    /// <summary>User defined extra data that serve for satisfying search queries in HomeNet.</summary>
    /// <remarks>This is index - see HomeNet.Data.Context.OnModelCreating.</remarks>
    [MaxLength(200)]
    public string ExtraData { get; set; }

    /// <summary>
    /// Expiration date after which this whole record can be deleted.
    /// This is used in case of the node clients when they change their home node 
    /// and the node holds the redirection information. The redirect is maintained 
    /// only until the expiration date.
    /// 
    /// In the IdentityRepository, if ExpirationDate is null, the identity's contract 
    /// is valid. If it is not null, it has been cancelled.
    /// </summary>
    /// <remarks>This is index - see HomeNet.Data.Context.OnModelCreating.</remarks>
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
  }
}
