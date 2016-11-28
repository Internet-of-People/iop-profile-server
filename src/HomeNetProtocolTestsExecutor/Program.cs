using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HomeNetProtocolTestsExecutor
{
  /// <summary>
  /// Tests Executor is a simple program that is used to execute functional tests as defined 
  /// in https://github.com/Internet-of-People/message-protocol/blob/master/TESTS.md.
  /// 
  /// 
  /// Usage: HomeNetProtocolTestsExecutor [<TestFirst> <TestLast>] ["EnableLongTime"]
  ///   * TestFirst is a name of the first test to execute, this is optional.
  ///   * TestLast is a name of the last test to execute, can only be used with TestFirst.
  ///   * "EnableLongtime" - if this string is not included, the test executor skips long time tests.
  ///   
  /// 
  /// Examples:
  /// 
  /// 1) To execute all tests except for the long time tests, simply run:
  /// 
  /// > HomeNetProtocolTestsExecutor 
  /// 
  /// 
  /// 2) To execute all tests, run:
  /// 
  /// > HomeNetProtocolTestsExecutor EnableLongTime
  /// 
  /// 
  /// 3) To execute all tests between HN01006 and HN03005 (both inclusive) except for the long time tests, run:
  /// 
  /// > HomeNetProtocolTestsExecutor HN01006 HN03005
  /// 
  /// 
  /// 4) To execute all tests between HN01006 and HN03005 (both inclusive), run:
  /// 
  /// > HomeNetProtocolTestsExecutor HN01006 HN03005 EnableLongTime
  /// 
  /// 
  /// 
  /// Requirements:
  /// 
  /// Before Test Executor can be started, the following setup must be prepared.
  /// The folder with the Test Executor's executable must contain:
  /// 
  ///   * "configs" directory, which contains all .conf files that are listed in the static description of the tests (Tests list) in the code below:
  ///     * "HomeNet-default.conf" is the default configuration file that is expected to be used as a default file for the public release.
  ///     * "HomeNet-max-identities-1.conf" is the default configuration file with max_hosted_identities set to 1.
  ///     * "HomeNet-different-ports.conf" is the default configuration file with each server role running on different port.
  ///   * "HomeNet-binaries" directory, which contains all files needed for the profile server to run.
  ///     * Additionally, there must be "HomeNet-empty.db" file with an initialized but otherwise empty database.
  ///   * "tests-binaries" directory, which contains all files needed for the HomeNetProtocolTests project to run.
  ///   * "NLog.config" file, othewise you won't be able to see any results.
  /// 
  /// The configuration files are expected to define following ports for interfaces:
  /// 
  ///   * primary_interface_port = 16987
  ///   * client_non_customer_interface_port = 16988 
  ///   * client_customer_interface_port = client_non_customer_interface_port or 16989 in "HomeNet-different-ports.conf"
  ///   
  /// </summary>
  public class Program
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Information about all tests required for their execution.
    /// </summary>
    public static List<Test> Tests = new List<Test>()
    {
      new Test("HN00001", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN00002", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN00003", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("HN00004", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("HN00005", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("HN00006", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, true),
      new Test("HN00007", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, true),
      new Test("HN00008", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, true),
      new Test("HN00009", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, true),
      new Test("HN00010", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01001", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01002", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01003", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01004", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01005", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01006", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01007", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01008", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01009", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01010", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01011", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01012", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01013", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01014", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01015", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01016", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01017", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN01018", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),

      new Test("HN02001", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02002", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02003", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02004", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02005", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02006", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02007", "HomeNet-max-identities-1.conf", new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02008", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02009", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02010", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02011", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02012", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02013", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02014", "HomeNet-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02015", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02016", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02017", "HomeNet-different-ports.conf",  new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02018", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02019", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02020", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02021", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02022", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02023", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02024", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02025", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02026", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN02027", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),

      new Test("HN03001", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN03002", "HomeNet-different-ports.conf",  new string[] { "127.0.0.1", "16989" }, false),
      new Test("HN03003", "HomeNet-different-ports.conf",  new string[] { "127.0.0.1", "16989" }, false),
      new Test("HN03004", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN03005", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN03006", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),

      new Test("HN04001", "HomeNet-different-ports.conf",  new string[] { "127.0.0.1", "16988", "16989" }, false),
      new Test("HN04002", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN04003", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04004", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04005", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04006", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04007", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04008", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988" }, false),
      new Test("HN04009", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04010", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04011", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04012", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04013", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04014", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),
      new Test("HN04015", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16988", "16988" }, false),

      new Test("HN05001", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05002", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05003", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05004", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05005", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("HN05006", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05007", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05008", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05009", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05010", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05011", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05012", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05013", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05014", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05015", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05016", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05017", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05018", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05019", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05020", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, true),
      new Test("HN05021", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05022", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05023", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05024", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN05025", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),

      new Test("HN06001", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN06002", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN06003", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN06004", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),

      new Test("HN07001", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN07002", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
      new Test("HN07003", "HomeNet-default.conf",          new string[] { "127.0.0.1", "16987" }, false),
    };


    /// <summary>Event that is set when the profile server is ready for the test.</summary>
    public static ManualResetEvent NodeReadyEvent = new ManualResetEvent(false);

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
          log.Error("Usage: HomeNetProtocolTestsExecutor [<TestFirst> <TestLast>] [EnableLongTime]");
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
        File.Copy(Path.Combine("HomeNet-binaries", "HomeNet-empty.db"), Path.Combine("HomeNet-binaries", "HomeNet.db"), true);
        File.Copy(Path.Combine("configs", test.Conf), Path.Combine("HomeNet-binaries", "HomeNet.conf"), true);

        NodeReadyEvent.Reset();
        TestPassed = false;
        TestFailed = false;

        Process nodeProcess = RunNode(Path.Combine("HomeNet-binaries", "HomeNet"));
        if (nodeProcess != null)
        {
          log.Trace("Waiting for node to start ...");
          if (NodeReadyEvent.WaitOne(20 * 1000))
          {
            log.Trace("Node ready!");

            Process testProcess = RunTest(Path.Combine("tests-binaries", "HomeNetProtocolTests"), test.Name, test.Args);
            int maxTime = test.LongTime ? 10 * 60 * 1000 : 2 * 60 * 1000;
            if (testProcess.WaitForExit(maxTime))
            {
              log.Trace("Sending enter to node.");
              string inputData = Environment.NewLine;
              using (StreamWriter sw = new StreamWriter(nodeProcess.StandardInput.BaseStream, Encoding.UTF8))
              {
                sw.Write(inputData);
              }

              log.Trace("Waiting for node process to stop.");
              if (nodeProcess.WaitForExit(10 * 1000))
              {
                log.Trace("Node process stopped.");
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
                log.Error("Node process did not finish on time, killing it now.");
                KillProcess(nodeProcess);
                break;
              }
            }
            else
            {
              log.Error("Test process did not finish on time, killing it now.");
              KillProcess(testProcess);
              break;
            }
          }
          else
          {
            log.Error("Node failed to start on time.");
            break;
          }
        }
        else
        {
          log.Error("Unable to start node.");
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
    public static Process RunNode(string Executable)
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
        process.OutputDataReceived += new DataReceivedEventHandler(NodeProcessOutputHandler);
        process.ErrorDataReceived += new DataReceivedEventHandler(NodeProcessOutputHandler);

        process.Start();
        processIsRunning = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred during starting: {0}\n", e.ToString());
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
          log.Error("Exception occurred after start: {0}\n", e.ToString());
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
    public static void NodeProcessOutputHandler(object SendingProcess, DataReceivedEventArgs OutLine)
    {
      if (OutLine.Data != null)
        NodeProcessNewOutput(OutLine.Data + Environment.NewLine);
    }

    /// <summary>
    /// Simple analyzer of the profile server process standard output, 
    /// that can recognize when the server is fully initialized and ready for the test.
    /// </summary>
    /// <param name="Data">Line of output.</param>
    public static void NodeProcessNewOutput(string Data)
    {
      log.Trace("(Data.Length:{0})", Data.Length);
      log.Trace("Data: {0}", Data);

      if (Data.Contains("ENTER"))
        NodeReadyEvent.Set();

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
        log.Error("Exception occurred when trying to kill process: {0}\n", e.ToString());
      }
      log.Info("(-)");
    }


    /// <summary>
    /// Runs a single specific test.
    /// </summary>
    /// <param name="Executable">HomeNetProtocolTests executable file name.</param>
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
        log.Error("Exception occurred during starting: {0}\n", e.ToString());
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
          log.Error("Exception occurred after start: {0}\n", e.ToString());
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
