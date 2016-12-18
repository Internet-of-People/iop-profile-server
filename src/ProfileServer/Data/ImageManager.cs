using ProfileServer.Data.Models;
using ProfileServer.Kernel;
using ProfileServer.Utils;
using ProfileServerCrypto;
using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Data
{
  /// <summary>
  /// Image manager is responsible for tracking the use of profile images. It also implements helper routines for image manipulations.
  /// <para>
  /// Profile images are stored on disk and the database entries only contain SHA256 hashes of the data.
  /// If two profiles use the same image, their hash is the same and only one instance of the binary data 
  /// is actually stored on the disk.
  /// </para>
  /// <para>
  /// Image manager tracks the reference counters for each image and is thus able to recognize 
  /// when an image file is no longer needed and can be deleted.
  /// </para>
  /// </summary>
  public class ImageManager: Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.ImageManger");

    /// <summary>Lock object to protect access to referenceCounter.</summary>
    private object referenceCounterLock = new object();

    /// <summary>Mapping of SHA256 image hashes to reference counters.</summary>
    private Dictionary<byte[], int> referenceCounter = new Dictionary<byte[], int>(StructuralEqualityComparer<byte[]>.Default);

    /// <summary>Lock object to protect access to imageDeleteList.</summary>
    private object imageDeleteListLock = new object();

    /// <summary>List of image hashes that will possibly be deleted.</summary>
    private List<byte[]> imageDeleteList = new List<byte[]>();

    /// <summary>Instance of the image manager component.</summary>
    private static ImageManager imageManager;


    public override bool Init()
    {
      log.Info("()");

      bool res = false;
      try
      {
        imageManager = this;

        if (InitializeReferenceCounter()
          && DeleteUnusedImages())
        {
          res = true;
          Initialized = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      log.Info("(-)");
    }


    /// <summary>
    /// Scans the database to know which images are actually in use.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private bool InitializeReferenceCounter()
    {
      log.Info("()");

      bool error = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        log.Trace("Scanning hosted identity database.");
        DatabaseLock lockObject = UnitOfWork.HostedIdentityLock;
        unitOfWork.AcquireLock(lockObject);
        try
        {
          byte[] invalidVersion = SemVer.Invalid.ToByteArray();
          List<HostedIdentity> identities = unitOfWork.HostedIdentityRepository.Get(i => (i.ExpirationDate == null) && (i.Version != invalidVersion), null, true).ToList();
          foreach (HostedIdentity identity in identities)
          {
            if (identity.ProfileImage != null) AddImageReference(identity.ProfileImage);
            if (identity.ThumbnailImage != null) AddImageReference(identity.ThumbnailImage);
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
          error = true;
        }
        unitOfWork.ReleaseLock(lockObject);

        if (!error)
        {
          log.Trace("Scanning neighbor identity database.");
          lockObject = UnitOfWork.NeighborIdentityLock;
          unitOfWork.AcquireLock(lockObject);
          try
          {
            List<NeighborIdentity> identities = unitOfWork.NeighborIdentityRepository.Get(null, null, true).ToList();
            foreach (NeighborIdentity identity in identities)
            {
              if (identity.ThumbnailImage != null) AddImageReference(identity.ThumbnailImage);
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
            error = true;
          }
          unitOfWork.ReleaseLock(lockObject);
        }
      }

      bool res = !error;

      log.Info("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Increases reference counter for an image hash.
    /// </summary>
    /// <param name="ImageHash">Hash of the image.</param>
    /// <returns>New reference counter value for the image hash.</returns>
    public int AddImageReference(byte[] ImageHash)
    {
      log.Trace("(ImageHash:'{0}')", ImageHash.ToHex());

      int res = 1;
      lock (referenceCounterLock)
      {
        int refCnt;
        if (referenceCounter.TryGetValue(ImageHash, out refCnt))
        {
          refCnt++;
          referenceCounter[ImageHash] = refCnt;
          res = refCnt;
        }
        else referenceCounter.Add(ImageHash, 1);
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Returns reference counter of an image hash.
    /// </summary>
    /// <param name="ImageHash">Hash of the image.</param>
    /// <returns>Reference counter value for the image hash.</returns>
    public int GetImageReference(byte[] ImageHash)
    {
      log.Trace("(ImageHash:'{0}')", ImageHash.ToHex());

      int res = 0;
      lock (referenceCounterLock)
      {
        int refCnt;
        if (referenceCounter.TryGetValue(ImageHash, out refCnt))
          res = refCnt;
      }

      log.Trace("(-):{0}", res);
      return res;
    }
    /// <summary>
    /// Decreases reference counter for an image hash. If it reaches zero, the image hash is put on the list of candidates for deletion. 
    /// </summary>
    /// <param name="ImageHash">Hash of the image.</param>
    /// <returns>New reference counter value for the image hash.</returns>
    public int RemoveImageReference(byte[] ImageHash)
    {
      log.Trace("(ImageHash:'{0}')", ImageHash.ToHex());

      int res = 0;
      lock (referenceCounterLock)
      {
        int refCnt;
        if (referenceCounter.TryGetValue(ImageHash, out refCnt))
        {
          refCnt--;

          if (refCnt > 0) referenceCounter[ImageHash] = refCnt;
          else referenceCounter.Remove(ImageHash);

          res = refCnt;
        }
      }

      if (res == 0)
      {
        log.Trace("Adding image hash '{0}' to list of images to delete.", ImageHash.ToHex());
        lock (imageDeleteListLock)
        {
          imageDeleteList.Add(ImageHash);
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Scans images folder and deletes images that are not referenced from the database.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private bool DeleteUnusedImages()
    {
      log.Info("()");

      // No need to lock reference counter as this is done only during startup with exclusive access.
      bool res = DeleteUnusedImages(Base.Configuration.ImageDataFolder);

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Scans a specific folder and deletes images that are not referenced from the database.
    /// </summary>
    /// <param name="Directory">Directory to scan.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private bool DeleteUnusedImages(string Directory)
    {
      bool res = false;
      try
      {
        DirectoryInfo dir = new DirectoryInfo(Directory);
        if (!dir.Exists) return res;

        res = true;

        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
          try
          {
            byte[] fileHash = Crypto.FromHex(file.Name);
            int refCnt = 0;
            if (!referenceCounter.TryGetValue(fileHash, out refCnt))
              refCnt = 0;

            if (refCnt == 0)
            {
              log.Trace("Deleting unused image '{0}'.", file.Name);
              file.Delete();
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred while working with file '{0}': {1}", file.FullName, e.ToString());
          }
        }

        DirectoryInfo[] dirs = dir.GetDirectories();
        foreach (DirectoryInfo subdir in dirs)
        {
          res = DeleteUnusedImages(subdir.FullName);
          if (!res) break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      return res;
    }

    /// <summary>
    /// Processes list of images that are waiting for deletion. Up to 50 images are deleted each time this function is called.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public void ProcessImageDeleteList()
    {
      log.Debug("()");

      List<byte[]> checkList = new List<byte[]>();
      lock (imageDeleteListLock)
      {
        int removeItems = Math.Min(imageDeleteList.Count, 50);

        checkList.AddRange(imageDeleteList.GetRange(0, removeItems));
        imageDeleteList.RemoveRange(0, removeItems);
      }

      log.Trace("{0} images will be checked for deletion.", checkList.Count());
      lock (referenceCounterLock)
      {
        foreach (byte[] hash in checkList)
        {
          int refCnt = 0;
          if (referenceCounter.TryGetValue(hash, out refCnt))
          {
            // Do not delete this image, new reference appeared.
            if (refCnt > 0) continue;

            referenceCounter.Remove(hash);
          }

          DeleteImageFile(hash);
        }
      }

      log.Debug("(-)");
    }




    /// <summary>
    /// Constructs a file name inside the image data folder from image hash.
    /// </summary>
    /// <param name="ImageHash">Image data SHA256 hash.</param>
    /// <returns>File name with path for the image. The path can be relative or absolute depending on Configuration.ImageDataFolder settings.</returns>
    public static string GetImageFileName(byte[] ImageHash)
    {
      return GetImageFileName(ImageHash, Kernel.Base.Configuration.ImageDataFolder);
    }


    /// <summary>
    /// Constructs a file name inside the image data folder from image hash.
    /// </summary>
    /// <param name="ImageHash">Image data SHA256 hash.</param>
    /// <param name="Folder">Root images folder in which the image directory structure is to be created and the image stored.</param>
    /// <returns>File name with path for the image.</returns>
    public static string GetImageFileName(byte[] ImageHash, string Folder)
    {
      log.Trace("(ImageHash:'{0}',Folder:'{1}')", ImageHash.ToHex(), Folder);

      string fileName = ImageHash.ToHex();
      string firstLevel = string.Format("{0:X2}", ImageHash[0]);
      string secondLevel = string.Format("{0:X2}", ImageHash[1]);

      string res = string.Format("{0}{1}{2}{1}{3}{1}{4}", Folder, Path.DirectorySeparatorChar, firstLevel, secondLevel, fileName);

      log.Trace("(-):'{0}'", res != null ? res : "null");
      return res;
    }




    /// <summary>
    /// Loads image data from a file.
    /// </summary>
    /// <param name="ImageHash">Image data SHA256 hash.</param>
    /// <returns>Binary image data if the function succeeds, null in case of error.</returns>
    public static async Task<byte[]> GetImageDataAsync(byte[] ImageHash)
    {
      log.Trace("(ImageHash:'{0}')", ImageHash.ToHex());

      byte[] res = null;
      int refCnt = imageManager.GetImageReference(ImageHash);
      if (refCnt == 0)
      {
        log.Warn("Image hash '{0}' has zero reference count, so the file does not exist.", ImageHash.ToHex());
        log.Trace("(-):null");
        return res;
      }

      string fileName = GetImageFileName(ImageHash);
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
    /// Saves image data to image data folder file. This function increments image reference counter.
    /// </summary>
    /// <param name="ImageHash">Image data SHA256 hash.</param>
    /// <param name="Data">Binary image data to save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static async Task<bool> SaveImageDataAsync(byte[] ImageHash, byte[] Data)
    {
      bool res = false;

      int newRefCnt = imageManager.AddImageReference(ImageHash);
      if (newRefCnt == 1)
      {
        // Save data to file only if this is the first reference of the image.
        string fileName = GetImageFileName(ImageHash);
        res = await SaveImageDataAsync(fileName, Data);
      }
      else res = true;

      if (!res) imageManager.RemoveImageReference(ImageHash);

      return res;
    }


    /// <summary>
    /// Saves image data to image data folder file.
    /// </summary>
    /// <param name="FileName">Name of the file to save the image data to.</param>
    /// <param name="Data">Binary image data to save.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private static async Task<bool> SaveImageDataAsync(string FileName, byte[] Data)
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
    /// <param name="ImageHash">Image data SHA256 hash.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private static bool DeleteImageFile(byte[] ImageHash)
    {
      string fileName = GetImageFileName(ImageHash);
      return DeleteImageFile(fileName);
    }

    /// <summary>
    /// Deletes an image file.
    /// </summary>
    /// <param name="FileName">Image file name.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private static bool DeleteImageFile(string FileName)
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

      int size = Math.Min(ProfileImage.Length, Data.Models.IdentityBase.MaxThumbnailImageLengthBytes);
      ThumbnailImage = new byte[size];
      Array.Copy(ProfileImage, ThumbnailImage, ThumbnailImage.Length);

      log.Trace("(-):{0})", ThumbnailImage.Length);
    }
  }
}
