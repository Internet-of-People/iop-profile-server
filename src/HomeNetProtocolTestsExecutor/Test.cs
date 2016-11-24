using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetProtocolTestsExecutor
{
  /// <summary>
  /// Information needed to execute a test case.
  /// </summary>
  public class Test
  {
    /// <summary>Name of the test.</summary>
    public string Name;

    /// <summary>Name of the configuration file to be used with this test.</summary>
    public string Conf;

    /// <summary>Command line arguments for the test.</summary>
    public string[] Args;

    /// <summary>True for tests that take a long time (more than 3 minutes) to complete, false for shorter tests.</summary>
    public bool LongTime;

    /// <summary>
    /// Initialize the test information.
    /// </summary>
    /// <param name="Name">Name of the test.</param>
    /// <param name="Conf">Name of the configuration file to be used with this test.</param>
    /// <param name="Args">Command line arguments for the test.</param>
    /// <param name="LongTime">True for tests that take a long time (more than 3 minutes) to complete, false for shorter tests.</param>
    public Test(string Name, string Conf, string[] Args, bool LongTime)
    {
      this.Name = Name;
      this.Conf = Conf;
      this.Args = Args;
      this.LongTime = LongTime;
    }
  }
}
