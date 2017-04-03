using System;
using ProfileServer.Kernel;
using System.Threading;
using System.IO;
using System.Threading.Tasks;

namespace ProfileServer
{
  /// <summary>
  /// Represents the main application program started be operating system.
  /// </summary>
  public class Program
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Program");

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
        Console.WriteLine("v1.0.1-alpha2");
        log.Info("(-)");
        NLog.LogManager.Flush();
        NLog.LogManager.Shutdown();
        return;
      }

      Console.WriteLine("Initializing ...");

      if (Base.Init())
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

        Base.Shutdown();
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
            File.WriteAllText(ExternalShutdownSignalFileName, "0");
            log.Info("External shutdown signal detected.");
            res = true;
          }
        }
      }
      catch
      {
      }
      return res;
    }
  }
}
