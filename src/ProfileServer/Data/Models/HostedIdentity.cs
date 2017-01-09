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
  /// Database representation of IoP Identity profile that is hosted by the profile server.
  /// </summary>
  public class HostedIdentity : IdentityBase
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.Models.HostedIdentity");

    /// <summary>Maximum number of bytes that profile image can occupy.</summary>
    public const int MaxProfileImageLengthBytes = 20 * 1024;


    /// <summary>SHA256 hash of profile image data, which is stored on disk, or null if the identity has no profile image.</summary>
    public byte[] ProfileImage { get; set; }



    /// <summary>CAN hash of the object that the client uploaded to CAN.</summary>
    public byte[] CanObjectHash { get; set; }

    /// <summary>Profile image binary data that are not stored into database.</summary>
    private byte[] profileImageData { get; set; }

    /// <summary>
    /// Loads profile image data to profileImageData field.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> LoadProfileImageDataAsync()
    {
      if (ProfileImage == null)
        return false;

      profileImageData = await ImageManager.GetImageDataAsync(ProfileImage);
      return profileImageData != null;
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
    /// Sets and saves profile image data to a file provided.
    /// </summary>
    /// <param name="Data">Binary image data to set and save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveProfileImageDataAsync(byte[] Data)
    {
      if (ProfileImage == null)
        return false;

      profileImageData = Data;
      return await ImageManager.SaveImageDataAsync(ProfileImage, profileImageData);
    }



  }
}
