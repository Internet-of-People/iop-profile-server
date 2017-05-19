using ImageSharp;
using ImageSharp.Formats;
using IopCommon;
using IopCrypto;
using IopProtocol;
using IopServerCore.Data;
using IopServerCore.Kernel;
using ProfileServer.Data.Models;
using ProfileServer.Kernel;
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
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Data.ImageManager";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer." + ComponentName);

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


    /// <summary>
    /// Initializes the component.
    /// </summary>
    public ImageManager():
      base(ComponentName)
    {
    }


    public override bool Init()
    {
      log.Info("()");

      bool res = false;
      try
      {
        imageManager = this;

        Configuration.Default.AddImageFormat(new JpegFormat());
        Configuration.Default.AddImageFormat(new PngFormat());

        if (InitializeReferenceCounter()
          && DeleteUnusedImages())
        {
          RegisterCronJobs();

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
    /// Registers component's cron jobs.
    /// </summary>
    public void RegisterCronJobs()
    {
      log.Trace("()");

      List<CronJob> cronJobDefinitions = new List<CronJob>()
      {
        // Deletes unused images from the images folder.
        { new CronJob() { Name = "deleteUnusedImages", StartDelay = 200 * 1000, Interval = 37 * 60 * 1000, HandlerAsync = CronJobHandlerDeleteUnusedImagesAsync } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for "deleteUnusedImages" cron job.
    /// </summary>
    public async void CronJobHandlerDeleteUnusedImagesAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await Task.Run(() => ProcessImageDeleteList());

      log.Trace("(-)");
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
          List<HostedIdentity> identities = unitOfWork.HostedIdentityRepository.Get(i => (i.Initialized == true) && (i.Cancelled == false), null, true).ToList();
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
      bool res = DeleteUnusedImages(Config.Configuration.ImageDataFolder);

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
            byte[] fileHash = file.Name.FromHex();
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
      return GetImageFileName(ImageHash, Config.Configuration.ImageDataFolder);
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

      bool res = false;
      try
      {
        Image image = new Image(Data);
        res = true;
      }
      catch
      {
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether binary data represent a valid PNG or JPEG image and checks that the claimed hash matches the data.
    /// </summary>
    /// <param name="Data">Binary data to check.</param>
    /// <param name="ImageHash">Claimed image hash that has to be equal to SHA256 hash of <paramref name="Data"/> for the function to be able to succeed.</param>
    /// <returns>true if the data represents a valid PNG or JPEG image and if the data hash matches, false otherwise</returns>
    public static bool ValidateImageWithHash(byte[] Data, byte[] ImageHash)
    {
      log.Trace("(Data.Length:{0},ImageHash:'{1}')", Data.Length, ImageHash.ToHex());

      bool res = false;

      if (ValidateImageFormat(Data))
      {
        byte[] hash = Crypto.Sha256(Data);
        if (ByteArrayComparer.Equals(hash, ImageHash))
          res = true;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Creates a thumbnail image from a profile image.
    /// </summary>
    /// <param name="ProfileImage">Binary data of the profile image data.</param>
    /// <param name="ThumbnailImage">On the output, this is filled with thumbnail image data.</param>
    /// <remarks>The caller is responsible for validating the image data provided in <paramref name="ProfileImage"/> before calling this function.</remarks>
    public static void ProfileImageToThumbnailImage(byte[] ProfileImage, out byte[] ThumbnailImage)
    {
      log.Trace("(ProfileImage.Length:{0})", ProfileImage.Length);

      ThumbnailImage = null;

      if (ProfileImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes)
      {
        ThumbnailImage = ProfileImage;
      }
      else
      {
        // We need to make the picture smaller.
        // If it is PNG, we try to convert it to JPEG of equivalent quality.
        // If it is still too big, we resize it.
        // If it is still too big, we reduce the quality down to 60 %.
        // If it is still too big, we resize it again.
        Image originalImage = ImageDataToJpegImage(ProfileImage);
        Image<Color> image = new Image(originalImage);
        double resizeRatio = 1;
        bool done = false;
        while (!done)
        {
          byte[] imageData = ImageToJpegByteArray(image);
          log.Trace("Current image data size is {0} bytes, width is {1} px, height is {2} px, quality is {3} %.", imageData.Length, image.Width, image.Height, image.Quality);

          if (imageData.Length <= IdentityBase.MaxThumbnailImageLengthBytes)
          {
            ThumbnailImage = imageData;
            break;
          }

          // Try to resize the image based on its data size.
          bool largeImage = (double)imageData.Length > 2 * (double)IdentityBase.MaxThumbnailImageLengthBytes;
          if (largeImage)
          {
            double dataSizeRatio = Math.Sqrt((double)IdentityBase.MaxThumbnailImageLengthBytes / (double)imageData.Length);
            resizeRatio *= dataSizeRatio;
            log.Trace("Changing image size from {0}x{1}px to {2}x{3}px.", image.Width, image.Height, (int)(originalImage.Width * resizeRatio), (int)(originalImage.Height * resizeRatio));
            image = ImageResizeByRatio(originalImage, resizeRatio);
            continue;
          }

          // Try to lower the quality.
          if (image.Quality > 60)
          {
            int newQuality = image.Quality - 10;
            if (newQuality < 60) newQuality = 60;
            log.Trace("Changing image quality from {0} % to {1} %.", image.Quality, newQuality);
            image.Quality = newQuality;
            continue;
          }

          // Try to resize the image.
          resizeRatio *= 0.9;
          log.Trace("Changing image size from {0}x{1}px to {2}x{3}px.", image.Width, image.Height, (int)(originalImage.Width * resizeRatio), (int)(originalImage.Height * resizeRatio));
          image = ImageResizeByRatio(originalImage, resizeRatio);
        }
      }

      log.Trace("(-):ThumbnailImage.Length:{0}", ThumbnailImage.Length);
    }


    /// <summary>
    /// Converts the image to JPEG format or equivalent quality.
    /// </summary>
    /// <param name="OriginalImage">Binary data of original image.</param>
    /// <returns>JPEG image.</returns>
    private static Image ImageDataToJpegImage(byte[] OriginalImage)
    {
      Image image = new Image(OriginalImage);
      image.Quality = 90;
      if (image.CurrentImageFormat.Extension == "jpg") return image;

      byte[] jpegData = ImageToJpegByteArray(image);
      Image res = new Image(jpegData);
      res.Quality = 90;
      return res;
    }

    /// <summary>
    /// Converts image to JPEG byte array of specified quality.
    /// </summary>
    /// <param name="Image">Image to save as JPEG byte array.</param>
    /// <returns>Byte array representing JPEG image.</returns>
    private static byte[] ImageToJpegByteArray(Image<Color> Image)
    {
      byte[] res = null;
      using (MemoryStream ms = new MemoryStream())
      {
        Image.SaveAsJpeg(ms, Image.Quality);
        res = ms.ToArray();
      }
      return res;
    }

    /// <summary>
    /// Resizes image to a new size using the specific ratio.
    /// </summary>
    /// <param name="OriginalImage">Image to resize.</param>
    /// <param name="Ratio">Resizing ratio.</param>
    /// <returns>New image, which width and height is smaller than of the original image by the given ratio.</returns>
    private static Image<Color> ImageResizeByRatio(Image OriginalImage, double Ratio)
    {
      int newImageWidth = (int)((double)OriginalImage.Width * Ratio);
      int newImageHeight = (int)((double)OriginalImage.Height * Ratio);
      Image<Color> res = new Image(OriginalImage).Resize(newImageWidth, newImageHeight);
      return res;
    }
  }
}
