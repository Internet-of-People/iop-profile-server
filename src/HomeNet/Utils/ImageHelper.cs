using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Utils
{
  /// <summary>
  /// Image handling routines.
  /// </summary>
  public static class ImageHelper
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Utils.ImageHelper");

    /// <summary>
    /// Constructs a file name inside the image data folder from image GUID.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>File name with path for the image. The path can be relative or absolute depending on Configuration.ImageDataFolder settings.</returns>
    public static string GetImageFileName(Guid ImageGuid)
    {
      log.Trace("(ImageGuid:'{0}')", ImageGuid);

      string fileName = ImageGuid.ToString();
      string imageFolder = Kernel.Base.Configuration.ImageDataFolder;
      byte[] guidBytes = ImageGuid.ToByteArray();
      string firstLevel = string.Format("{0:X2}", guidBytes[0]);
      string secondLevel = string.Format("{0:X2}", guidBytes[1]);

      string res = string.Format("{0}{1}{2:X2}{1}{3:X2}{1}{4}", imageFolder, Path.DirectorySeparatorChar, guidBytes[0], guidBytes[1], ImageGuid.ToString());

      log.Trace("(-):'{0}'", res != null ? res : "null");
      return res;
    }


    /// <summary>
    /// Loads image data from a file.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>Binary image data if the function succeeds, null in case of error.</returns>
    public static byte[] GetImageData(Guid ImageGuid)
    {
      log.Trace("(ImageGuid:'{0}')", ImageGuid);

      byte[] res = null;
      string fileName = GetImageFileName(ImageGuid);
      if (fileName != null)
      {
        try
        {
          res = File.ReadAllBytes(fileName);
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
    public static bool SaveImageData(Guid ImageGuid, byte[] Data)
    {
      log.Trace("(ImageGuid:'{0}',Data.Length:{1})", ImageGuid, Data != null ? Data.Length.ToString() : "n/a");

      bool res = false;
      if ((Data != null) && (Data.Length > 0))
      {
        try
        {
          string fileName = GetImageFileName(ImageGuid);
          string path = Path.GetDirectoryName(fileName);
          Directory.CreateDirectory(path);
          File.WriteAllBytes(fileName, Data);
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
    /// Deletes an image file.
    /// </summary>
    /// <param name="ImageGuid">Image GUID.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool DeleteImageFile(Guid ImageGuid)
    {
      log.Trace("(ImageGuid:'{0}')", ImageGuid);

      bool res = false;
      try
      {
        string fileName = GetImageFileName(ImageGuid);
        if (!string.IsNullOrEmpty(fileName))
        {
          File.Delete(fileName);
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
  }
}
