using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetProtocolTests
{
  /// <summary>
  /// This project implements tests according to the IoP Message Protocol - HomeNet Node Tests specification,
  /// which can be found at https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md
  /// 
  /// The tests are expected to be run using external scripts that will prepare the target software
  /// according to each test needs. Preparation of the software differs from one implementation to another.
  /// 
  /// Always read the test's Prerequisites/Inputs sections to know how to setup the testing environment 
  /// for a particular test and how to run it.
  /// </summary>
  public class Program
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Runs the specific test with the given arguments.
    /// </summary>
    /// <param name="args">The first argument is expected to be the unique identifier of the test to run. The other arguments are test specific.</param>
    /// <returns>0 if the test succeeds, 1 if the test fails, 2 if the test identifier is invalid, 3 if the test input is invalid, 4 in case of other error.</returns>
    public static int Main(string[] args)
    {
      log.Debug("(args:'{0}')", string.Join(" ", args));
      int res = 4;

      if (args.Length < 1)
      {
        log.Error("Usage: HomeNetProtocolTests <TestId> [test arguments ...]");
        log.Debug("(-):{0}", res);
        return res;
      }

      string testId = args[0];

      Type testClass = Type.GetType("HomeNetProtocolTests.Tests." + testId);
      if (testClass != null)
      {
        try
        {
          object instance = Activator.CreateInstance(testClass);
          if (instance is ProtocolTest)
          {
            ProtocolTest protocolTest = (ProtocolTest)instance;
            if (protocolTest.ParseArguments(args))
            {
              if (protocolTest.Run())
              {
                res = protocolTest.Passed ? 0 : 1;
              }
            }
            else res = 3;

            log.Info("{0} - {1}", protocolTest.Name, res == 0 ? "PASSED" : "FAILED");
          }
          else
          {
            log.Error("Invalid test ID '{0}'.", testId);
            res = 2;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: '0'.", e.ToString());
        }
      }
      else
      {
        log.Error("Invalid test ID '{0}'.", testId);
        res = 2;
      }


      log.Debug("(-):{0}", res);
      return res;
    }
  }
}
