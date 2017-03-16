using IopCommon;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerNetworkSimulator
{
  /// <summary>
  /// ProfileServer Simulator simulates network of profile servers on a single machine.
  /// 
  /// 
  /// Usage: ProfileServerSimulator ScenarioFile
  ///   * ScenarioFile contains a description of the simulation to execute.
  ///   
  /// </summary>
  public class Program
  {
    private static Logger log = new Logger("ProfileServerNetworkSimulator.Program");

    /// <summary>
    /// Main program routine.
    /// </summary>
    /// <param name="args">Command line arguments, see the usage description above.</param>
    public static void Main(string[] args)
    {
      log.Trace("(args:'{0}')", string.Join(",", args));

      if (args.Length != 1)
      {
        log.Error("Usage: ProfileServerSimulator <ScenarioFile>");
        log.Trace("(-)");
        return;
      }

      CultureInfo culture = new CultureInfo("en-US");
      CultureInfo.DefaultThreadCurrentCulture = culture;
      CultureInfo.DefaultThreadCurrentUICulture = culture;
      CultureInfo.CurrentCulture = culture;
      CultureInfo.CurrentUICulture = culture;

      string scenarioFile = args[0];
      log.Info("Loading scenario file '{0}'.", scenarioFile);
      log.Info("");
      List<Command> commands = CommandParser.ParseScenarioFile(scenarioFile);
      if (commands == null)
      {
        log.Trace("(-)");
        return;
      }

      CommandProcessor processor = new CommandProcessor(commands);
      bool success = processor.Execute();

      log.Info("");
      log.Info("All done, shutting down ...");
      processor.Shutdown();

      if (success)
      {
        log.Info("");
        log.Info("Analyzing log files ...");
        success = processor.CheckLogs();
      }

      log.Info("");
      log.Info("SCENARIO {0}", success ? "PASSED" : "FAILED");
      log.Info("");

      log.Trace("(-)");

      // Make sure async logs are flushed before program ends.
      NLog.LogManager.Flush();
      NLog.LogManager.Shutdown();
    }
  }
}
