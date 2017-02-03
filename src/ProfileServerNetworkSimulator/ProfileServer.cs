using Google.Protobuf;
using Iop.Profileserver;
using ProfileServerCrypto;
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

namespace ProfileServerNetworkSimulator
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

    /// <summary>Port of LOC server.</summary>
    private int locPort;
    /// <summary>Port of LOC server.</summary>
    public int LocPort { get { return locPort; } }

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

    /// <summary>Associated LOC server.</summary>
    private LocServer locServer;
    /// <summary>Associated LOC server.</summary>
    public LocServer LocServer { get { return locServer; } }


    /// <summary>Lock object to protect access to some internal fields.</summary>
    private object internalLock = new object();

    /// <summary>Network ID of the profile server, or null if it has not been initialized yet.</summary>
    private byte[] networkId = null;

    /// <summary>
    /// Profile server is initialized if it registered with its associated LOC server and filled in its network ID.
    /// If it deregisters with its LOC server, it is set to false again, but its network ID remains.
    /// </summary>
    private bool initialized = false;

    /// <summary>Node location in LOC.</summary>
    private Iop.Locnet.GpsLocation nodeLocation;
    /// <summary>Node location in LOC.</summary>
    public Iop.Locnet.GpsLocation NodeLocation { get { return nodeLocation; } }

    /// <summary>List of profile servers for which this server acts as a neighbor, that are to be informed once this server is initialized.</summary>
    private HashSet<ProfileServer> initializationNeighborhoodNotificationList;

    /// <summary>
    /// Creates a new instance of a profile server.
    /// </summary>
    /// <param name="Name">Unique profile server instance name.</param>
    /// <param name="Location">GPS location of this profile server instance.</param>
    /// <param name="Port">Base TCP port that defines the range of ports that are going to be used by this profile server instance and its related servers.</param>
    public ProfileServer(string Name, GpsLocation Location, int Port)
    {
      log = new PrefixLogger("ProfileServerSimulator.ProfileServer", "[" + Name + "] ");
      log.Trace("(Name:'{0}',Location:{1},Port:{2})", Name, Location, Port);

      this.name = Name;
      this.location = Location;
      basePort = Port;
      ipAddress = IPAddress.Parse("127.0.0.1");

      locPort = basePort;
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
    /// Returns instance directory for the profile server instance.
    /// </summary>
    /// <returns>Instance directory for the profile server instance.</returns>
    public string GetInstanceDirectoryName()
    {
      return GetInstanceDirectoryName(name);
    }

    /// <summary>
    /// Returns instance directory for the profile server instance.
    /// </summary>
    /// <param name="InstanceName">Name of the profile server instance.</param>
    /// <returns>Instance directory for the profile server instance.</returns>
    public static string GetInstanceDirectoryName(string InstanceName)
    {
      return Path.Combine(CommandProcessor.InstanceDirectory, "Ps-" + InstanceName);
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
        instanceDirectory = GetInstanceDirectoryName();
        Directory.CreateDirectory(instanceDirectory);

        if (Helpers.DirectoryCopy(CommandProcessor.ProfileServerBinariesDirectory, instanceDirectory))
        {
          string configFinal = Path.Combine(instanceDirectory, ConfigFileName);
          if (InitializeConfig(configFinal))
          {
            locServer = new LocServer(this);
            res = locServer.Start();
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
    /// Creates a final configuration file for the instance.
    /// </summary>
    /// <param name="FinalConfigFile">Name of the final configuration file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool InitializeConfig(string FinalConfigFile)
    {
      log.Trace("(FinalConfigFile:'{0}')", FinalConfigFile);

      bool res = false;

      try
      {
        string config = "test_mode = on\n"
          + "server_interface = 127.0.0.1\n"
          + "primary_interface_port = " + primaryInterfacePort.ToString() + "\n"
          + "server_neighbor_interface_port = " + serverNeighborInterfacePort.ToString() + "\n"
          + "client_non_customer_interface_port = " + clientNonCustomerInterfacePort.ToString() + "\n"
          + "client_customer_interface_port = " + clientCustomerInterfacePort.ToString() + "\n"
          + "client_app_service_interface_port = " + clientAppServiceInterfacePort.ToString() + "\n"
          + "tls_server_certificate = ProfileServer.pfx\n"
          + "image_data_folder = images\n"
          + "tmp_data_folder = tmp\n"
          + "max_hosted_identities = 10000\n"
          + "max_identity_relations = 100\n"
          + "neighborhood_initialization_parallelism = 10\n"
          + "loc_port = " + locPort.ToString() + "\n"
          + "neighbor_profiles_expiration_time = 86400\n"
          + "max_neighborhood_size = 105\n"
          + "max_follower_servers_count = 200\n"
          + "follower_refresh_time = 43200\n"
          + "can_api_port = 15001\n";

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

      locServer.Shutdown();
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
        if (serverProcessInitializationCompleteEvent.WaitOne(60 * 1000))
        {
          log.Trace("Waiting for profile server to initialize with its LOC server ...");
          int counter = 45;
          while (!IsInitialized() && (counter > 0))
          {
            Thread.Sleep(1000);
          }
          res = counter > 0;
        }
        else log.Error("Instance process failed to start on time.");

        if (!res) StopProcess();
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Stops profile server instance if it is running.
    /// </summary>
    /// <returns>true if the server was running and it was stopped, false otherwise.</returns>
    public bool Stop()
    {
      log.Trace("()");
      bool res = false;

      if (runningProcess != null)
      {
        log.Trace("Instance process is running, stopping it now.");
        if (StopProcess())
        {
          Uninitialize();
          res = true;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether the profile server process is running.
    /// </summary>
    /// <returns>true if the profile server process is running.</returns>
    public bool IsRunningProcess()
    {
      return runningProcess != null;
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

        if (runningProcess.WaitForExit(20 * 1000))
        {
          res = true;
        }
        else
        {
          log.Error("Instance did not finish on time, killing it now.");
          res = Helpers.KillProcess(runningProcess);
        }

        if (res)
        {
          serverProcessInitializationCompleteEvent.Reset();
          runningProcess = null;
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
        process.StartInfo.FileName = Path.Combine(instanceDirectory, ExecutableFileName);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WorkingDirectory = instanceDirectory;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;

        log.Trace("Starting command line: '{0}'", process.StartInfo.FileName);

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += new DataReceivedEventHandler(ProcessOutputHandler);
        process.ErrorDataReceived += new DataReceivedEventHandler(ProcessOutputHandler);

        if (process.Start())
        {
          processIsRunning = true;
        }
        else
        {
          log.Error("New process was not started.");
          error = true;
        }
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
      log.Trace("(-):{0}", res != null ? "Process" : "null");
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
    /// Adds a new identity client to the profile servers identity client list when initializing from snapshot.
    /// </summary>
    /// <param name="Client">Identity client that the profile server is going to host.</param>
    public void AddIdentityClientSnapshot(IdentityClient Client)
    {
      hostedIdentities.Add(Client);
    }


    /// <summary>
    /// Sets profile server's network identifier.
    /// </summary>
    /// <param name="NetworkId">Profile server's network identifier.</param>
    public void SetNetworkId(byte[] NetworkId)
    {
      log.Trace("(NetworkId:'{0}')", Crypto.ToHex(NetworkId));

      List<ProfileServer> serversToNotify = null;
      lock (internalLock)
      {
        networkId = NetworkId;
        initialized = true;

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
          ps.LocServer.AddNeighborhood(new List<ProfileServer>() { this });
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Sets the profile server's to uninitialized state.
    /// </summary>
    public void Uninitialize()
    {
      log.Trace("()");

      lock (internalLock)
      {
        initialized = false;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Obtains server's network identifier.
    /// </summary>
    /// <returns>Profile server's network ID.</returns>
    public byte[] GetNetworkId()
    {
      log.Trace("()");

      byte[] res = null;

      lock (internalLock)
      {
        res = networkId;
      }

      log.Trace("(-):{0}", res != null ? Crypto.ToHex(res) : "null");
      return res;
    }

    /// <summary>
    /// Obtains information whether the profile server has been initialized already.
    /// Initialization means the server has been started and announced its profile to its LOC server.
    /// </summary>
    /// <returns>true if the server is initialized and its profile is known.</returns>
    public bool IsInitialized()
    {
      log.Trace("()");

      bool res = false;

      lock (internalLock)
      {
        res = initialized;
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Acquires internal lock of the profile server.
    /// </summary>
    public void Lock()
    {
      log.Trace("()");

      Monitor.Enter(internalLock);

      log.Trace("(-)");
    }

    /// <summary>
    /// Releases internal lock of the profile server.
    /// </summary>
    public void Unlock()
    {
      log.Trace("()");

      Monitor.Exit(internalLock);

      log.Trace("(-)");
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
          NodeId = ProtocolHelper.ByteArrayToByteString(new byte[0]),
          Contact = new Iop.Locnet.NodeContact()
          {
            IpAddress = ProtocolHelper.ByteArrayToByteString(ipAddress.GetAddressBytes()),
            ClientPort = (uint)locPort,
            NodePort = (uint)locPort
          },
          Location = nodeLocation,
        };

        Iop.Locnet.ServiceInfo serviceInfo = new Iop.Locnet.ServiceInfo()
        {
          Type = Iop.Locnet.ServiceType.Profile,
          Port = (uint)primaryInterfacePort,
          ServiceData = ProtocolHelper.ByteArrayToByteString(networkId)
        };
        res.Services.Add(serviceInfo);
      }

      log.Trace("(-)");
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

    /// <summary>
    /// Checks log files of profile server instance to see if there are any errors.
    /// </summary>
    /// <param name="LogDirectory">If the function succeeds, this is filled with the name of the log directory of the profile server instance.</param>
    /// <param name="FileNames">If the function succeeds, this is filled with log file names.</param>
    /// <param name="ErrorCount">If the function succeeds, this is filled with number of errors found in log files.</param>
    /// <param name="WarningCount">If the function succeeds, this is filled with number of warnings found in log files.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool CheckLogs(out string LogDirectory, out List<string> FileNames, out List<int> ErrorCount, out List<int> WarningCount)
    {
      log.Trace("()");

      FileNames = null;
      ErrorCount = null;
      WarningCount = null;
      LogDirectory = null;

      List<string> fileNames = new List<string>();
      List<int> errorCount = new List<int>();
      List<int> warningCount = new List<int>();
      string logDirectory = Path.Combine(instanceDirectory, "Logs");
      bool error = false;
      try
      {        
        string[] files = Directory.GetFiles(logDirectory, "*.txt", SearchOption.TopDirectoryOnly);
        foreach (string file in files)
        {
          int errCount = 0;
          int warnCount = 0;
          if (CheckLogFile(file, out errCount, out warnCount))
          {
            fileNames.Add(Path.GetFileName(file));
            errorCount.Add(errCount);
            warningCount.Add(warnCount);
          }
          else
          {
            error = true;
            break;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Unable to analyze logs, exception occurred: {0}", e.ToString());
      }

      bool res = !error;
      if (res)
      {
        FileNames = fileNames;
        ErrorCount = errorCount;
        WarningCount = warningCount;
        LogDirectory = instanceDirectory;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks a signle log file of profile server instance to see if there are any errors.
    /// </summary>
    /// <param name="FileName">Name of the log file to check.</param>
    /// <param name="ErrorCount">If the function succeeds, this is filled with number of errors found in the log file.</param>
    /// <param name="WarningCount">If the function succeeds, this is filled with number of warnings found in the log file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool CheckLogFile(string FileName, out int ErrorCount, out int WarningCount)
    {
      log.Trace("(FileName:'{0}')", FileName);

      bool res = false;
      ErrorCount = 0;
      WarningCount = 0;
      try
      {
        int errors = 0;
        int warnings = 0;
        string[] lines = File.ReadAllLines(FileName);
        for (int i = 0; i < lines.Length; i++)
        {
          string line = lines[i];
          if (line.Contains("] ERROR:") && (!line.Contains("Failed to refresh profile server's IPNS record")))
            errors++;

          if (line.Contains("] WARN:") && (!line.Contains("WARN: ProfileServer.Utils.DbLogger.Log Sensitive data logging is enabled")))
            warnings++;
        }

        ErrorCount = errors;
        WarningCount = warnings;
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Unable to analyze logs, exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0},ErrorCount={1},WarningCount={1}", res, ErrorCount, WarningCount);
      return res;
    }


    /// <summary>
    /// Calculates expected search query results from the given profile server and its neighbors.
    /// </summary>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="IncludeHostedOnly">If set to true, the search results should only include profiles hosted on the queried profile server.</param>
    /// <param name="IncludeImages">If set to true, the search results should include images.</param>
    /// <param name="ExpectedCoveredServers">If the function succeeds, this is filled with list of covered servers that the search query should return.</param>
    /// <param name="LocalServerResultsCount">If the function succeeds, this is filled with the number of search results obtained from the local server.</param>
    /// <returns>List of profiles that match the given criteria or null if the function fails.</returns>
    public List<IdentityNetworkProfileInformation> GetExpectedSearchResults(string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius, bool IncludeHostedOnly, bool IncludeImages, out List<byte[]> ExpectedCoveredServers, out int LocalServerResultsCount)
    {
      log.Trace("(NameFilter:'{0}',TypeFilter:'{1}',LocationFilter:'{2}',Radius:{3},IncludeHostedOnly:{4},IncludeImages:{5})", NameFilter, TypeFilter, LocationFilter, Radius, IncludeHostedOnly, IncludeImages);

      List<IdentityNetworkProfileInformation> res = new List<IdentityNetworkProfileInformation>();
      ExpectedCoveredServers = new List<byte[]>();
      ExpectedCoveredServers.Add(networkId);

      List<IdentityNetworkProfileInformation> localResults = SearchQuery(NameFilter, TypeFilter, LocationFilter, Radius, IncludeImages);
      LocalServerResultsCount = localResults.Count;

      foreach (IdentityNetworkProfileInformation localResult in localResults)
      {
        localResult.IsHosted = true;
        localResult.IsOnline = false;
      }

      res.AddRange(localResults);

      if (!IncludeHostedOnly)
      {
        List<ProfileServer> neighbors = LocServer.GetNeighbors();
        foreach (ProfileServer neighbor in neighbors)
        {
          ByteString neighborId = ProtocolHelper.ByteArrayToByteString(neighbor.GetNetworkId());
          ExpectedCoveredServers.Add(neighborId.ToByteArray());
          List<IdentityNetworkProfileInformation> neighborResults = neighbor.SearchQuery(NameFilter, TypeFilter, LocationFilter, Radius, IncludeImages);
          foreach (IdentityNetworkProfileInformation neighborResult in neighborResults)
          {
            neighborResult.IsHosted = false;
            neighborResult.HostingServerNetworkId = neighborId;
          }

          res.AddRange(neighborResults);
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Performs a search query on the profile server's hosted identities.
    /// </summary>
    /// <param name="NameFilter">Name filter of the search query, or null if name filtering is not required.</param>
    /// <param name="TypeFilter">Type filter of the search query, or null if type filtering is not required.</param>
    /// <param name="LocationFilter">Location filter of the search query, or null if location filtering is not required.</param>
    /// <param name="Radius">If <paramref name="LocationFilter"/> is not null, this is the radius of the target area.</param>
    /// <param name="IncludeImages">If set to true, the search results should include images.</param>
    /// <returns>List of hosted profiles that match the given criteria.</returns>
    public List<IdentityNetworkProfileInformation> SearchQuery(string NameFilter, string TypeFilter, GpsLocation LocationFilter, int Radius, bool IncludeImages)
    {
      log.Trace("(NameFilter:'{0}',TypeFilter:'{1}',LocationFilter:'{2}',Radius:{3},IncludeImages:{4})", NameFilter, TypeFilter, LocationFilter, Radius, IncludeImages);

      List<IdentityNetworkProfileInformation> res = new List<IdentityNetworkProfileInformation>();

      foreach (IdentityClient client in hostedIdentities)
      {
        if (client.MatchesSearchQuery(NameFilter, TypeFilter, LocationFilter, Radius))
        {
          IdentityNetworkProfileInformation info = client.GetIdentityNetworkProfileInformation(IncludeImages);
          res.Add(info);
        }
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Creates profile server's snapshot.
    /// </summary>
    /// <returns>Profile server's snapshot.</returns>
    public ProfileServerSnapshot CreateSnapshot()
    {
      ProfileServerSnapshot res = new ProfileServerSnapshot()
      {
        AvailableIdentitySlots = this.availableIdentitySlots,
        BasePort = this.basePort,
        ClientAppServiceInterfacePort = this.clientAppServiceInterfacePort,
        ClientCustomerInterfacePort = this.clientCustomerInterfacePort,
        ClientNonCustomerInterfacePort = this.clientNonCustomerInterfacePort,
        HostedIdentities = this.hostedIdentities.Select(i => i.Name).ToList(),
        IpAddress = this.ipAddress.ToString(),
        IsRunning = false,
        LocPort = this.locPort,
        LocServer = this.locServer.CreateSnapshot(),
        LocationLatitude = this.location.Latitude,
        LocationLongitude = this.location.Longitude,
        Name = this.name,
        NetworkId = Crypto.ToHex(this.networkId),
        PrimaryInterfacePort = this.primaryInterfacePort,
        ServerNeighborInterfacePort = this.serverNeighborInterfacePort        
      };
      return res;
    }


    /// <summary>
    /// Creates instance of profile server from snapshot.
    /// </summary>
    /// <param name="Snapshot">Profile server snapshot.</param>
    /// <returns>New profile server instance.</returns>
    public static ProfileServer CreateFromSnapshot(ProfileServerSnapshot Snapshot)
    {
      ProfileServer res = new ProfileServer(Snapshot.Name, new GpsLocation(Snapshot.LocationLatitude, Snapshot.LocationLongitude), Snapshot.BasePort);

      res.availableIdentitySlots = Snapshot.AvailableIdentitySlots;
      res.clientAppServiceInterfacePort = Snapshot.ClientAppServiceInterfacePort;
      res.clientCustomerInterfacePort = Snapshot.ClientCustomerInterfacePort;
      res.clientNonCustomerInterfacePort = Snapshot.ClientNonCustomerInterfacePort;
      res.ipAddress = IPAddress.Parse(Snapshot.IpAddress);
      res.locPort = Snapshot.LocPort;
      res.networkId = Crypto.FromHex(Snapshot.NetworkId);
      res.primaryInterfacePort = Snapshot.PrimaryInterfacePort;
      res.serverNeighborInterfacePort = Snapshot.ServerNeighborInterfacePort;
      res.instanceDirectory = res.GetInstanceDirectoryName();
      res.locServer = new LocServer(res);

      byte[] ipBytes = res.ipAddress.GetAddressBytes();
      Iop.Locnet.NodeContact contact = new Iop.Locnet.NodeContact()
      {
        IpAddress = ProtocolHelper.ByteArrayToByteString(ipBytes),
        ClientPort = (uint)res.locPort,
        NodePort = (uint)res.locPort
      };

      return res;
    }
  }
}
