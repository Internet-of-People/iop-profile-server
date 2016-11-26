using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetSimulator
{
  /// <summary>
  /// HomeNet Simulator simulates network of profile servers on a single machine.
  /// 
  /// 
  /// Usage: HomeNetSimulator ScenarioFile
  ///   * ScenarioFile contains a description of the simulation to execute.
  ///   
  /// </summary>
  public class Program
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Main program routine.
    /// </summary>
    /// <param name="args">Command line arguments, see the usage description above.</param>
    public static void Main(string[] args)
    {
      log.Trace("(args:'{0}')", string.Join(",", args));

      if (args.Length != 1)
      {
        log.Error("Usage: HomeNetSimulator <ScenarioFile>");
        log.Trace("(-)");
        return;
      }

      string scenarioFile = args[0];
      List<Command> commands = CommandParser.ParseScenario(scenarioFile);
      if (commands == null)
      {
        log.Trace("(-)");
        return;
      }

      log.Trace("(-)");
    }
  }
}
