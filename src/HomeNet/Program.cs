using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NLog.Common;
using System.IO;
using System.Diagnostics;

using HomeNet.Kernel;
using System.Net.Sockets;
using System.Threading;

namespace HomeNet
{
  /// <summary>
  /// Represents the main application program started be operating system.
  /// </summary>
  public class Program
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

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
    }
  }
}
