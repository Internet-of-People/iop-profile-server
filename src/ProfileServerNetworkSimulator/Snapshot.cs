using Newtonsoft.Json;
using ProfileServerCrypto;
using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ProfileServerNetworkSimulator
{
  /// <summary>
  /// Description of profile server instance.
  /// </summary>
  public class ProfileServerSnapshot
  {
    /// <summary>Name of the instance.</summary>
    public string Name;

    /// <summary>true if the server was running before the snapshot was taken.</summary>
    public bool IsRunning;

    /// <summary>GPS location latitude of the server.</summary>
    public decimal LocationLatitude;

    /// <summary>GPS location longitude of the server.</summary>
    public decimal LocationLongitude;

    /// <summary>IP address of the interface on which the server is listening.</summary>
    public string IpAddress;

    /// <summary>Base TCP port of the instance, which can use ports between Port and Port + 19.</summary>
    public int BasePort;

    /// <summary>Port of LOC server.</summary>
    public int LocPort;

    /// <summary>Port of profile server primary interface.</summary>
    public int PrimaryInterfacePort;

    /// <summary>Port of profile server neighbors interface.</summary>
    public int ServerNeighborInterfacePort;

    /// <summary>Port of profile server non-customer interface.</summary>
    public int ClientNonCustomerInterfacePort;

    /// <summary>Port of profile server customer interface.</summary>
    public int ClientCustomerInterfacePort;

    /// <summary>Port of profile server application service interface.</summary>
    public int ClientAppServiceInterfacePort;

    /// <summary>Number of free slots for identities.</summary>
    public int AvailableIdentitySlots;

    /// <summary>List of hosted customer identities.</summary>
    public List<string> HostedIdentities;

    /// <summary>Network ID of the profile server.</summary>
    public string NetworkId;

    /// <summary>Related LOC server instance.</summary>
    public LocServerSnapshot LocServer;
  }

  /// <summary>
  /// Description of LOC server instance.
  /// </summary>
  public class LocServerSnapshot
  {
    /// <summary>Interface IP address the server listens on.</summary>
    public string IpAddress;

    /// <summary>TCP port the server listens on.</summary>
    public int Port;

    /// <summary>Name of profile servers that are neighbors of the parent profile server.</summary>
    public List<string> NeighborsNames;
  }

  /// <summary>
  /// Description of identity client instance.
  /// </summary>
  public class IdentitySnapshot
  {
    /// <summary>Identity name.</summary>
    public string Name;

    /// <summary>Hosting profile server name.</summary>
    public string ProfileServerName;

    /// <summary>Identity Type.</summary>
    public string Type;

    /// <summary>Initial GPS location latitude.</summary>
    public decimal LocationLatitude;

    /// <summary>Initial GPS location longitude.</summary>
    public decimal LocationLongitude;

    /// <summary>Profile image file name or null if the identity has no profile image.</summary>
    public string ImageFileName;

    /// <summary>SHA256 hash of profile image data or null if the identity has no profile image.</summary>
    public string ProfileImageHash;

    /// <summary>SHA256 hash of thumbnail image data or null if the identity has no thumbnail image.</summary>
    public string ThumbnailImageHash;

    /// <summary>Profile extra data information.</summary>
    public string ExtraData;

    /// <summary>Profile version.</summary>
    public SemVer Version;

    /// <summary>Network identifier of the client's identity.</summary>
    public string IdentityId;

    /// <summary>Public key in uppercase hex format.</summary>
    public string PublicKeyHex;
    
    /// <summary>Private key in uppercase hex format.</summary>
    public string PrivateKeyHex;
    
    /// <summary>Expanded private key in uppercase hex format.</summary>
    public string ExpandedPrivateKeyHex;

    /// <summary>Challenge that the profile server sent to the client when starting conversation.</summary>
    public string Challenge;

    /// <summary>Challenge that the client sent to the profile server when starting conversation.</summary>
    public string ClientChallenge;

    /// <summary>Profile server's public key received when starting conversation.</summary>
    public string ProfileServerKey;
    
    /// <summary>true if the client initialized its profile on the profile server, false otherwise.</summary>
    public bool ProfileInitialized;

    /// <summary>true if the client has an active hosting agreement with the profile server, false otherwise.</summary>
    public bool HostingActive;
  }


  /// <summary>
  /// Describes whole state of a simulation instance in a form that can be saved to a file.
  /// </summary>
  public class Snapshot
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerNetworkSimulator.Snapshot");

    /// <summary>Name of the file with serialized profile server information.</summary>
    public const string ProfileServersFileName = "ProfileServers.json";

    /// <summary>Name of the file with serialized identities information.</summary>
    public const string IdentitiesFileName = "Identities.json";

    /// <summary>Name of the file with serialized images information.</summary>
    public const string ImagesFileName = "Images.json";

    /// <summary>Name of the snapshot.</summary>
    public string Name;

    /// <summary>List of profile servers.</summary>
    public List<ProfileServerSnapshot> ProfileServers;

    /// <summary>List of identities.</summary>
    public List<IdentitySnapshot> Identities;

    /// <summary>Date of images used by identities mapped by their SHA256 hash.</summary>
    public Dictionary<string, string> Images;

    /// <summary>Directory with snapshot files.</summary>
    private string snapshotDirectory;

    /// <summary>Name of profile servers JSON file within the snapshot directory.</summary>
    private string profileServersFile;

    /// <summary>Name of identities JSON file within the snapshot directory.</summary>
    private string identitiesFile;

    /// <summary>Name of images JSON file within the snapshot directory.</summary>
    private string imagesFile;

    /// <summary>
    /// Initializes snapshot instance.
    /// </summary>
    /// <param name="Name">Name of the snapshot.</param>
    public Snapshot(string Name)
    {
      this.Name = Name;
      ProfileServers = new List<ProfileServerSnapshot>();
      Identities = new List<IdentitySnapshot>();
      Images = new Dictionary<string, string>(StringComparer.Ordinal);

      snapshotDirectory = Path.Combine(CommandProcessor.SnapshotDirectory, this.Name);
      profileServersFile = Path.Combine(snapshotDirectory, ProfileServersFileName);
      identitiesFile = Path.Combine(snapshotDirectory, IdentitiesFileName);
      imagesFile = Path.Combine(snapshotDirectory, ImagesFileName);
    }

    /// <summary>
    /// Stops all running profile servers and takes snapshot of the simulation.
    /// <para>All profile servers are expected to be stopped when this method is called.</para>
    /// </summary>
    /// <param name="RunningServerNames">List of servers that were running before the snapshot was taken.</param>
    /// <param name="ProfileServers">List of simulation profile servers.</param>
    /// <param name="IdentityClients">List of simulation identities.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Take(HashSet<string> RunningServerNames, Dictionary<string, ProfileServer> ProfileServers, Dictionary<string, IdentityClient> IdentityClients)
    {
      log.Trace("()");

      foreach (ProfileServer server in ProfileServers.Values)
      {
        ProfileServerSnapshot serverSnapshot = server.CreateSnapshot();
        serverSnapshot.IsRunning = RunningServerNames.Contains(server.Name);
        this.ProfileServers.Add(serverSnapshot);
      }

      foreach (IdentityClient identity in IdentityClients.Values)
      {
        IdentitySnapshot identitySnapshot = identity.CreateSnapshot();

        if (identitySnapshot.ProfileImageHash != null)
        {
          if (!this.Images.ContainsKey(identitySnapshot.ProfileImageHash))
          {
            string imageDataHex = Crypto.ToHex(identity.ProfileImage);
            this.Images.Add(identitySnapshot.ProfileImageHash, imageDataHex);
          }
        }

        if (identitySnapshot.ThumbnailImageHash != null)
        {
          if (!this.Images.ContainsKey(identitySnapshot.ThumbnailImageHash))
          {
            string imageDataHex = Crypto.ToHex(identity.ThumbnailImage);
            this.Images.Add(identitySnapshot.ThumbnailImageHash, imageDataHex);
          }
        }

        this.Identities.Add(identitySnapshot);
      }


      bool error = false;
      try
      {
        if (Directory.Exists(snapshotDirectory))
          Directory.Delete(snapshotDirectory, true);

        Directory.CreateDirectory(snapshotDirectory);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to delete and recreate '{0}': {1}", snapshotDirectory, e.ToString());
        error = true;
      }

      if (!error)
      {
        try
        {
          string serializedProfileServers = JsonConvert.SerializeObject(this.ProfileServers, Formatting.Indented);
          string serializedIdentities = JsonConvert.SerializeObject(this.Identities, Formatting.Indented);
          string serializedImages = JsonConvert.SerializeObject(this.Images, Formatting.Indented);

          File.WriteAllText(profileServersFile, serializedProfileServers);
          File.WriteAllText(identitiesFile, serializedIdentities);
          File.WriteAllText(imagesFile, serializedImages);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred while trying to save serialized simulation information: {0}", e.ToString());
          error = true;
        }
      }

      if (!error)
      {
        foreach (ProfileServer server in ProfileServers.Values)
        {
          string serverInstanceDirectory = server.GetInstanceDirectoryName();
          string snapshotInstanceDirectory = Path.Combine(new string[] { snapshotDirectory, "bin", server.Name });
          if (!Helpers.DirectoryCopy(serverInstanceDirectory, snapshotInstanceDirectory, true, new string[] { "logs", "tmp" }))
          {
            log.Error("Unable to copy files from directory '{0}' to '{1}'.", serverInstanceDirectory, snapshotInstanceDirectory);
            error = true;
            break;
          }

          string logsDirectory = Path.Combine(snapshotInstanceDirectory, "Logs");
          try
          {
            if (Directory.Exists(logsDirectory))
              Directory.Delete(logsDirectory, true);
          }
          catch (Exception e)
          {
            log.Error("Exception occurred while trying to delete directory '{0}': {1}", logsDirectory, e.ToString());
            error = true;
            break;
          }
        }
      }

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Loads snapshot from snapshot folder.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Load()
    {
      log.Trace("()");

      bool error = false;

      try
      {
        log.Debug("Loading profile servers information.");
        string serializedProfileServers = File.ReadAllText(profileServersFile);

        log.Debug("Deserializing profile servers information.");
        ProfileServers = JsonConvert.DeserializeObject<List<ProfileServerSnapshot>>(serializedProfileServers);


        log.Debug("Loading identities information.");
        string serializedIdentities = File.ReadAllText(identitiesFile);

        log.Debug("Deserializing identities information.");
        Identities = JsonConvert.DeserializeObject<List<IdentitySnapshot>>(serializedIdentities);

        log.Debug("Loading images information.");
        string serializedImages = File.ReadAllText(imagesFile);

        log.Debug("Deserializing images information.");
        Images = JsonConvert.DeserializeObject<Dictionary<string, string>>(serializedImages);


        log.Debug("Loading profile servers instance folders.");
        foreach (ProfileServerSnapshot server in ProfileServers)
        {
          string serverInstanceDirectory = ProfileServer.GetInstanceDirectoryName(server.Name);
          string snapshotInstanceDirectory = Path.Combine(new string[] { snapshotDirectory, "bin", server.Name });
          log.Debug("Copying '{0}' to '{1}'.", snapshotInstanceDirectory, serverInstanceDirectory);
          if (!Helpers.DirectoryCopy(snapshotInstanceDirectory, serverInstanceDirectory))
          {
            log.Error("Unable to copy files from directory '{0}' to '{1}'.", snapshotInstanceDirectory, serverInstanceDirectory);
            error = true;
            break;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to load serialized simulation files: {0}", e.ToString());
        error = true;
      }

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
  }
