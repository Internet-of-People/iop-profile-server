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
    private PrefixLogger log;

    /// <summary>Maximal number of identities that a single profile server can host.</summary>
    public const int MaxHostedIdentities = 20000;

    /// <summary>Name of the configuration template file.</summary>
    public const string ConfigFileTemplateName = "ProfileServer-template.conf";

    /// <summary>Name of the final configuration file.</summary>
    public const string ConfigFileName = "ProfileServer.conf";

    /// <summary>Name of the final configuration file.</summary>
    public const string ExecutableFileName = "ProfileServer";

    /// <summary>Name of the directory containing files of this profile server instance.</summary>
    private string instanceDirectory;

    /// <summary>Name of the profile server instance.</summary>
    private string name;
    /// <summary>Name of the profile server instance.</summary>
    public string Name { get { return name; } }

    /// <summary>GPS location of the server.</summary>
    private GpsLocation location;

    /// <summary>IP address of the interface on which the server is listening.</summary>
    private IPAddress ipAddress;
    /// <summary>IP address of the interface on which the server is listening.</summary>
    public IPAddress IpAddress { get { return ipAddress; } }

    /// <summary>Base TCP port of the instance, which can use ports between Port and Port + 19.</summary>
    private int basePort;

    /// <summary>Port of LBN server.</summary>
    private int lbnPort;
    /// <summary>Port of LBN server.</summary>
    public int LbnPort { get { return lbnPort; } }

    /// <summary>Port of profile server primary interface.</summary>
    private int primaryInterfacePort;

    /// <summary>Port of profile server neighbors interface.</summary>
    private int serverNeighborInterfacePort;

    /// <summary>Port of profile server non-customer interface.</summary>
    private int clientNonCustomerInterfacePort;
    /// <summary>Port of profile server non-customer interface.</summary>
    public int ClientNonCustomerInterfacePort { get { return clientNonCustomerInterfacePort; } }

    /// <summary>Port of profile server customer interface.</summary>
    private int clientCustomerInterfacePort;
    /// <summary>Port of profile server customer interface.</summary>
    public int ClientCustomerInterfacePort { get { return clientCustomerInterfacePort; } }

    /// <summary>Port of profile server application service interface.</summary>
    private int clientAppServiceInterfacePort;

    /// <summary>System process of the running instance.</summary>
    private Process runningProcess;

    /// <summary>Event that is set when the profile server instance process is fully initialized.</summary>
    private ManualResetEvent serverProcessInitializationCompleteEvent = new ManualResetEvent(false);

    /// <summary>Number of free slots for identities.</summary>
    private int availableIdentitySlots;
    /// <summary>Number of free slots for identities.</summary>
    public int AvailableIdentitySlots { get { return availableIdentitySlots; } }

    /// <summary>List of hosted customer identities.</summary>
    private List<IdentityClient> hostedIdentities;

    /// <summary>Associated LBN server.</summary>
    private LbnServer lbnServer;
    /// <summary>Associated LBN server.</summary>
    public LbnServer LbnServer { get { return lbnServer; } }


    /// <summary>Lock object to protect access to some internal fields.</summary>
    private object internalLock = new object();

    /// <summary>Node profile in LBN.</summary>
    private Iop.Locnet.NodeProfile nodeProfile;

    /// <summary>Node location in LBN.</summary>
    private Iop.Locnet.GpsLocation nodeLocation;

    /// <summary>List of profile servers for which this server acts as a neighbor, that are to be informed once this server is initialized.</summary>
    private HashSet<ProfileServer> initializationNeighborhoodNotificationList;

    /// <summary>
    /// Creates a new instance of a profile server.
    /// </summary>
    public ProfileServer(string Name, GpsLocation Location, int Port)
    {
      log = new PrefixLogger("ProfileServerSimulator.ProfileServer", "[" + Name + "] ");
      log.Trace("(Name:'{0}',Location:{1},Port:{2})", Name, Location, Port);

      this.name = Name;
      this.location = Location;
      basePort = Port;
      ipAddress = IPAddress.Parse("127.0.0.1");

      lbnPort = basePort;
      primaryInterfacePort = basePort + 1;
      serverNeighborInterfacePort = basePort + 2;
      clientNonCustomerInterfacePort = basePort + 3;
      clientCustomerInterfacePort = basePort + 4;
      clientAppServiceInterfacePort = basePort + 5;

      availableIdentitySlots = MaxHostedIdentities;
      hostedIdentities = new List<IdentityClient>();

      nodeLocation = new Iop.Locnet.GpsLocation()
      {
        Latitude = Location.GetLocationTypeLatitude(),
        Longitude = Location.GetLocationTypeLongitude()
      };

      initializationNeighborhoodNotificationList = new HashSet<ProfileServer>();

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
        instanceDirectory = Path.Combine(CommandProcessor.InstanceDirectory, "Ps-" + name);
        Directory.CreateDirectory(instanceDirectory);

        if (Helpers.DirectoryCopy(CommandProcessor.ProfileServerBinariesDirectory, instanceDirectory))
        {
          string configTemplate = Path.Combine(new string[] { CommandProcessor.BaseDirectory, CommandProcessor.ProfileServerBinariesDirectory, ConfigFileTemplateName });
          string configFinal = Path.Combine(instanceDirectory, ConfigFileName);
          if (InitializeConfig(configTemplate, configFinal))
          {
            lbnServer = new LbnServer(this);
            res = lbnServer.Start();
          }
          else log.Error("Unable to initialize configuration file '{0}' for server '{1}'.", configFinal, name);
        }
        else log.Error("Unable to copy files from directory '{0}' to '{1}'.", CommandProcessor.ProfileServerBinariesDirectory, instanceDirectory);
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

        config = config.Replace("$test_mode", "on");
        config = config.Replace("$primary_interface_port", primaryInterfacePort.ToString());
        config = config.Replace("$server_neighbor_interface_port", serverNeighborInterfacePort.ToString());
        config = config.Replace("$client_non_customer_interface_port", clientNonCustomerInterfacePort.ToString());
        config = config.Replace("$client_customer_interface_port", clientCustomerInterfacePort.ToString());
        config = config.Replace("$client_app_service_interface_port", clientAppServiceInterfacePort.ToString());
        config = config.Replace("$max_hosted_identities", "10000");
        config = config.Replace("$max_identity_relations", "100");
        config = config.Replace("$neighborhood_initialization_parallelism", "10");
        config = config.Replace("$lbn_port", lbnPort.ToString());
        config = config.Replace("$max_neighborhood_size", "105");
        config = config.Replace("$max_follower_servers_count", "200");
        config = config.Replace("$follower_refresh_time", "43200");

        File.WriteAllText(FinalConfigFile, config);
        res = true;
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

      lbnServer.Shutdown();
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

      runningProcess = RunProcess();
      if (runningProcess != null)
      {
        log.Trace("Waiting for profile server to start ...");
        if (serverProcessInitializationCompleteEvent.WaitOne(20 * 1000))
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

      if (runningProcess != null)
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
        using (StreamWriter sw = new StreamWriter(runningProcess.StandardInput.BaseStream, Encoding.UTF8))
        {
          sw.Write(inputData);
        }

        if (!runningProcess.WaitForExit(10 * 1000))
        {
          log.Error("Instance did not finish on time, killing it now.");
          res = Helpers.KillProcess(runningProcess);
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
        process.StartInfo.FileName = Path.Combine(instanceDirectory, ExecutableFileName);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WorkingDirectory = instanceDirectory;// Path.GetDirectoryName(fullFileName);
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
        serverProcessInitializationCompleteEvent.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Adds a new identity client to the profile servers identity client list.
    /// </summary>
    /// <param name="Client">Identity client that the profile server is going to host.</param>
    public void AddIdentityClient(IdentityClient Client)
    {
      log.Trace("(Client.Name:'{0}')", Client.Name);

      hostedIdentities.Add(Client);
      availableIdentitySlots--;

      log.Trace("(-)");
    }


    /// <summary>
    /// Sets profile server's node profile.
    /// </summary>
    /// <param name="NodeProfile">Node profile to set.</param>
    public void SetNodeProfile(Iop.Locnet.NodeProfile NodeProfile)
    {
      log.Trace("()");

      List<ProfileServer> serversToNotify = null;
      lock (internalLock)
      {
        nodeProfile = NodeProfile;
        if (initializationNeighborhoodNotificationList.Count != 0)
        {
          serversToNotify = initializationNeighborhoodNotificationList.ToList();
          initializationNeighborhoodNotificationList.Clear();
        }
      }

      if (serversToNotify != null)
      {
        log.Debug("Sending neighborhood notification to {0} profile servers.", serversToNotify.Count);
        foreach (ProfileServer ps in serversToNotify)
          ps.LbnServer.AddNeighborhood(new List<ProfileServer>() { this });
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Removes profile server's node profile.
    /// </summary>
    public void RemoveNodeProfile()
    {
      log.Trace("()");

      lock (internalLock)
      {
        nodeProfile = null;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Obtains server's network identifier.
    /// </summary>
    /// <returns>Profile server's network ID.</returns>
    public Google.Protobuf.ByteString GetNetworkId()
    {
      log.Trace("()");

      Google.Protobuf.ByteString res = null;

      lock (internalLock)
      {
        if (nodeProfile != null)
          res = nodeProfile.NodeId;
      }

      log.Trace("(-):{0}", res != null ? ProfileServerCrypto.Crypto.ToHex(res.ToByteArray()) : "null");
      return res;
    }

    /// <summary>
    /// Obtains information whether the profile server has been initialized already.
    /// Initialization means the server has been started and announced its profile to its LBN server.
    /// </summary>
    /// <returns>true if the server is initialized and its profile is known.</returns>
    public bool IsInitialized()
    {
      log.Trace("()");

      bool res = false;

      lock (internalLock)
      {
        res = nodeProfile != null;
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Returns NodeInfo structure of the profile server.
    /// </summary>
    /// <returns>NodeInfo structure of the profile server.</returns>
    public Iop.Locnet.NodeInfo GetNodeInfo()
    {
      log.Trace("()");

      Iop.Locnet.NodeInfo res = null;
      lock (internalLock)
      {
        res = new Iop.Locnet.NodeInfo()
        {
          Profile = this.nodeProfile,
          Location = this.nodeLocation
        };
      }

      log.Trace("(-):{0}", res != null ? "NodeInfo" : "null");
      return res;
    }

    /// <summary>
    /// Installs a notification to sent to the profile server, for which this profile server acts as a neighbor.
    /// The notification will be sent as soon as this profile server starts and performs its profile initialization.
    /// </summary>
    /// <param name="ServerToInform">Profile server to inform.</param>
    public void InstallInitializationNeighborhoodNotification(ProfileServer ServerToInform)
    {
      log.Trace("(ServerToInform.Name:'{0}')", ServerToInform.Name);

      lock (internalLock)
      {
        if (initializationNeighborhoodNotificationList.Add(ServerToInform)) log.Debug("Server '{0}' added to neighborhood notification list of server '{1}'.", ServerToInform.Name, Name);
        else log.Debug("Server '{0}' is already on neighborhood notification list of server '{1}'.", ServerToInform.Name, Name);
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Uninstalls a neighborhood notification.
    /// </summary>
    /// <param name="ServerToInform">Profile server that was about to be informed, but will not be anymore.</param>
    public void UninstallInitializationNeighborhoodNotification(ProfileServer ServerToInform)
    {
      log.Trace("(ServerToInform.Name:'{0}')", ServerToInform.Name);

      lock (internalLock)
      {
        if (initializationNeighborhoodNotificationList.Remove(ServerToInform)) log.Debug("Server '{0}' removed from neighborhood notification list of server '{1}'.", ServerToInform.Name, Name);
        else log.Debug("Server '{0}' not found on the neighborhood notification list of server '{1}' and can't be removed.", ServerToInform.Name, Name);
      }

      log.Trace("(-)");
    }
  }
}
