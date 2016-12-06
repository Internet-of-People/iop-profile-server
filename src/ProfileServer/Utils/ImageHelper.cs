using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Utils
{
  /// <summary>
  /// Image handling routines.
  /// </summary>
  public static class ImageHelper
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Utils.ImageHelper");

    /// <summary>
    /// Constructs a file name inside the image data folder from image GUID.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>File name with path for the image. The path can be relative or absolute depending on Configuration.ImageDataFolder settings.</returns>
    public static string GetImageFileName(Guid ImageGuid)
    {
      return GetImageFileName(ImageGuid, Kernel.Base.Configuration.ImageDataFolder);
    }


    /// <summary>
    /// Constructs a file name inside the temporary data folder from image GUID.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>File name with path for the image. The path can be relative or absolute depending on Configuration.TempDataFolder settings.</returns>
    public static string GetTempImageFileName(Guid ImageGuid)
    {
      return GetImageFileName(ImageGuid, Kernel.Base.Configuration.TempDataFolder);
    }


    /// <summary>
    /// Constructs a file name inside the image data folder from image GUID.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <param name="Folder">Root images folder in which the image directory structure is to be created and the image stored.</param>
    /// <returns>File name with path for the image.</returns>
    public static string GetImageFileName(Guid ImageGuid, string Folder)
    {
      log.Trace("(ImageGuid:'{0}',Folder:'{1}')", ImageGuid, Folder);

      string fileName = ImageGuid.ToString();
      byte[] guidBytes = ImageGuid.ToByteArray();
      string firstLevel = string.Format("{0:X2}", guidBytes[0]);
      string secondLevel = string.Format("{0:X2}", guidBytes[1]);

      string res = string.Format("{0}{1}{2:X2}{1}{3:X2}{1}{4}", Folder, Path.DirectorySeparatorChar, guidBytes[0], guidBytes[1], ImageGuid.ToString());

      log.Trace("(-):'{0}'", res != null ? res : "null");
      return res;
    }




    /// <summary>
    /// Loads image data from a file.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>Binary image data if the function succeeds, null in case of error.</returns>
    public static async Task<byte[]> GetImageDataAsync(Guid ImageGuid)
    {
      log.Trace("(ImageGuid:'{0}')", ImageGuid);

      byte[] res = null;
      string fileName = GetImageFileName(ImageGuid);
      if (fileName != null)
      {
        try
        {
          res = await FileHelper.ReadAllBytesAsync(fileName);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      log.Trace("(-):*.Lenght={0}", res != null ? res.Length.ToString() : "n/a");
      return res;
    }

    /// <summary>
    /// Saves image data to image data folder file.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <param name="Data">Binary image data to save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static async Task<bool> SaveImageDataAsync(Guid ImageGuid, byte[] Data)
    {
      string fileName = GetImageFileName(ImageGuid);
      return await SaveImageDataAsync(fileName, Data);
    }


    /// <summary>
    /// Saves image data to temporary data folder file.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <param name="Data">Binary image data to save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static async Task<bool> SaveTempImageDataAsync(Guid ImageGuid, byte[] Data)
    {
      string fileName = GetTempImageFileName(ImageGuid);
      return await SaveImageDataAsync(fileName, Data);
    }


    /// <summary>
    /// Saves image data to image data folder file.
    /// </summary>
    /// <param name="FileName">Name of the file to save the image data to.</param>
    /// <param name="Data">Binary image data to save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static async Task<bool> SaveImageDataAsync(string FileName, byte[] Data)
    {
      log.Trace("(FileName:'{0}',Data.Length:{1})", FileName, Data != null ? Data.Length.ToString() : "n/a");

      bool res = false;
      if ((Data != null) && (Data.Length > 0))
      {
        try
        {
          string path = Path.GetDirectoryName(FileName);
          Directory.CreateDirectory(path);
          await FileHelper.WriteAllBytesAsync(FileName, Data);
          res = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Deletes an image file from image folder.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool DeleteImageFile(Guid ImageGuid)
    {
      string fileName = GetImageFileName(ImageGuid);
      return DeleteImageFile(fileName);
    }

    /// <summary>
    /// Deletes an image file from temporary folder.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool DeleteTempImageFile(Guid ImageGuid)
    {
      string fileName = GetTempImageFileName(ImageGuid);
      return DeleteImageFile(fileName);
    }


    /// <summary>
    /// Deletes an image file.
    /// </summary>
    /// <param name="FileName">Image file name.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool DeleteImageFile(string FileName)
    {
      log.Trace("(FileName:'{0}')", FileName);

      bool res = false;
      try
      {
        if (!string.IsNullOrEmpty(FileName))
        {
          File.Delete(FileName);
          res = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}.", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Moves an image file from temporary folder to images folder.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool MoveImageFileFromTemp(Guid ImageGuid)
    {
      log.Trace("(ImageGuid:'{0}')", ImageGuid);

      string tempFileName = GetTempImageFileName(ImageGuid);
      string imagesFileName = GetImageFileName(ImageGuid);

      bool res = false;
      try
      {
        string path = Path.GetDirectoryName(imagesFileName);
        Directory.CreateDirectory(path);
        if (File.Exists(imagesFileName)) File.Delete(imagesFileName);
        File.Move(tempFileName, imagesFileName);
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}.", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether binary data represent a valid PNG or JPEG image.
    /// </summary>
    /// <param name="Data">Binary data to check.</param>
    /// <returns>true if the data represents a valid PNG or JPEG image, false otherwise</returns>
    public static bool ValidateImageFormat(byte[] Data)
    {
      log.Trace("(Data.Length:{0})", Data.Length);
#warning TODO: This function currently does nothing, waiting for some libraries to be released.
      // TODO: 
      // * check image is valid PNG or JPEG format
      // * waiting for https://github.com/JimBobSquarePants/ImageSharp to release
      //   or https://magick.codeplex.com/documentation to support all OS with NET Core releases
      log.Fatal("TODO UNIMPLEMENTED");

      bool res = Data.Length > 2;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Creates a thumbnail image from a profile image.
    /// </summary>
    /// <param name="ProfileImage">Binary data of the profile image data.</param>
    /// <param name="ThumbnailImage">On the output, this is filled with thumbnail image data.</param>
    public static void ProfileImageToThumbnailImage(byte[] ProfileImage, out byte[] ThumbnailImage)
    {
      log.Trace("(ProfileImage.Length:{0})", ProfileImage.Length);

#warning TODO: This function currently does nothing, waiting for some libraries to be released.
      // TODO: 
      // * check if ProfileImage is small enough to represent thumbnail image without changes
      // * if it is too big, check if it is PNG or JPEG
      // * if it is PNG, convert to JPEG
      // * resize and increase compression until small enough
      // * waiting for https://github.com/JimBobSquarePants/ImageSharp to release
      //   or https://magick.codeplex.com/documentation to support all OS with NET Core releases

      log.Fatal("TODO UNIMPLEMENTED");

      ThumbnailImage = ProfileImage;

      log.Trace("(-):{0})", ThumbnailImage.Length);
    }
  }
}
