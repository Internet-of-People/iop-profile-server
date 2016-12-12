using System;
using ProfileServer.Kernel;

namespace ProfileServer
{
  /// <summary>
  /// Represents the main application program started be operating system.
  /// </summary>
  public class Program
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Program");

    /// <summary>
    /// Application entry point.
    /// </summary>
    /// <param name="args">Program command line arguments.</param>
    public static void Main(string[] args)
    {
      log.Info("()");
      if (Base.Init())
      {
        Console.WriteLine("Profile server is running now.");
        Console.WriteLine("Press ENTER to exit.");
        Console.ReadLine();

        Base.Shutdown();
      }
      else Console.WriteLine("Initialization failed.");

      log.Info("(-)");

      // Make sure async logs are flushed before program ends.
      NLog.LogManager.Flush();
      NLog.LogManager.Shutdown();
    }
  }
}
