using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerProtocolTestsExecutor
{
  /// <summary>
  /// Tests Executor is a simple program that is used to execute functional tests as defined 
  /// in https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md.
  /// 
  /// 
  /// Usage: ProfileServerProtocolTestsExecutor [TestFirst TestLast] ["EnableLongTime"]
  ///   * TestFirst is a name of the first test to execute, this is optional.
  ///   * TestLast is a name of the last test to execute, can only be used with TestFirst.
  ///   * "EnableLongtime" - if this string is not included, the test executor skips long time tests.
  ///   
  /// 
  /// Examples:
  /// 
  /// 1) To execute all tests except for the long time tests, simply run:
  /// 
  /// > ProfileServerProtocolTestsExecutor 
  /// 
  /// 
  /// 2) To execute all tests, run:
  /// 
  /// > ProfileServerProtocolTestsExecutor EnableLongTime
  /// 
  /// 
  /// 3) To execute all tests between PS01006 and PS03005 (both inclusive) except for the long time tests, run:
  /// 
  /// > ProfileServerProtocolTestsExecutor PS01006 PS03005
  /// 
  /// 
  /// 4) To execute all tests between PS01006 and PS03005 (both inclusive), run:
  /// 
  /// > ProfileServerProtocolTestsExecutor PS01006 PS03005 EnableLongTime
  /// 
  /// 
  /// 
  /// Requirements:
  /// 
  /// Before Test Executor can be started, the following setup must be prepared.
  /// The folder with the Test Executor's executable must contain:
  /// 
  ///   * "configs" directory, which contains all .conf files that are listed in the static description of the tests (Tests list) in the code below:
  ///     * "ProfileServer-default.conf" is the default configuration file that is expected to be used as a default file for the public release.
  ///     * "ProfileServer-max-identities-1.conf" is the default configuration file with max_hosted_identities set to 1.
  ///     * "ProfileServer-different-ports.conf" is the default configuration file with each server role running on different port.
  ///   * "ProfileServer-binaries" directory, which contains all files needed for the profile server to run.
  ///     * Additionally, there must be "ProfileServer-empty.db" file with an initialized but otherwise empty database.
  ///   * "tests-binaries" directory, which contains all files needed for the ProfileServerProtocolTests project to run.
  ///   * "NLog.config" file, othewise you won't be able to see any results.
  /// 
  /// The configuration files are expected to define following ports for interfaces:
  /// 
  ///   * primary_interface_port = 16987
  ///   * client_non_customer_interface_port = 16988 
  ///   * client_customer_interface_port = client_non_customer_interface_port or 16989 in "ProfileServer-different-ports.conf"
  ///   
  /// </summary>
  public class Program
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocolTestsExecutor.Program");

    /// <summary>
    /// Information about all tests required for their execution.
    /// </summary>
    public static List<Test> Tests = new List<Test>()
    {
      new Test("PS00001", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS00002", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS00003", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("PS00004", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("PS00005", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("PS00006", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, true),
      new Test("PS00007", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, true),
      new Test("PS00008", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, true),
      new Test("PS00009", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, true),
      new Test("PS00010", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01001", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01002", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01003", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01004", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01005", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01006", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01007", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01008", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01009", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01010", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01011", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01012", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01013", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01014", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01015", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01016", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01017", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS01018", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),

      new Test("PS02001", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02002", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02003", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02004", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02005", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02006", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02007", "ProfileServer-max-identities-1.conf", new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02008", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02009", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02010", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02011", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02012", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02013", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02014", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02015", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02016", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02017", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02018", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02019", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02020", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02021", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02022", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02023", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02024", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02025", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02026", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS02027", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),

      new Test("PS03001", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS03002", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16989" }, false),
      new Test("PS03003", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16989" }, false),
      new Test("PS03004", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS03005", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS03006", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),

      new Test("PS04001", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16988", "16989" }, false),
      new Test("PS04002", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS04003", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04004", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04005", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04006", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04007", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04008", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("PS04009", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04010", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04011", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04012", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04013", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04014", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("PS04015", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),

      new Test("PS05001", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05002", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05003", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05004", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05005", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16987" }, true),
      new Test("PS05006", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05007", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05008", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05009", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05010", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05011", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05012", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05013", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05014", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05015", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05016", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05017", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05018", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05019", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05020", "ProfileServer-different-ports.conf",  new string[] { "127.0.0.1", "16987" }, true),
      new Test("PS05021", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05022", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05023", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05024", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS05025", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),

      new Test("PS06001", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS06002", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS06003", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("PS06004", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),

      new Test("PS07001", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS07002", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("PS07003", "ProfileServer-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
    };


    /// <summary>Event that is set when the profile server is ready for the test.</summary>
    public static ManualResetEvent ProfileServerReadyEvent = new ManualResetEvent(false);

    /// <summary>Indication of whether a currently executed test passed.</summary>
    public static bool TestPassed = false;

    /// <summary>Indication of whether a currently executed test failed.</summary>
    public static bool TestFailed = false;


    /// <summary>
    /// Main program routine.
    /// </summary>
    /// <param name="args">Command line arguments, see the usage description above.</param>
    public static void Main(string[] args)
    {
      log.Trace("()");

      // Command line argument parsing.
      bool runLongTimeTests = false;
      int testFromIndex = 0;
      int testToIndex = Tests.Count - 1;

      if (args.Length > 0)
      {
        bool usage = false;
        if (args.Length >= 2)
        {
          testFromIndex = -1;
          testToIndex = -1;
          string testFromName = args[0].ToLower();
          string testToName = args[1].ToLower();
          for (int i = 0; i < Tests.Count; i++)
          {
            if (Tests[i].Name.ToLower() == testFromName)
              testFromIndex = i;

            if (Tests[i].Name.ToLower() == testToName)
              testToIndex = i;

            if ((testFromIndex != -1) && (testToIndex != -1))
              break;
          }

          usage = (testFromIndex == -1) || (testToIndex == -1);

          if (!usage && (args.Length > 2))
          {
            if (args.Length == 3)
            {
              if (args[2].ToLower() == "enablelongtime")
                runLongTimeTests = true;
            }
            else usage = true;
          }
        }
        else if ((args.Length == 1) && (args[0].ToLower() == "enablelongtime"))
        {
          runLongTimeTests = true;
        }
        else usage = true;

        if (usage)
        {
          log.Error("Usage: ProfileServerProtocolTestsExecutor [<TestFirst> <TestLast>] [EnableLongTime]");
          log.Trace("(-)");
          return;
        }
      }


      // Test execution part.
      int passed = 0;
      int failed = 0;
      int noresult = 0;
      for (int i = testFromIndex; i < testToIndex + 1; i++)
      {
        Test test = Tests[i];
        if (!runLongTimeTests && test.LongTime)
        {
          log.Trace("Test '{0}' is long time and will be skipped.", test.Name);
          log.Info("{0} - SKIPPED", test.Name);
          continue;
        }

        log.Trace("Starting test '{0}'.", test.Name);
        File.Copy(Path.Combine("ProfileServer-binaries", "ProfileServer-empty.db"), Path.Combine("ProfileServer-binaries", "ProfileServer.db"), true);
        File.Copy(Path.Combine("configs", test.Conf), Path.Combine("ProfileServer-binaries", "ProfileServer.conf"), true);

        ProfileServerReadyEvent.Reset();
        TestPassed = false;
        TestFailed = false;

        Process profileServerProcess = RunProfileServer(Path.Combine("ProfileServer-binaries", "ProfileServer"));
        if (profileServerProcess != null)
        {
          log.Trace("Waiting for profile server to start ...");
          if (ProfileServerReadyEvent.WaitOne(20 * 1000))
          {
            log.Trace("Profile server ready!");

            Process testProcess = RunTest(Path.Combine("tests-binaries", "ProfileServerProtocolTests"), test.Name, test.Args);
            int maxTime = test.LongTime ? 10 * 60 * 1000 : 2 * 60 * 1000;
            if (testProcess.WaitForExit(maxTime))
            {
              log.Trace("Sending ENTER to profile server.");
              string inputData = Environment.NewLine;
              using (StreamWriter sw = new StreamWriter(profileServerProcess.StandardInput.BaseStream, Encoding.UTF8))
              {
                sw.Write(inputData);
              }

              log.Trace("Waiting for profile server process to stop.");
              if (profileServerProcess.WaitForExit(10 * 1000))
              {
                log.Trace("Profile server process stopped.");
                if (TestPassed)
                {
                  if (!TestFailed)
                  {
                    log.Info("{0} - PASSED", test.Name);
                    passed++;
                  }
                  else
                  {
                    log.Error("Test passed and failed!?");
                    noresult++;
                    break;
                  }

                }
                else if (TestFailed)
                {
                  log.Info("{0} - FAILED", test.Name);
                  failed++;
                }
                else
                {
                  log.Error("Test neither passed nor failed.");
                  noresult++;
                  break;
                }
              }
              else
              {
                log.Error("Profile server process did not finish on time, killing it now.");
                KillProcess(profileServerProcess);
                break;
              }
            }
            else
            {
              log.Error("Test process did not finish on time, killing it now.");
              KillProcess(testProcess);
              noresult++;
              break;
            }
          }
          else
          {
            log.Error("Profile server failed to start on time.");
            break;
          }
        }
        else
        {
          log.Error("Unable to start profile server.");
          break;
        }
      }

      int total = passed + failed + noresult;
      log.Info("");
      log.Info("Passed: {0}/{1}", passed, total);
      log.Info("Failed: {0}/{1}", failed, total);

      log.Trace("(-)");
    }


    /// <summary>
    /// Runs the profile server
    /// </summary>
    /// <param name="Executable">Profile server executable file name.</param>
    /// <returns>Running profile server process.</returns>
    public static Process RunProfileServer(string Executable)
    {
      log.Trace("()");
      bool error = false;
      Process process = null;
      bool processIsRunning = false;
      try
      {
        process = new Process();
        string fullFileName = Path.GetFullPath(Executable);
        process.StartInfo.FileName = Executable;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WorkingDirectory = Path.GetDirectoryName(fullFileName);
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

        log.Trace("Starting command line: '{0}'", process.StartInfo.FileName);

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += new DataReceivedEventHandler(ProfileServerProcessOutputHandler);
        process.ErrorDataReceived += new DataReceivedEventHandler(ProfileServerProcessOutputHandler);

        process.Start();
        processIsRunning = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred during starting: {0}", e.ToString());
        error = true;
      }

      if (!error)
      {
        try
        {
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
        }
        catch (Exception e)
        {
          log.Error("Exception occurred after start: {0}", e.ToString());
          error = true;
        }
      }

      if (error)
      {
        if (processIsRunning && (process != null))
          KillProcess(process);
      }

      Process res = !error ? process : null;
      log.Trace("(-)");
      return res;
    }


    /// <summary>
    /// Standard output handler for profile server process.
    /// </summary>
    /// <param name="SendingProcess">Not used.</param>
    /// <param name="OutLine">Line of output without new line character.</param>
    public static void ProfileServerProcessOutputHandler(object SendingProcess, DataReceivedEventArgs OutLine)
    {
      if (OutLine.Data != null)
        ProfileServerProcessNewOutput(OutLine.Data + Environment.NewLine);
    }

    /// <summary>
    /// Simple analyzer of the profile server process standard output, 
    /// that can recognize when the server is fully initialized and ready for the test.
    /// </summary>
    /// <param name="Data">Line of output.</param>
    public static void ProfileServerProcessNewOutput(string Data)
    {
      log.Trace("(Data.Length:{0})", Data.Length);
      log.Trace("Data: {0}", Data);

      if (Data.Contains("ENTER"))
        ProfileServerReadyEvent.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Terminates a process.
    /// </summary>
    /// <param name="Process">Process to terminate.</param>
    private static void KillProcess(Process Process)
    {
      log.Info("()");
      try
      {
        Process.Kill();
      }
      catch (Exception e)
      {
        log.Error("Exception occurred when trying to kill process: {0}", e.ToString());
      }
      log.Info("(-)");
    }


    /// <summary>
    /// Runs a single specific test.
    /// </summary>
    /// <param name="Executable">ProfileServerProtocolTests executable file name.</param>
    /// <param name="TestName">Name of the test to execute.</param>
    /// <param name="Arguments">Command line arguments for the test.</param>
    /// <returns></returns>
    public static Process RunTest(string Executable, string TestName, string[] Arguments)
    {
      log.Trace("()");
      bool error = false;
      Process process = null;
      bool processIsRunning = false;
      try
      {
        process = new Process();
        string fullFileName = Path.GetFullPath(Executable);
        process.StartInfo.FileName = Executable;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WorkingDirectory = Path.GetDirectoryName(fullFileName);
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

        string args = TestName;
        if (Arguments != null) args = TestName + " " + string.Join(" ", Arguments);

        bool emptyArgs = string.IsNullOrEmpty(args);
        if (!emptyArgs) process.StartInfo.Arguments = args;

        log.Trace("Starting command line: '{0}'{1}{2}", process.StartInfo.FileName, emptyArgs ? "" : " ", args);

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += new DataReceivedEventHandler(TestProcessOutputHandler);
        process.ErrorDataReceived += new DataReceivedEventHandler(TestProcessOutputHandler);

        process.Start();
        processIsRunning = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred during starting: {0}", e.ToString());
        error = true;
      }

      if (!error)
      {
        try
        {
          process.BeginOutputReadLine();
          process.BeginErrorReadLine();
        }
        catch (Exception e)
        {
          log.Error("Exception occurred after start: {0}", e.ToString());
          error = true;
        }
      }

      if (error)
      {
        if (processIsRunning && (process != null))
        {
          log.Trace("Killing process because of error.");
          KillProcess(process);
        }
      }

      Process res = !error ? process : null;
      log.Trace("(-)");
      return res;
    }


    /// <summary>
    /// Standard output handler for profile server process.
    /// </summary>
    /// <param name="SendingProcess">Not used.</param>
    /// <param name="OutLine">Line of output without new line character.</param>
    public static void TestProcessOutputHandler(object SendingProcess, DataReceivedEventArgs OutLine)
    {
      if (OutLine.Data != null)
        TestProcessNewOutput(OutLine.Data + Environment.NewLine);
    }


    /// <summary>
    /// Simple analyzer of the test process standard output, 
    /// that can recognize whether the test failed or passed.
    /// </summary>
    /// <param name="Data">Line of output.</param>
    public static void TestProcessNewOutput(string Data)
    {
      log.Trace("(Data.Length:{0})", Data.Length);

      log.Trace("Data: {0}", Data);

      if (Data.Contains("PASSED")) TestPassed = true;
      else if (Data.Contains("FAILED")) TestFailed = true;

      log.Trace("(-)");
    }
  }
}
