using IopCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests
{
  /// <summary>Types of test arguments.</summary>
  public enum ProtocolTestArgumentType
  {
    /// <summary>IPv4 or IPv6 address.</summary>
    IpAddress,

    /// <summary>TCP or UDP port - an integer between 1 and 65535.</summary>
    Port
  }

  /// <summary>
  /// Description of a test input (argument).
  /// </summary>
  public class ProtocolTestArgument
  {
    /// <summary>Name of the argument.</summary>
    public string Name;

    /// <summary>Type of the argument.</summary>
    public ProtocolTestArgumentType Type;

    /// <summary>
    /// Initializes an argument instance.
    /// </summary>
    /// <param name="Name">Name of the argument.</param>
    /// <param name="Type">Type of the argument</param>
    public ProtocolTestArgument(string Name, ProtocolTestArgumentType Type)
    {
      this.Name = Name;
      this.Type = Type;
    }

    public override string ToString()
    {
      return "'" + Name + "'";
    }
  }

  /// <summary>
  /// Base class for all tests defining a common test interface.
  /// </summary>
  public abstract class ProtocolTest
  {
    private static Logger log = new Logger("ProfileServerProtocolTests.Tests.ProtocolTest");

    /// <summary>Name of the test.</summary>
    public abstract string Name { get; }

    /// <summary>List of test's arguments according to the specification.</summary>
    public abstract List<ProtocolTestArgument> ArgumentDescriptions { get; }

    /// <summary>Actual values from command line for arguments defined by ArgumentDescriptions mapped by argument name.</summary>
    public Dictionary<string, object> ArgumentValues;

    /// <summary>If the test executes properly, this is set to true if the test is passed, otherwise it is set to false.</summary>
    public bool Passed = false;

    /// <summary>
    /// Runs the test.
    /// </summary>
    /// <returns>true if the test was passed, false if it was failed.</returns>
    public abstract Task<bool> RunAsync();


    /// <summary>
    /// Runs the test and saves the result.
    /// </summary>
    /// <returns>true if the test executes correctly, false otherwise. 
    /// If the test executes correctly, the test result is saved to Passed field.</returns>
    public bool Run()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        Task<bool> runTask = RunAsync();
        res = runTask.Result;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0},Passed={1}", res, Passed);
      return res;
    }


    /// <summary>
    /// Checks if all the test inputs are provided by the command arguments and parses their values.
    /// </summary>
    /// <param name="args">Program command line arguments.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool ParseArguments(string[] args)
    {
      log.Trace("(args:'{0}')", string.Join(" ", args));

      bool res = false;

      if (args.Length - 1 == ArgumentDescriptions.Count)
      {
        ArgumentValues = new Dictionary<string, object>(StringComparer.Ordinal);
        int index = 1;
        foreach (ProtocolTestArgument argument in ArgumentDescriptions)
        {
          string arg = args[index];
          index++;

          object argumentValue = null;
          switch (argument.Type)
          {
            case ProtocolTestArgumentType.IpAddress:
              {
                IPAddress value;
                if (IPAddress.TryParse(arg, out value))
                  argumentValue = value;
                
                break;
              }

            case ProtocolTestArgumentType.Port:
              {
                int value;
                if (int.TryParse(arg, out value) && ((0 < value) && (value <= 65535)))
                  argumentValue = value;

                break;
              }
          }

          if (argumentValue == null)
          {
            log.Error("Invalid value '{0}' for argument '{1}' type '{2}'.", arg, argument.Name, argument.Type);
            break;
          }

          ArgumentValues.Add(argument.Name, argumentValue);
        }

        res = ArgumentValues.Count == ArgumentDescriptions.Count;
      }
      else log.Error("Test {0} arguments: {1}", Name, string.Join(" ", ArgumentDescriptions));

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
