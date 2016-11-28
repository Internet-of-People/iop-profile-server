using HomeNetProtocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetSimulator
{
  /// <summary>
  /// Implementation of various helper routines.
  /// </summary>
  public static class Helpers
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    
    /// <summary>Random number generator.</summary>
    public static Random Rng = new Random();

    /// <summary>
    /// Copy directory contents from one directory to another.
    /// </summary>
    /// <param name="SourceDirName">Name of the source directory.</param>
    /// <param name="DestDirName">Name of the destination directory.</param>
    /// <param name="CopySubDirs">True if subdirectories should be copied as well, false otherwise.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    /// <remarks>
    /// Original code - https://msdn.microsoft.com/en-us/library/bb762914.aspx.
    /// </remarks>
    public static bool DirectoryCopy(string SourceDirName, string DestDirName, bool CopySubDirs = true)
    {
      bool res = false;
      DirectoryInfo dir = new DirectoryInfo(SourceDirName);
      if (!dir.Exists) return res;

      try
      {
        res = true;
        DirectoryInfo[] dirs = dir.GetDirectories();

        // If the destination directory doesn't exist, create it.
        if (!Directory.Exists(DestDirName))
          Directory.CreateDirectory(DestDirName);

        // Get the files in the directory and copy them to the new location.
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
          string temppath = Path.Combine(DestDirName, file.Name);
          file.CopyTo(temppath, false);
        }

        // If copying subdirectories, copy them and their contents to new location.
        if (CopySubDirs)
        {
          foreach (DirectoryInfo subdir in dirs)
          {
            string temppath = Path.Combine(DestDirName, subdir.Name);
            res = DirectoryCopy(subdir.FullName, temppath, CopySubDirs);
            if (!res) break;
          }
        }
      }
      catch
      {
        res = false;
      }

      return res;
    }

    /// <summary>
    /// Generates random GPS location within a target area.
    /// </summary>
    /// <param name="Latitude">Latitude of the centre of the target area.</param>
    /// <param name="Longitude">Longitude of the centre of the target area.</param>
    /// <param name="Radius">Radius in metres of the target area.</param>
    /// <returns>GPS location within the target area.</returns>
    public static GpsLocation GenerateRandomGpsLocation(decimal Latitude, decimal Longitude, int Radius)
    {
      GpsLocation res;
      GpsLocation basePoint = new GpsLocation(Latitude, Longitude);
      if (Radius != 0)
      {

        double bearing = Rng.NextDouble() * 360.0;
        double distance = Rng.NextDouble() * (double)Radius;
        res = basePoint.GoVector(bearing, distance);
      }
      else res = basePoint;

      return res;
    }


    /// <summary>
    /// Terminates a process.
    /// </summary>
    /// <param name="Process">Process to terminate.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool KillProcess(Process Process)
    {
      log.Info("()");

      bool res = false;
      try
      {
        Process.Kill();
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred when trying to kill process: {0}", e.ToString());
      }

      log.Info("(-):{0}", res);
      return res;
    }
  }
}
