using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Utils
{
  /// <summary>
  /// Helper functions for file operations.
  /// </summary>
  public static class FileHelper
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Utils.FileHelper");

    /// <summary>
    /// Opens a binary file, reads the contents of the file into a byte array, and then closes the file.
    /// </summary>
    /// <param name="FileName">Name of the file to read.</param>
    /// <returns>Byte array containing the contents of the file.</returns>
    public static async Task<byte[]> ReadAllBytesAsync(string FileName)
    {
      byte[] res = null;
      using (FileStream file = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true))
      {
        res = new byte[file.Length];
        await file.ReadAsync(res, 0, (int)file.Length);
      }
      return res;
    }

    /// <summary>
    /// Creates a new file, writes the specified byte array to the file, and then closes the file. If the target file already exists, it is overwritten.
    /// </summary>
    /// <param name="FileName">Name of the file to write.</param>
    /// <param name="Data">Bytes to write to the file.</param>
    public static async Task WriteAllBytesAsync(string FileName, byte[] Data)
    {
      using (FileStream file = new FileStream(FileName, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
      {
        await file.WriteAsync(Data, 0, Data.Length);
      }
    }

    /// <summary>
    /// Tries to find a file using its name or path.
    /// </summary>
    /// <param name="FileName">Name of the file or relative or full path to the file.</param>
    /// <param name="ExistingFileName">String to receive the name of an existing file if the function succeeds.</param>
    /// <returns>true if the file is found, false otherwise.</returns>
    public static bool FindFile(string FileName, out string ExistingFileName)
    {
      log.Trace("(FileName:'{0}')", FileName);

      bool res = false;
      ExistingFileName = null;
      if (File.Exists(FileName))
      {
        ExistingFileName = FileName;
        res = true;
      }
      else
      {
        string path = System.Reflection.Assembly.GetEntryAssembly().Location;
        path = Path.GetDirectoryName(path);
        path = Path.Combine(path, FileName);
        log.Trace("Checking path '{0}'.", path);
        if (File.Exists(path))
        {
          ExistingFileName = path;
          res = true;
        }
      }

      if (res) log.Trace("(-):{0},ExistingFileName='{1}'", res, ExistingFileName);
      else log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Tries to find a directory using its name or path.
    /// </summary>
    /// <param name="DirectoryName">Name of the directory or relative or full path to the directory.</param>
    /// <param name="ExistingDirectoryName">String to receive the name of an existing directory if the function succeeds.</param>
    /// <returns>true if the directory is found, false otherwise.</returns>
    public static bool FindDirectory(string DirectoryName, out string ExistingDirectoryName)
    {
      log.Trace("(DirectoryName:'{0}')", DirectoryName);

      bool res = false;
      ExistingDirectoryName = null;
      if (Directory.Exists(DirectoryName))
      {
        ExistingDirectoryName = DirectoryName;
        res = true;
      }
      else
      {
        string path = System.Reflection.Assembly.GetEntryAssembly().Location;
        path = Path.GetDirectoryName(path);
        path = Path.Combine(path, DirectoryName);
        log.Trace("Checking path '{0}'.", path);
        if (Directory.Exists(path))
        {
          ExistingDirectoryName = path;
          res = true;
        }
      }

      if (res) log.Trace("(-):{0},ExistingDirectoryName='{1}'", res, ExistingDirectoryName);
      else log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Removes all files and folders from a specific directory.
    /// </summary>
    /// <param name="DirectoryName">Name of the directory to clean.</param>
    /// <returns>true if the function succeeds, false otherwise</returns>
    public static bool CleanDirectory(string DirectoryName)
    {
      log.Trace("(DirectoryName:'{0}')", DirectoryName);

      bool res = false;
      try
      {
        string existingDirectoryName;
        if (FindDirectory(DirectoryName, out existingDirectoryName))
        {
          DirectoryInfo di = new DirectoryInfo(DirectoryName);

          foreach (FileInfo file in di.GetFiles())
            file.Delete();

          foreach (DirectoryInfo dir in di.GetDirectories())
            dir.Delete(true);

          res = true;
        }
        else log.Error("Directory '{0}' not found.", DirectoryName);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
