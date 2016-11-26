using HomeNetProtocol;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetSimulator
{
  /// <summary>
  /// Parser of commands given in a scenario. 
  /// </summary>
  public static class CommandParser
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Reads contents of a scenario file and parses its commands.
    /// </summary>
    /// <param name="ScenarioFile">Name of the scenario file to parse.</param>
    /// <returns>List of commands.</returns>
    public static List<Command> ParseScenario(string ScenarioFile)
    {
      log.Trace("(ScenarioFile:'{0}')", ScenarioFile);

      List<Command> res = null;
      try
      {
        if (File.Exists(ScenarioFile))
        {
          string[] lines = File.ReadAllLines(ScenarioFile);
          res = ParseScenario(lines);
        }
        else log.Error("Unable to find scenario file '{0}'.", ScenarioFile);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (res != null) log.Trace("(-):*.Count={0}", res.Count);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Parses lines of a scenario file.
    /// </summary>
    /// <param name="Lines">List of scenario lines.</param>
    /// <returns>List of commands.</returns>
    public static List<Command> ParseScenario(string[] Lines)
    {
      log.Trace("()");

      bool error = false;
      List<Command> commands = new List<Command>();
      if ((Lines != null) && (Lines.Length > 0))
      {
        int lineNumber = 0;
        try
        {
          foreach (string aline in Lines)
          {
            lineNumber++;
            string line = aline.Trim();
            if ((line.Length == 0) || (line[0] == '#')) continue;

            string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Command command = ParseCommand(parts, lineNumber);
            if (command == null)
            {
              error = true;
              break;
            }

            commands.Add(command);
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred while parsing line number {0}: {1}", lineNumber, e.ToString());
        }
      }
      else log.Error("Scenario file is empty.");

      List<Command> res = null;
      if (!error)
        res = commands;

      if (res != null) log.Trace("(-):*.Count={0}", res.Count);
      else log.Trace("(-):null");
      return res;
    }

    /// <summary>
    /// Parses a single line of a scenario file.
    /// </summary>
    /// <param name="Parts">List of tokens of the line being parsed.</param>
    /// <param name="LineNumber">Line number of the line being parsed.</param>
    /// <returns></returns>
    public static Command ParseCommand(string[] Parts, int LineNumber)
    {
      log.Trace("(Parse:'{0}',LineNumber:{1})", string.Join(" ", Parts), LineNumber);

      Command res = null;
      CommandType commandType;
      if (!Enum.TryParse(Parts[0], out commandType))
        commandType = CommandType.Unknown;

      int paramCount = Parts.Length - 1;
      int p = 1;


      switch (commandType)
      {
        case CommandType.ProfileServer:
          {
            if (paramCount != 6)
            {
              log.Error("ProfileServer requires 6 parameters, but only {0} given on line {1}.", paramCount, LineNumber);
              break;
            }

            CommandProfileServer command = new CommandProfileServer()
            {
              GroupName = Parts[p++],
              Count = int.Parse(Parts[p++]),
              BasePort = int.Parse(Parts[p++]),
              Latitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Longitude = decimal.Parse(Parts[p++], CultureInfo.InvariantCulture),
              Radius = int.Parse(Parts[p++])
            };

            bool countValid = (1 <= command.Count) && (command.Count <= 999);
            if (!countValid)
            {
              log.Error("Count must be an integer between 1 and 999. {0} given on line {1}.", command.Count, LineNumber);
              break;
            }

            int basePortUpperLimit = 65535 - 20 * command.Count;
            bool basePortValid = (1 <= command.BasePort) && (command.BasePort <= basePortUpperLimit);
            if (!basePortValid)
            {
              log.Error("Having Count {0}, BasePort must be an integer between 1 and {1}. {2} given on line {3}.", command.Count, basePortUpperLimit, command.BasePort, LineNumber);
              break;
            }

            bool latitudeValid = new GpsLocation(command.Latitude, 0).IsValid();
            if (!latitudeValid)
            {
              log.Error("Latitude must be a decimal number between -90 and 90. {0} given on line {1}.", command.Latitude, LineNumber);
              break;
            }

            bool longitudeValid = new GpsLocation(0, command.Longitude).IsValid();
            if (!latitudeValid)
            {
              log.Error("Longitude must be a decimal number between -179.999999 and 180. {0} given on line {1}.", command.Longitude, LineNumber);
              break;
            }

            bool radiusValid = (0 <= command.Radius) && (command.Radius <= 20000);
            if (!radiusValid)
            {
              log.Error("Radius must be an integer between 0 and 20000. {0} given on line {1}.", command.Longitude, LineNumber);
              break;
            }

            res = command;
            break;
          }

        default:
          log.Error("Invalid command '{0}' on line number {1}.", Parts[0], LineNumber);
          break;
      }


      log.Trace("(-):{0}", res != null ? "Command" : "null");
      return res;
    }
  }
}
