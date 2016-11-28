using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerSimulator
{
  /// <summary>
  /// Engine that executes the commands.
  /// </summary>
  public class CommandProcessor
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Directory that contains files of running server instances.</summary>
    public const string InstanceDirectory = "instances";

    /// <summary>Directory that contains original binaries.</summary>
    public const string BinariesDirectory = "bin";

    /// <summary>Directory that contains images.</summary>
    public const string ImagesDirectory = "images";

    /// <summary>Directory that contains original profile server files within binary directory.</summary>
    public static string ProfileServerBinariesDirectory = Path.Combine(BinariesDirectory, "ProfileServer");

    /// <summary>List of commands to execute.</summary>
    public List<Command> Commands;

    /// <summary>List of profile server instances mapped by their name.</summary>
    public Dictionary<string, ProfileServer> ProfileServers = new Dictionary<string, ProfileServer>(StringComparer.Ordinal);

    /// <summary>List of identity client instances mapped by their name.</summary>
    public Dictionary<string, IdentityClient> IdentityClients = new Dictionary<string, IdentityClient>(StringComparer.Ordinal);

    /// <summary>
    /// Initializes the object instance.
    /// </summary>
    /// <param name="Commands">List of commands to execute.</param>
    public CommandProcessor(List<Command> Commands)
    {
      log.Trace("()");

      this.Commands = Commands;

      log.Trace("(-)");
    }

    /// <summary>
    /// Frees resources used by command processor.
    /// </summary>
    public void Shutdown()
    {
      log.Trace("()");

      foreach (IdentityClient identity in IdentityClients.Values)
      {
        identity.Shutdown();
      }

      foreach (ProfileServer server in ProfileServers.Values)
      {
        server.Shutdown();
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Executes all commands.
    /// </summary>
    public void Execute()
    {
      log.Trace("()");

      ClearHistory();

      int index = 1;
      bool error = false;
      foreach (Command command in Commands)
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
                ProfileServer profileServer = new ProfileServer(name, location, cmd.BasePort);
                ProfileServers.Add(name, profileServer);
              }

              log.Info("  * {0} profile servers created.", cmd.Count);
              break;
            }

          case CommandType.StartServer:
            {
              CommandStartServer cmd = (CommandStartServer)command;
              for (int i = 1; i <= cmd.PsCount; i++)
              {
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);
                ProfileServer profileServer;
                if (ProfileServers.TryGetValue(name, out profileServer))
                {
                  if (!profileServer.Start())
                  {
                    log.Error("  * Unable to start server instance '{0}'.", name);
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

              if (!error) log.Info("  * {0} profile servers started.", cmd.PsCount);
              break;
            }

          case CommandType.StopServer:
            {
              CommandStopServer cmd = (CommandStopServer)command;
              for (int i = 1; i <= cmd.PsCount; i++)
              {
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);
                ProfileServer profileServer;
                if (ProfileServers.TryGetValue(name, out profileServer))
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
              for (int i = 1; i <= cmd.PsCount; i++)
              {
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);
                ProfileServer profileServer;
                if (ProfileServers.TryGetValue(name, out profileServer))
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
                string name = GetInstanceName(cmd.PsGroup, cmd.PsIndex + i);

                int serverIndex = Helpers.Rng.Next(availableServers.Count);
                ProfileServer profileServer = availableServers[serverIndex];

                GpsLocation location = Helpers.GenerateRandomGpsLocation(cmd.Latitude, cmd.Longitude, cmd.Radius);
                IdentityClient identityClient = null;
                try
                {
                  identityClient = new IdentityClient(name, cmd.IdentityType, location, cmd.ImageMask, cmd.ImageChance);
                }
                catch
                {
                  log.Error("Unable to create identity '{0}'.", name);
                  error = true;
                  break;
                }

                if (error) break;

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
                }
              }

              if (!error) log.Info("  * {0} identities created and initialized on {1} servers.", cmd.Count, cmd.PsCount);
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

      log.Trace("(-)");
    }

    /// <summary>
    /// Removes data from previous run.
    /// </summary>
    public void ClearHistory()
    {
      log.Trace("()");

      if (Directory.Exists(InstanceDirectory))
        Directory.Delete(InstanceDirectory, true);

      Directory.CreateDirectory(InstanceDirectory);

      log.Trace("(-)");
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
  }
}
