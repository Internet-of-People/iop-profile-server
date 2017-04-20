using System;
using IopCommon;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace ProfileServer
{
  /// <summary>
  /// Represents the main application program started be operating system.
  /// </summary>
  public class Program
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Program");

    /// <summary>Profile server version.</summary>
    private const string Version = "v1.1.0-beta1";

    /// <summary>File that is being periodically checked to signal shutdown.</summary>
    private const string ExternalShutdownSignalFileName = "shutdown.signal";

    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Program command line arguments.</param>
    public static void Main(string[] args)
    {
      log.Info("()");

      if ((args.Length == 1) && (args[0] == "--version"))
      {
        Console.WriteLine(Version);
        log.Info("(-)");
        NLog.LogManager.Flush();
        NLog.LogManager.Shutdown();
        return;
      }

      Console.WriteLine("Initializing ...");

      if (Kernel.Kernel.Init())
      {
        Console.WriteLine("Profile server is running now.");
        Console.WriteLine("Press ENTER to exit.");

        Task<string> readEnterTask = Task.Run(() => Console.In.ReadLineAsync());
        bool shutdown = false;
        while (!shutdown)
        {
          shutdown = (readEnterTask.Status != TaskStatus.WaitingForActivation) || CheckExternalShutdown();
          Thread.Sleep(1000);
        }

        Kernel.Kernel.Shutdown();
      }
      else Console.WriteLine("Initialization failed.");

      log.Info("(-)");

      // Make sure async logs are flushed before program ends.
      NLog.LogManager.Flush();
      NLog.LogManager.Shutdown();
    }




    /// <summary>
    /// Checks whether an external shutdown signal in form of value 1 in file ExternalShutdownSignalFileName is present.
    /// If the file is present and the value is 1, the value is changed to 0.
    /// </summary>
    /// <returns>true if ExternalShutdownSignalFileName file is present and it contains value 1.</returns>
    public static bool CheckExternalShutdown()
    {
      bool res = false;
      try
      {
        if (File.Exists(ExternalShutdownSignalFileName))
        {
          string text = File.ReadAllText(ExternalShutdownSignalFileName);
          if (text.Trim() == "1")
          {
            if (ClearExternalShutdownSignal())
            {
              log.Info("External shutdown signal detected.");
              res = true;
            }
          }
        }
      }
      catch
      {
      }
      return res;
    }


    /// <summary>
    /// Clears external shutdown signal.
    /// </summary>
    /// <returns>true if the function succeeded, false otherwise.</returns>
    public static bool ClearExternalShutdownSignal()
    {
      log.Trace("()");
      bool res = false;
      try
      {
        File.WriteAllText(ExternalShutdownSignalFileName, "0");
        res = true;
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
