using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerSimulator
{
  /// <summary>
  /// Represents a single profile server. Provides abilities to start and stop a server process
  /// and holds information about the expected state of the server's database.
  /// </summary>
  public class ProfileServer
  {
    private static PrefixLogger log;

    /// <summary>Maximal number of identities that a single profile server can host.</summary>
    public const int MaxHostedIdentities = 20000;

    /// <summary>Name of the configuration template file.</summary>
    public const string ConfigFileTemplateName = "ProfileServer-template.conf";

    /// <summary>Name of the final configuration file.</summary>
    public const string ConfigFileName = "ProfileServer.conf";

    /// <summary>Name of the final configuration file.</summary>
    public const string ExecutableFileName = "ProfileServer";

    /// <summary>Configuration file</summary>
    public List<string> Configuration;

    /// <summary>Name of the directory containing files of this profile server instance.</summary>
    public string InstanceDirectory;

    /// <summary>Name of the profile server instance.</summary>
    public string Name;

    /// <summary>GPS location of the server.</summary>
    public GpsLocation Location;

    /// <summary>IP address of the interface on which the server is listening.</summary>
    public IPAddress IpAddress;

    /// <summary>Base TCP port of the instance, which can use ports between Port and Port + 19.</summary>
    public int BasePort;

    /// <summary>Port of LBN server.</summary>
    public int LbnPort;

    /// <summary>Port of profile server primary interface.</summary>
    public int PrimaryInterfacePort;

    /// <summary>Port of profile server neighbor interface.</summary>
    public int ServerNeighborInterfacePort;

    /// <summary>Port of profile server non-customer interface.</summary>
    public int ClientNonCustomerInterfacePort;

    /// <summary>Port of profile server customer interface.</summary>
    public int ClientCustomerInterfacePort;

    /// <summary>Port of profile server application service interface.</summary>
    public int ClientAppServiceInterfacePort;

    /// <summary>System process of the running instance.</summary>
    public Process RunningProcess;

    /// <summary>Event that is set when the profile server instance process is fully initialized.</summary>
    public ManualResetEvent ServerProcessInitializationCompleteEvent = new ManualResetEvent(false);

    /// <summary>Number of free slots for identities.</summary>
    public int AvailableIdentitySlots;

    /// <summary>List of hosted customer identities.</summary>
    public List<IdentityClient> HostedIdentities;

    /// <summary>Associated LBN server.</summary>
    public LbnServer LbnServer;


    /// <summary>Node profile in LBN.</summary>
    public Iop.Locnet.NodeProfile NodeProfile;

    /// <summary>Node location in LBN.</summary>
    public Iop.Locnet.GpsLocation NodeLocation;


    /// <summary>
    /// Creates a new instance of a profile server.
    /// </summary>
    public ProfileServer(string Name, GpsLocation Location, int Port)
    {
      log = new PrefixLogger("ProfileServerSimulator.ProfileServer", Name);
      log.Trace("(Name:'{0}',Location:{1},Port:{2})", Name, Location, Port);

      this.Name = Name;
      this.Location = Location;
      BasePort = Port;
      IpAddress = IPAddress.Parse("127.0.0.1");

      LbnPort = BasePort;
      PrimaryInterfacePort = BasePort + 1;
      ServerNeighborInterfacePort = BasePort + 2;
      ClientNonCustomerInterfacePort = BasePort + 3;
      ClientCustomerInterfacePort = BasePort + 4;
      ClientAppServiceInterfacePort = BasePort + 5;

      AvailableIdentitySlots = MaxHostedIdentities;
      HostedIdentities = new List<IdentityClient>();

      NodeLocation = new Iop.Locnet.GpsLocation()
      {
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude()
      };

      log.Trace("(-)");
    }


    /// <summary>
    /// Initialize a new instance of a profile server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Initialize()
    {
      log.Trace("()");

      bool res = false;

      try
      {
        InstanceDirectory = Path.Combine(CommandProcessor.InstanceDirectory, "Ps-" + Name);
        Directory.CreateDirectory(InstanceDirectory);

        if (Helpers.DirectoryCopy(CommandProcessor.ProfileServerBinariesDirectory, InstanceDirectory))
        {
          string configTemplate = Path.Combine(CommandProcessor.ProfileServerBinariesDirectory, ConfigFileTemplateName);
          string configFinal = Path.Combine(InstanceDirectory, ConfigFileName);
          if (InitializeConfig(configTemplate, configFinal))
          {
            LbnServer = new LbnServer(this);
            res = LbnServer.Start();
          }
          else log.Error("Unable to initialize configuration file '{0}' for server '{1}'.", configFinal, Name);
        }
        else log.Error("Unable to copy files from directory '{0}' to '{1}'.", CommandProcessor.ProfileServerBinariesDirectory, InstanceDirectory);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Creates a final configuration file from template configuration file.
    /// </summary>
    /// <param name="TemplateFile">Name of the template configuration file.</param>
    /// <param name="FinalConfigFile">Name of the final configuration file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool InitializeConfig(string TemplateFile, string FinalConfigFile)
    {
      log.Trace("(TemplateFile:'{0}')", TemplateFile);

      bool res = false;

      try
      {
        string config = File.ReadAllText(TemplateFile);

        config = config.Replace("$primary_interface_port", PrimaryInterfacePort.ToString());
        config = config.Replace("$server_neighbor_interface_port", ServerNeighborInterfacePort.ToString());
        config = config.Replace("$client_non_customer_interface_port", ClientNonCustomerInterfacePort.ToString());
        config = config.Replace("$client_customer_interface_port", ClientCustomerInterfacePort.ToString());
        config = config.Replace("$client_app_service_interface_port", ClientAppServiceInterfacePort.ToString());
        config = config.Replace("$lbn_port", LbnPort.ToString());

        File.WriteAllText(FinalConfigFile, config);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources used by the profile server.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      LbnServer.Shutdown();
      Stop();

      log.Trace("(-)");
    }

    /// <summary>
    /// Starts profile sever instance.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Start()
    {
      log.Trace("()");

      bool res = false;

      RunningProcess = RunProcess();
      if (RunningProcess != null)
      {
        log.Trace("Waiting for profile server to start ...");
        if (ServerProcessInitializationCompleteEvent.WaitOne(20 * 1000))
        {
          res = true;
        }
        else log.Error("Instance process failed to start on time.");

        if (!res) StopProcess();
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public bool Stop()
    {
      log.Trace("()");
      bool res = false;

      if (RunningProcess != null)
      {
        log.Trace("Instance process is running, stopping it now.");
        res = StopProcess();
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Stops running instance process.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool StopProcess()
    {
      log.Trace("()");

      bool res = false;

      try
      {
        log.Trace("Sending ENTER to instance process.");
        string inputData = Environment.NewLine;
        using (StreamWriter sw = new StreamWriter(RunningProcess.StandardInput.BaseStream, Encoding.UTF8))
        {
          sw.Write(inputData);
        }

        if (!RunningProcess.WaitForExit(10 * 1000))
        {
          log.Error("Instance did not finish on time, killing it now.");
          res = Helpers.KillProcess(RunningProcess);
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Runs the profile server instance.
    /// </summary>
    /// <returns>Running profile server process.</returns>
    public Process RunProcess()
    {
      log.Trace("()");

      bool error = false;
      Process process = null;
      bool processIsRunning = false;
      try
      {
        process = new Process();
        //string fullFileName = Path.GetFullPath(Executable);
        process.StartInfo.FileName = Path.Combine(InstanceDirectory, ExecutableFileName);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WorkingDirectory = InstanceDirectory;// Path.GetDirectoryName(fullFileName);
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

        log.Trace("Starting command line: '{0}'", process.StartInfo.FileName);

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += new DataReceivedEventHandler(ProcessOutputHandler);
        process.ErrorDataReceived += new DataReceivedEventHandler(ProcessOutputHandler);

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
          Helpers.KillProcess(process);
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
    public void ProcessOutputHandler(object SendingProcess, DataReceivedEventArgs OutLine)
    {
      if (OutLine.Data != null)
        ProcessNewOutput(OutLine.Data + Environment.NewLine);
    }

    /// <summary>
    /// Simple analyzer of the profile server process standard output, 
    /// that can recognize when the server is fully initialized and ready for the test.
    /// </summary>
    /// <param name="Data">Line of output.</param>
    public void ProcessNewOutput(string Data)
    {
      log.Trace("(Data.Length:{0})", Data.Length);
      log.Trace("Data: {0}", Data);

      if (Data.Contains("ENTER"))
        ServerProcessInitializationCompleteEvent.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Adds a new identity client to the profile servers identity client list.
    /// </summary>
    /// <param name="Client">Identity client that the profile server is going to host.</param>
    public void AddIdentityClient(IdentityClient Client)
    {
      log.Trace("(Identity.Name:'{0}')", Client.Name);

      HostedIdentities.Add(Client);
      AvailableIdentitySlots--;

      log.Trace("(-)");
    }
  }
}
