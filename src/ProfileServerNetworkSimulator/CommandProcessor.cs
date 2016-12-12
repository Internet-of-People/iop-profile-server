using Google.Protobuf;
using Iop.Profileserver;
using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerNetworkSimulator
{
  /// <summary>
  /// Engine that executes the commands.
  /// </summary>
  public class CommandProcessor
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerNetworkSimulator.CommandProcessor");

    /// <summary>Directory of the assembly.</summary>
    public static string BaseDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);

    /// <summary>Full path to the directory that contains files of running server instances.</summary>
    public static string InstanceDirectory = Path.Combine(BaseDirectory, "instances");

    /// <summary>Full path to the directory that contains original binaries.</summary>
    public static string BinariesDirectory = Path.Combine(BaseDirectory, "bin");

    /// <summary>Full path to the directory that contains images.</summary>
    public static string ImagesDirectory = Path.Combine(BaseDirectory, "images");

    /// <summary>Full path to the directory that contains original profile server files within binary directory.</summary>
    public static string ProfileServerBinariesDirectory = Path.Combine(BinariesDirectory, "ProfileServer");

    /// <summary>List of commands to execute.</summary>
    private List<Command> commands;

    /// <summary>List of profile server instances mapped by their name.</summary>
    private Dictionary<string, ProfileServer> profileServers = new Dictionary<string, ProfileServer>(StringComparer.Ordinal);

    /// <summary>List of identity client instances mapped by their name.</summary>
    private Dictionary<string, IdentityClient> identityClients = new Dictionary<string, IdentityClient>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes the object instance.
    /// </summary>
    /// <param name="Commands">List of commands to execute.</param>
    public CommandProcessor(List<Command> Commands)
    {
      log.Trace("()");

      this.commands = Commands;

      log.Trace("(-)");
    }

    /// <summary>
    /// Frees resources used by command processor.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      foreach (IdentityClient identity in identityClients.Values)
        identity.Shutdown();

      foreach (ProfileServer server in profileServers.Values)
        server.Shutdown();

      log.Trace("(-)");
    }

    /// <summary>
    /// Executes all commands.
    /// </summary>
    /// <returns>true if the function succeeds and all the commands are executed successfully, false otherwise.</returns>
    public bool Execute()
    {
      log.Trace("()");

      bool res = false;
      if (!ClearHistory())
      {
        log.Error("Unable to clear history, please make sure the access to the instance folder is not blocked by other process.");
        log.Trace("(-):{0}", res);
        return res;
      }

      int index = 1;
      bool error = false;
      foreach (Command command in commands)
      {
        log.Info("Executing #{0:0000}@l{1}: {2}", index, command.LineNumber, command.OriginalCommand);
        index++;

        switch (command.Type)
        {
          case CommandType.ProfileServer:
            {
              CommandProfileServer cmd = (CommandProfileServer)command;
              for (int i = 1; i <= cmd.Count; i++)
              {
                string name = GetInstanceName(cmd.GroupName, i);
                GpsLocation location = Helpers.GenerateRandomGpsLocation(cmd.Latitude, cmd.Longitude, cmd.Radius);
                int basePort = cmd.BasePort + 20 * (i - 1);
                ProfileServer profileServer = new ProfileServer(name, location, basePort);
                if (profileServer.Initialize())
                {
                  profileServers.Add(name, profileServer);
                }
                else
                {
                  log.Error("  * Initialization of profile server '{0}' failed.", profileServer.Name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} profile servers created.", cmd.Count);
              break;
            }

          case CommandType.StartServer:
            {
              CommandStartServer cmd = (CommandStartServer)command;
              for (int i = 0; i < cmd.PsCount; i++)
              {
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);
                ProfileServer profileServer;
                if (profileServers.TryGetValue(name, out profileServer))
                {
                  if (!profileServer.Start())
                  {
                    log.Error("  * Unable to start server instance '{0}'.", name);
                    error = true;
                    break;
                  }
                }
                else
                {
                  log.Error("  * Profile server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} profile servers started.", cmd.PsCount);
              break;
            }

          case CommandType.StopServer:
            {
              CommandStopServer cmd = (CommandStopServer)command;
              for (int i = 0; i < cmd.PsCount; i++)
              {
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);
                ProfileServer profileServer;
                if (profileServers.TryGetValue(name, out profileServer))
                {
                  if (!profileServer.Stop())
                  {
                    log.Error("  * Unable to stop server instance '{0}'.", name);
                    error = true;
                  }
                }
                else
                {
                  log.Error("  * Profile server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} profile servers stopped.", cmd.PsCount);
              break;
            }

          case CommandType.Identity:
            {
              CommandIdentity cmd = (CommandIdentity)command;

              List<ProfileServer> availableServers = new List<ProfileServer>();
              int availableSlots = 0;
              for (int i = 0; i < cmd.PsCount; i++)
              {
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);
                ProfileServer profileServer;
                if (profileServers.TryGetValue(name, out profileServer))
                {
                  availableServers.Add(profileServer);
                  availableSlots += profileServer.AvailableIdentitySlots;
                }
                else
                {
                  log.Error("  * Profile server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (error) break;


              if (availableSlots < cmd.Count)
              {
                log.Error("  * Total number of available identity slots in selected servers is {0}, but {1} slots are required.", availableSlots, cmd.Count);
                error = true;
                break;
              }


              for (int i = 1; i <= cmd.Count; i++)
              {
                string name = GetIdentityName(cmd.Name, i);

                int serverIndex = Helpers.Rng.Next(availableServers.Count);
                ProfileServer profileServer = availableServers[serverIndex];

                GpsLocation location = Helpers.GenerateRandomGpsLocation(cmd.Latitude, cmd.Longitude, cmd.Radius);
                IdentityClient identityClient = null;
                try
                {
                  identityClient = new IdentityClient(name, cmd.IdentityType, location, cmd.ImageMask, cmd.ImageChance);
                  identityClients.Add(name, identityClient);
                }
                catch
                {
                  log.Error("Unable to create identity '{0}'.", name);
                  error = true;
                  break;
                }

                Task<bool> initTask = identityClient.InitializeProfileHosting(profileServer);
                if (initTask.Result) 
                {
                  profileServer.AddIdentityClient(identityClient);
                  if (profileServer.AvailableIdentitySlots == 0)
                    availableServers.RemoveAt(serverIndex);
                }
                else
                {
                  log.Error("Unable to register profile hosting and initialize profile of identity '{0}' on server '{1}'.", name, profileServer.Name);
                  error = true;
                  break;
                }
              }

              if (!error) log.Info("  * {0} identities created and initialized on {1} servers.", cmd.Count, cmd.PsCount);
              break;
            }

          case CommandType.CancelIdentity:
            {
              CommandCancelIdentity cmd = (CommandCancelIdentity)command;

              List<IdentityClient> clients = new List<IdentityClient>();
              for (int i = 0; i < cmd.Count; i++)
              {
                string name = GetIdentityName(cmd.Name, cmd.Index + i);
                IdentityClient identityClient;
                if (identityClients.TryGetValue(name, out identityClient))
                {
                  clients.Add(identityClient);
                }
                else
                {
                  log.Error("  * Identity name '{0}' does not exist.", name);
                  error = true;
                  break;
                }
              }

              if (error) break;


              foreach (IdentityClient client in clients)
              {
                Task<bool> cancelTask = client.CancelProfileHosting();
                if (!cancelTask.Result)
                {
                  log.Error("Unable to cancel profile hosting agreement of identity '{0}' on server '{1}'.", client.Name, client.ProfileServer.Name);
                  error = true;
                }

              }

              if (!error) log.Info("  * {0} identities cancelled their hosting agreement.", clients.Count);

              break;
            }

          case CommandType.Neighborhood:
            {
              CommandNeighborhood cmd = (CommandNeighborhood)command;

              List<ProfileServer> neighborhoodList = new List<ProfileServer>();
              for (int i = 0; i < cmd.PsGroups.Count; i++)
              {
                string psGroup = cmd.PsGroups[i];
                int psCount = cmd.PsCounts[i];
                int psIndex = cmd.PsIndexes[i];
                for (int j = 0; j < psCount; j++)
                {
                  string name = GetInstanceName(psGroup, psIndex + j);

                  ProfileServer profileServer;
                  if (profileServers.TryGetValue(name, out profileServer))
                  {
                    neighborhoodList.Add(profileServer);
                  }
                  else
                  {
                    log.Error("  * Profile server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }
              }

              if (!error)
              {
                foreach (ProfileServer ps in neighborhoodList)
                {
                  if (!ps.LbnServer.AddNeighborhood(neighborhoodList))
                  {
                    log.Error("  * Unable to add neighbors to server '{0}'.", ps.Name);
                    error = true;
                    break;
                  }
                }

                if (!error)
                  log.Info("  * Neighborhood of {0} profile servers has been established.", neighborhoodList.Count);
              }
              break;
            }

          case CommandType.CancelNeighborhood:
            {
              CommandCancelNeighborhood cmd = (CommandCancelNeighborhood)command;

              List<ProfileServer> neighborhoodList = new List<ProfileServer>();
              for (int i = 0; i < cmd.PsGroups.Count; i++)
              {
                string psGroup = cmd.PsGroups[i];
                int psCount = cmd.PsCounts[i];
                int psIndex = cmd.PsIndexes[i];
                for (int j = 0; j < psCount; j++)
                {
                  string name = GetInstanceName(psGroup, psIndex + j);

                  ProfileServer profileServer;
                  if (profileServers.TryGetValue(name, out profileServer))
                  {
                    neighborhoodList.Add(profileServer);
                  }
                  else
                  {
                    log.Error("  * Profile server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }
              }

              if (!error)
              {
                foreach (ProfileServer ps in neighborhoodList)
                {
                  if (!ps.LbnServer.CancelNeighborhood(neighborhoodList))
                  {
                    log.Error("  * Unable to add neighbors to server '{0}'.", ps.Name);
                    error = true;
                    break;
                  }
                }

                if (!error)
                  log.Info("  * Neighbor relations among {0} profile servers have been cancelled.", neighborhoodList.Count);
              }
              break;
            }

          case CommandType.Neighbor:
            {
              CommandNeighbor cmd = (CommandNeighbor)command;

              ProfileServer profileServer;
              if (profileServers.TryGetValue(cmd.Source, out profileServer))
              {
                List<ProfileServer> neighborhoodList = new List<ProfileServer>();
                for (int i = 0; i < cmd.Targets.Count; i++)
                {
                  string name = cmd.Targets[i];
                  ProfileServer target;
                  if (profileServers.TryGetValue(name, out target))
                  {
                    neighborhoodList.Add(target);
                  }
                  else
                  {
                    log.Error("  * Profile server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }


                if (!error)
                {
                  if (profileServer.LbnServer.AddNeighborhood(neighborhoodList))
                  {
                    log.Info("  * {0} servers have been added to the neighborhood of server '{1}'.", neighborhoodList.Count, profileServer.Name);
                  }
                  else
                  {
                    log.Error("  * Unable to add neighbors to server '{0}'.", profileServer.Name);
                    error = true;
                    break;
                  }
                }
              }
              else
              {
                log.Error("  * Profile server instance '{0}' does not exist.", cmd.Source);
                error = true;
                break;
              }

              break;
            }

          case CommandType.CancelNeighbor:
            {
              CommandCancelNeighbor cmd = (CommandCancelNeighbor)command;

              ProfileServer profileServer;
              if (profileServers.TryGetValue(cmd.Source, out profileServer))
              {
                List<ProfileServer> neighborhoodList = new List<ProfileServer>();
                for (int i = 0; i < cmd.Targets.Count; i++)
                {
                  string name = cmd.Targets[i];
                  ProfileServer target;
                  if (profileServers.TryGetValue(name, out target))
                  {
                    neighborhoodList.Add(profileServer);
                  }
                  else
                  {
                    log.Error("  * Profile server instance '{0}' does not exist.", name);
                    error = true;
                    break;
                  }
                }


                if (!error)
                {
                  if (profileServer.LbnServer.CancelNeighborhood(neighborhoodList))
                  {
                    log.Info("  * {0} servers have been removed from the neighborhood of server '{1}'.", neighborhoodList.Count, profileServer.Name);
                  }
                  else
                  {
                    log.Error("  * Unable to remove neighbors from neighborhood of server '{0}'.", profileServer.Name);
                    error = true;
                    break;
                  }
                }
              }
              else
              {
                log.Error("  * Profile server instance '{0}' does not exist.", cmd.Source);
                error = true;
                break;
              }

              break;
            }


          case CommandType.TestQuery:
            {
              CommandTestQuery cmd = (CommandTestQuery)command;

              List<ProfileServer> targetServers = new List<ProfileServer>();
              for (int i = 0; i < cmd.PsCount; i++)
              {
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);
                ProfileServer profileServer;
                if (profileServers.TryGetValue(name, out profileServer))
                {
                  targetServers.Add(profileServer);
                }
                else
                {
                  log.Error("  * Profile server instance '{0}' does not exist.", name);
                  error = true;
                  break;
                }

              }

              IdentityClient client = null;
              try
              {
                client = new IdentityClient("Query Client", "Query Client", new GpsLocation(0, 0), null, 0);

                int maxResults = cmd.IncludeImages ? 1000 : 10000;
                string nameFilter = cmd.NameFilter != "**" ? cmd.NameFilter : null;
                string typeFilter = cmd.TypeFilter != "**" ? cmd.TypeFilter : null;
                GpsLocation queryLocation = cmd.Latitude != GpsLocation.NoLocation.Latitude ? new GpsLocation(cmd.Latitude, cmd.Longitude) : null;
                foreach (ProfileServer targetServer in targetServers)
                {
                  client.InitializeTcpClient();
                  Task<List<IdentityNetworkProfileInformation>> searchTask = client.SearchQueryAsync(targetServer, nameFilter, typeFilter, queryLocation, cmd.Radius, false, cmd.IncludeImages);
                  List<IdentityNetworkProfileInformation> searchResults = searchTask.Result;
                  if (searchResults != null)
                  {
                    List<IdentityNetworkProfileInformation> expectedSearchResults = targetServer.GetExpectedSearchResults(nameFilter, typeFilter, queryLocation, cmd.Radius, false, cmd.IncludeImages);

                    if (!CompareSearchResults(searchResults, expectedSearchResults, maxResults))
                    {
                      log.Error("  * Real search results are different from the expected results on server instance '{0}'.", targetServer.Name);
                      error = true;
                      break;
                    }
                  }
                  else 
                  {
                    log.Error("  * Unable to perform search on server instance '{0}'.", targetServer.Name);
                    error = true;
                    break;
                  }
                }
              }
              catch (Exception e)
              {
                log.Error("Exception occurred: {0}", e.ToString());
                error = true;
                break;
              }

              if (!error) log.Info("  * Results of search queries on {0} servers match expected results.", targetServers.Count);

              break;
            }



          case CommandType.Delay:
            {
              CommandDelay cmd = (CommandDelay)command;
              log.Info("  * Waiting {0} seconds ...", cmd.Seconds);
              Thread.Sleep(TimeSpan.FromSeconds((double)cmd.Seconds));
              break;
            }

          default:
            log.Error("Invalid command type '{0}'.", command.Type);
            error = true;
            break;
        }

        if (error) break;
      }

      res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Removes data from previous run.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool ClearHistory()
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (Directory.Exists(InstanceDirectory))
          Directory.Delete(InstanceDirectory, true);

        Directory.CreateDirectory(InstanceDirectory);
        res = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to delete and recreated '{0}': {1}", InstanceDirectory, e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Generates instance name from a group name and an instance number.
    /// </summary>
    /// <param name="GroupName">Name of the server group.</param>
    /// <param name="InstanceNumber">Instance number.</param>
    /// <returns>Instance name.</returns>
    public string GetInstanceName(string GroupName, int InstanceNumber)
    {
      return string.Format("{0}{1:000}", GroupName, InstanceNumber);
    }


    /// <summary>
    /// Generates identity name from an identity group name and an identity number.
    /// </summary>
    /// <param name="GroupName">Name of the identity group.</param>
    /// <param name="IdentityNumber">Identity number.</param>
    /// <returns>Identity name.</returns>
    public string GetIdentityName(string GroupName, int IdentityNumber)
    {
      return string.Format("{0}{1:00000}", GroupName, IdentityNumber);
    }


    /// <summary>
    /// Checks log files of server instances to see if there are any errors.
    /// </summary>
    public void CheckLogs()
    {
      log.Trace("()");

      bool error = false;
      foreach (ProfileServer server in profileServers.Values)
      {        
        string logDir;
        List<string> fileNames;
        List<int> errorCount;
        if (server.CheckLogs(out logDir, out fileNames, out errorCount))
        {
          if (logDir.ToLowerInvariant().StartsWith(InstanceDirectory.ToLowerInvariant() + Path.DirectorySeparatorChar))
            logDir = Path.Combine("instances", logDir.Substring(InstanceDirectory.Length + 1));

          int instanceErrorCount = 0;
          for (int i = 0; i < errorCount.Count; i++)
            instanceErrorCount += errorCount[i];

          if (instanceErrorCount > 0)
          {
            error = true;

            if (fileNames.Count == 1)
            {
              log.Info("  * {0} errors found in instance {1} log file '{2}'.", errorCount[0], server.Name, Path.Combine(logDir, fileNames[0]));
            }
            else
            {
              log.Info("  * {0} errors found in log files of instance {1}, log directory '{2}':", instanceErrorCount, server.Name, logDir);
              for (int i = 0; i < fileNames.Count; i++)
              {
                if (errorCount[i] > 0)
                  log.Info("    * {0}: {1} errors.", fileNames[i], errorCount[i]);
              }
            }
          }
        }
        else
        {
          log.Error("  * Failed to analyze logs of instance {0}.", server.Name);
          error = true;
        }
      }

      if (!error) log.Info("  * No errors found in logs.");

      log.Trace("(-)");
    }


    /// <summary>
    /// Compares results of a test search query against the expected results.
    /// </summary>
    /// <param name="RealSearchResults">Search results obtained by real client from a profile server.</param>
    /// <param name="ExpectedSearchResults">Results calculated from the global knowledge.</param>
    /// <param name="MaxTotalResults">Limit to total number of results. If <paramref name="ExpectedSearchResults"/> is lower than this value, 
    /// the result sets must be exactly the same, otherwise <paramref name="ExpectedSearchResults"/> must be a superset of <paramref name="RealSeachResults"/>
    /// and the number of real results must be equal to this value..</param>
    /// <returns>true if real results match expected results, false otherwise.</returns>
    public bool CompareSearchResults(List<IdentityNetworkProfileInformation> RealSearchResults, List<IdentityNetworkProfileInformation> ExpectedSearchResults, int MaxTotalResults)
    {
      log.Trace("(RealSearchResults.Count:{0},ExpectedSearchResults.Count:{1},MaxTotalResults:{2})", RealSearchResults.Count, ExpectedSearchResults.Count, MaxTotalResults);

      bool res = false;

      if (ExpectedSearchResults.Count <= MaxTotalResults)
      {
        // If number of all existing results (i.e. expected results) is no more than maximal possible number of results 
        // the client could get, the real set of results and the expected set of results must be exactly the same.
        res = RealSearchResults.Count == ExpectedSearchResults.Count;
      }
      else
      {
        // If the number of expected result is greater than the maximal number of results the client could get (MaxTotalResults)
        // the number of real results must be equal to MaxTotalResults and it must be a subset of expected results set.
        res = RealSearchResults.Count == MaxTotalResults;
      }

      if (res)
      {
        HashSet<int> expectedRecordIndexes = new HashSet<int>();
        for (int i = 0; i < RealSearchResults.Count; i++)
        {
          IdentityNetworkProfileInformation info = RealSearchResults[i];

          bool match = false;
          for (int j = 0; j < ExpectedSearchResults.Count; j++)
          {
            // The record can not be used twice.
            if (expectedRecordIndexes.Contains(j)) continue;

            IdentityNetworkProfileInformation expectedRecord = ExpectedSearchResults[j];
            byte[] infoBinary = info.ToByteArray();
            byte[] expectedRecordBinary = expectedRecord.ToByteArray();
            bool itemMatch = StructuralEqualityComparer<byte[]>.Default.Equals(infoBinary, expectedRecordBinary);
            /*
            byte[] infoPubKey = info.IdentityPublicKey.ToByteArray();
            byte[] expectedPubKey = expectedRecord.IdentityPublicKey.ToByteArray();
            bool pubKeyMatch = StructuralEqualityComparer<byte[]>.Default.Equals(infoPubKey, expectedPubKey);

            if (!pubKeyMatch) continue;

            bool matchExtraData = info.ExtraData == expectedRecord.ExtraData;

            byte[] infoHostingId = info.HostingServerNetworkId.ToByteArray();
            byte[] expectedHostingId = expectedRecord.HostingServerNetworkId.ToByteArray();
            bool hostingIdMatch = StructuralEqualityComparer<byte[]>.Default.Equals(infoHostingId, expectedHostingId);

            bool isHostedMatch = info.IsHosted == expectedRecord.IsHosted;
            bool isOnlineMatch = info.IsOnline == expectedRecord.IsOnline;
            bool latitudeMatch = info.Latitude == expectedRecord.Latitude;
            bool longitudeMatch = info.Longitude == expectedRecord.Longitude;
            bool nameMatch = info.Name == expectedRecord.Name;

            byte[] infoImage = info.ThumbnailImage.ToByteArray();
            byte[] expectedImage = expectedRecord.ThumbnailImage.ToByteArray();
            bool imageMatch = StructuralEqualityComparer<byte[]>.Default.Equals(infoImage, expectedImage);

            bool typeMatch = info.Type == expectedRecord.Type;

            byte[] infoVersion = info.Version.ToByteArray();
            byte[] expectedVersion = expectedRecord.Version.ToByteArray();
            bool versionMatch = StructuralEqualityComparer<byte[]>.Default.Equals(infoVersion, expectedVersion);

            // Real search result is found among the expected results, if everything matches.
            bool itemMatch = pubKeyMatch && matchExtraData && hostingIdMatch && isHostedMatch && isOnlineMatch && latitudeMatch && longitudeMatch && nameMatch && imageMatch && typeMatch && versionMatch;
            */
            if (itemMatch)
            {
              expectedRecordIndexes.Add(j);
              match = true;
              break;
            }
          }

          // If a single record is not found, results are not as expected.
          if (!match)
          {
            res = false;
            break;
          }
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
