using ProfileServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using ProfileServerCrypto;
using ProfileServer.Data.Models;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.IO;
using ProfileServer.Network;
using ProfileServer.Utils;
using System.Globalization;
using ProfileServerProtocol.Multiformats;

namespace ProfileServer.Config
{
  /// <summary>
  /// Types of values allowed in the configuration file.
  /// </summary>
  public enum ConfigValueType
  {
    /// <summary>Boolean switch "on" or "off".</summary>
    OnOffSwitch,

    /// <summary>String value that can not be empty.</summary>
    StringNonEmpty,

    /// <summary>String value that can be empty.</summary>
    StringEmpty,

    /// <summary>Integer value.</summary>
    Int,

    /// <summary>TCP/UDP port number - i.e. integer in range 0-65535.</summary>
    Port,

    /// <summary>IPv4 or IPv6 address or string "any".</summary>
    IpAddress
  }

  /// <summary>
  /// Description of each server role interface.
  /// </summary>
  public class RoleServerConfiguration
  {
    /// <summary>Roles of this server.</summary>
    public ServerRole Roles;

    /// <summary>Are the services on this port encrypted?</summary>
    public bool Encrypted;

    /// <summary>true if the server is operating on TCP protocol, false if on UDP.</summary>
    public bool IsTcpServer;

    /// <summary>Port on which this server provides its services.</summary>
    public int Port;
  }


  /// <summary>
  /// Provides access to the global configuration. The configuration is partly stored in the configuration file 
  /// and partly in the database. The instance of the configuration class is accessible via <c>Kernel.Base.Configuration</c>.
  /// </summary>
  /// <remarks>
  /// Loading configuration is essential for the node's startup. If any part of it fails, the node will refuse to start.
  /// </remarks>
  public class Config : Kernel.Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Config.Config");

    /// <summary>Default name of the configuration file.</summary>
    public const string ConfigFileName = "ProfileServer.conf";

    /// <summary>Specification of network interface, on which the node servers will operate.</summary>
    public IPAddress ServerInterface;

    /// <summary>Certificate to be used for TCP TLS server.</summary>
    public X509Certificate TcpServerTlsCertificate;

    /// <summary>Description of role servers.</summary>
    public ServerRolesConfig ServerRoles;

    /// <summary>Path to the directory where images are stored.</summary>
    public string ImageDataFolder;

    /// <summary>Path to the directory where temporary files are stored.</summary>
    public string TempDataFolder;

    /// <summary>Maximal total number of identities hosted by this node.</summary>
    public int MaxHostedIdentities;

    /// <summary>Maximum number of relations that an identity is allowed to have. Must be lower than RelatedIdentity.MaxIdentityRelations.</summary>
    public int MaxIdenityRelations;

    /// <summary>Maximum number of parallel neighborhood initialization processes that the node is willing to process.</summary>
    public int NeighborhoodInitializationParallelism;

    /// <summary>End point of the Location Based Network server.</summary>
    public IPEndPoint LbnEndPoint;

    /// <summary>End point of the Content Address Network server.</summary>
    public IPEndPoint CanEndPoint;

    /// <summary>Cryptographic keys of the node that can be used for signing messages and verifying signatures.</summary>
    public KeysEd25519 Keys;

    /// <summary>Time in seconds between the last update of shared profiles received from a neighbor server up to the point when 
    /// the profile server is allowed to delete the profiles if they were not refreshed.</summary>
    public int NeighborProfilesExpirationTimeSeconds;

    /// <summary>Time in seconds between the last refresh request sent by the profile server to its Follower server.</summary>
    public int FollowerRefreshTimeSeconds;

    /// <summary>Test mode allows the server to violate protocol as some of the limitations are not enforced.</summary>
    public bool TestModeEnabled;

    /// <summary>Maximum number of neighbors that the profile server is going to accept.</summary>
    public int MaxNeighborhoodSize;

    /// <summary>Maximum number of follower servers the profile server is willing to share its database with.</summary>
    public int MaxFollowerServersCount;


    /// <summary>Last sequence number used for IPNS record.</summary>
    public UInt64 CanIpnsLastSequenceNumber;

    /// <summary>CAN hash of profile server's contact information object in CAN.</summary>
    public byte[] CanProfileServerContactInformationHash;

    /// <summary>True if the profile server's contact information loaded from the database differs from the one loaded from the configuration file.</summary>
    public bool CanProfileServerContactInformationChanged;


    public override bool Init()
    {
      log.Trace("()");

      bool res = false;

      try
      {
        if (LoadConfigurationFromFile(ConfigFileName))
        {
          if (InitializeDbSettings())
          {
            res = true;
            Initialized = true;
          }
          else log.Error("Database initialization failed.");
        }
        else log.Error("Loading configuration file failed.");
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    public override void Shutdown()
    {
      log.Trace("()");

      log.Trace("(-)");
    }
    
    /// <summary>
    /// Loads global configuration from a file.
    /// </summary>
    /// <param name="FileName">Name of the configuration file. This can be both relative or full path to the file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    /// <remarks>See the configuration file comments for the file format and accepted values.</remarks>
    public bool LoadConfigurationFromFile(string FileName)
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (File.Exists(ConfigFileName))
        {
          string[] lines = File.ReadAllLines(ConfigFileName);
          res = LoadConfigurationFromStringArray(lines);
        }
        else log.Error("Unable to find configuration file '{0}'.", ConfigFileName);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// <para>Loads global configuration from a string array that corresponds to lines of configuration file.</para>
    /// <seealso cref="LoadConfigurationFromFile"/>
    /// </summary>
    /// <param name="Lines">Node configuration as a string array.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool LoadConfigurationFromStringArray(string[] Lines)
    {
      log.Trace("()");

      bool error = false;
      if ((Lines != null) && (Lines.Length > 0))
      {
        bool testModeEnabled = false;
        string tcpServerTlsCertificateFileName = null;
        X509Certificate tcpServerTlsCertificate = null;
        ServerRolesConfig serverRoles = null;
        IPAddress serverInterface = null;
        string imageDataFolder = null;
        string tempDataFolder = null;
        int maxHostedIdentities = 0;
        int maxIdentityRelations = 0;
        int neighborhoodInitializationParallelism = 0;
        int lbnPort = 0;
        int canPort = 0;
        IPEndPoint lbnEndPoint = null;
        IPEndPoint canEndPoint = null;
        int neighborProfilesExpirationTimeSeconds = 0;
        int followerRefreshTimeSeconds = 0;
        int maxNeighborhoodSize = 0;
        int maxFollowerServersCount = 0;

        Dictionary<string, object> nameVal = new Dictionary<string, object>(StringComparer.Ordinal);

        // Definition of all supported values in configuration file together with their types.
        Dictionary<string, ConfigValueType> namesDefinition = new Dictionary<string, ConfigValueType>(StringComparer.Ordinal)
        {
          { "test_mode",                               ConfigValueType.OnOffSwitch    },
          { "server_interface",                        ConfigValueType.IpAddress      },
          { "primary_interface_port",                  ConfigValueType.Port           },
          { "server_neighbor_interface_port",          ConfigValueType.Port           },
          { "client_non_customer_interface_port",      ConfigValueType.Port           },
          { "client_customer_interface_port",          ConfigValueType.Port           },
          { "client_app_service_interface_port",       ConfigValueType.Port           },          
          { "tls_server_certificate",                  ConfigValueType.StringNonEmpty },
          { "image_data_folder",                       ConfigValueType.StringNonEmpty },
          { "tmp_data_folder",                         ConfigValueType.StringNonEmpty },
          { "max_hosted_identities",                   ConfigValueType.Int            },
          { "max_identity_relations",                  ConfigValueType.Int            },
          { "neighborhood_initialization_parallelism", ConfigValueType.Int            },
          { "lbn_port",                                ConfigValueType.Port           },
          { "can_api_port",                            ConfigValueType.Port           },
          { "neighbor_profiles_expiration_time",       ConfigValueType.Int            },
          { "max_neighborhood_size",                   ConfigValueType.Int            },
          { "max_follower_servers_count",              ConfigValueType.Int            },
          { "follower_refresh_time",                   ConfigValueType.Int            },
        };

        error = !LinesToNameValueDictionary(Lines, namesDefinition, nameVal);
        if (!error)
        {
          testModeEnabled = (bool)nameVal["test_mode"];
          serverInterface = (IPAddress)nameVal["server_interface"];
          int primaryInterfacePort = (int)nameVal["primary_interface_port"];
          int serverNeighborInterfacePort = (int)nameVal["server_neighbor_interface_port"];
          int clientNonCustomerInterfacePort = (int)nameVal["client_non_customer_interface_port"];
          int clientCustomerInterfacePort = (int)nameVal["client_customer_interface_port"];
          int clientAppServiceInterfacePort = (int)nameVal["client_app_service_interface_port"];

          tcpServerTlsCertificateFileName = (string)nameVal["tls_server_certificate"];
          imageDataFolder = (string)nameVal["image_data_folder"];
          tempDataFolder = (string)nameVal["tmp_data_folder"];
          maxHostedIdentities = (int)nameVal["max_hosted_identities"];
          maxIdentityRelations = (int)nameVal["max_identity_relations"];
          neighborhoodInitializationParallelism = (int)nameVal["neighborhood_initialization_parallelism"];

          lbnPort = (int)nameVal["lbn_port"];
          canPort = (int)nameVal["can_api_port"];

          neighborProfilesExpirationTimeSeconds = (int)nameVal["neighbor_profiles_expiration_time"];
          followerRefreshTimeSeconds = (int)nameVal["follower_refresh_time"];

          maxNeighborhoodSize = (int)nameVal["max_neighborhood_size"];
          maxFollowerServersCount = (int)nameVal["max_follower_servers_count"];


          serverRoles = new ServerRolesConfig();          
          error = !(serverRoles.AddRoleServer(primaryInterfacePort, ServerRole.Primary)
                 && serverRoles.AddRoleServer(serverNeighborInterfacePort, ServerRole.ServerNeighbor)
                 && serverRoles.AddRoleServer(clientNonCustomerInterfacePort, ServerRole.ClientNonCustomer)
                 && serverRoles.AddRoleServer(clientCustomerInterfacePort, ServerRole.ClientCustomer)
                 && serverRoles.AddRoleServer(clientAppServiceInterfacePort, ServerRole.ClientAppService));
        }

        if (!error)
        {
          if (!testModeEnabled && serverInterface.IsReservedOrLocal())
          {
            log.Error("server_interface must be an IP address of external, publicly accessible interface.");
            error = true;
          }
        }

        if (!error)
        {
          string finalTlsCertFileName;
          if (FileHelper.FindFile(tcpServerTlsCertificateFileName, out finalTlsCertFileName))
          {
            tcpServerTlsCertificateFileName = finalTlsCertFileName;
          }
          else 
          {
            log.Error("File '{0}' not found.", tcpServerTlsCertificateFileName);
            error = true;
          }          
        }

        if (!error)
        {
          try
          {
            tcpServerTlsCertificate = new X509Certificate2(tcpServerTlsCertificateFileName);
          }
          catch (Exception e)
          {
            log.Error("Exception occurred while loading certificate file '{0}': {1}", tcpServerTlsCertificateFileName, e.ToString());
            error = true;
          }
        }

        if (!error)
        {
          try
          {
            if (!Directory.Exists(imageDataFolder))
              Directory.CreateDirectory(imageDataFolder);
          }
          catch (Exception e)
          {
            log.Error("Exception occurred while initializing image data folder '{0}': {1}", imageDataFolder, e.ToString());
            error = true;
          }
        }

        if (!error)
        {
          try
          {
            if (Directory.Exists(tempDataFolder))
            {
              if (!FileHelper.CleanDirectory(tempDataFolder))
              {
                log.Error("Unable to remove all files and folders from temporary directory '{0}'.", tempDataFolder);
                error = true;
              }
            }
            else Directory.CreateDirectory(tempDataFolder);
          }
          catch (Exception e)
          {
            log.Error("Exception occurred while initializing image data folder '{0}': {1}", imageDataFolder, e.ToString());
            error = true;
          }
        }

        if (!error)
        {
          if ((maxHostedIdentities <= 0) || (maxHostedIdentities > IdentityBase.MaxHostedIdentities))
          {
            log.Error("max_hosted_identities must be an integer between 1 and {0}.", IdentityBase.MaxHostedIdentities);
            error = true;
          }
        }

        if (!error)
        {
          if ((maxIdentityRelations <= 0) || (maxIdentityRelations > RelatedIdentity.MaxIdentityRelations))
          {
            log.Error("max_identity_relations must be an integer between 1 and {0}.", RelatedIdentity.MaxIdentityRelations);
            error = true;
          }
        }

        if (!error)
        {
          if (neighborhoodInitializationParallelism <= 0)
          {
            log.Error("neighborhood_initialization_parallelism must be a positive integer.");
            error = true;
          }
        }

        if (!error)
        {
          foreach (RoleServerConfiguration rsc in serverRoles.RoleServers.Values)
          {
            if (lbnPort == rsc.Port)
            {
              log.Error("lbn_port {0} collides with port of server role {1}.", lbnPort, rsc.Roles);
              error = true;
              break;
            }
          }

          if (!error)
            lbnEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), lbnPort);
        }

        if (!error)
        {
          foreach (RoleServerConfiguration rsc in serverRoles.RoleServers.Values)
          {
            if (canPort == rsc.Port)
            {
              log.Error("can_api_port {0} collides with port of server role {1}.", canPort, rsc.Roles);
              error = true;
              break;
            }
          }

          if (canPort == lbnPort)
          {
            log.Error("can_api_port {0} collides with lbn_port.", canPort);
            error = true;
          }

          if (!error)
            canEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), canPort);
        }

        if (!error)
        {
          if (!testModeEnabled && (neighborProfilesExpirationTimeSeconds < Neighbor.MinNeighborhoodExpirationTimeSeconds))
          {
            log.Error("neighbor_profiles_expiration_time must be an integer number greater or equal to {0}.", Neighbor.MinNeighborhoodExpirationTimeSeconds);
            error = true;
          }
        }

        if (!error)
        {
          bool followerRefreshTimeSecondsValid = (0 < followerRefreshTimeSeconds) && (followerRefreshTimeSeconds < Neighbor.MinNeighborhoodExpirationTimeSeconds);
          if (!testModeEnabled && !followerRefreshTimeSecondsValid)
          {
            log.Error("follower_refresh_time must be an integer number between 1 and {0}.", Neighbor.MinNeighborhoodExpirationTimeSeconds - 1);
            error = true;
          }
        }

        if (!error)
        {
          if (!testModeEnabled && (maxNeighborhoodSize < Neighbor.MinMaxNeighborhoodSize))
          {
            log.Error("max_neighborhood_size must be an integer number greater or equal to {0}.", Neighbor.MinMaxNeighborhoodSize);
            error = true;
          }
        }

        if (!error)
        {
          if (!testModeEnabled && (maxFollowerServersCount < maxNeighborhoodSize))
          {
            log.Error("max_follower_servers_count must be an integer greater or equal to max_neighborhood_size.");
            error = true;
          }
        }

        // Finally, if everything is OK, change the actual configuration.
        if (!error)
        {
          TestModeEnabled = testModeEnabled;
          ServerInterface = serverInterface;
          ServerRoles = serverRoles;
          TcpServerTlsCertificate = tcpServerTlsCertificate;
          ImageDataFolder = imageDataFolder;
          TempDataFolder = tempDataFolder;
          MaxHostedIdentities = maxHostedIdentities;
          MaxIdenityRelations = maxIdentityRelations;
          NeighborhoodInitializationParallelism = neighborhoodInitializationParallelism;
          LbnEndPoint = lbnEndPoint;
          CanEndPoint = canEndPoint;
          NeighborProfilesExpirationTimeSeconds = neighborProfilesExpirationTimeSeconds;
          FollowerRefreshTimeSeconds = followerRefreshTimeSeconds;
          MaxNeighborhoodSize = maxNeighborhoodSize;
          MaxFollowerServersCount = maxFollowerServersCount;

          log.Info("New configuration loaded successfully.");
        }
      }
      else log.Error("Configuration file is empty.");

      bool res = !error;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Converts an array of configuration lines to a dictionary name-value structure,
    /// while checking if all names are defined, values are valid, and no required name is missing.
    /// </summary>
    /// <param name="Lines">Array of configuration lines.</param>
    /// <param name="NamesDefinition">Definition of configuration names.</param>
    /// <param name="NameVal">Empty dictionary to be filled with name-value pairs if the function succeeds.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool LinesToNameValueDictionary(string[] Lines, Dictionary<string, ConfigValueType> NamesDefinition, Dictionary<string, object> NameVal)
    {
      bool error = false;
      int lineNumber = 0;
      foreach (string aline in Lines)
      {
        lineNumber++;
        string line = aline.Trim();
        if ((line.Length == 0) || (line[0] == '#')) continue;

        int epos = line.IndexOf('=');
        if (epos == -1)
        {
          log.Error("Line {0} does not contain equal sign.", lineNumber);
          error = true;
          break;
        }

        string name = line.Substring(0, epos).Trim();
        string value = line.Substring(epos + 1).Trim();

        if (string.IsNullOrEmpty(name))
        {
          log.Error("No name before equal sign on line {0}.", lineNumber);
          error = true;
          break;
        }

        if (NameVal.ContainsKey(name))
        {
          log.Error("Name '{0}' redefined on line {1}.", name, lineNumber);
          error = true;
          break;
        }

        if (!NamesDefinition.ContainsKey(name))
        {
          log.Error("Unknown name '{0}' on line {1}.", name, lineNumber);
          error = true;
          break;
        }

        error = true;
        ConfigValueType type = NamesDefinition[name];
        switch (type)
        {
          case ConfigValueType.Int:
            {
              int val;
              if (int.TryParse(value, out val))
              {
                NameVal.Add(name, val);
                error = false;
              }
              else log.Error("Invalid integer value '{0}' on line {1}.", value, lineNumber);
              break;
            }

          case ConfigValueType.Port:
            {
              int val;
              if (int.TryParse(value, out val))
              {
                if ((val >= 0) || (val <= 65535))
                {
                  NameVal.Add(name, val);
                  error = false;
                }
                else log.Error("Invalid port value '{0}' on line {1}.", value, lineNumber);
              }
              else log.Error("Invalid port value '{0}' on line {1}.", value, lineNumber);

              break;
            }

          case ConfigValueType.IpAddress:
            {
              IPAddress val = IPAddress.Any;
              if (IPAddress.TryParse(value, out val))
              {
                NameVal.Add(name, val);
                error = false;
              }
              else log.Error("Invalid IP address interface value '{0}' on line {1}.", value, lineNumber);

              break;
            }

          case ConfigValueType.StringEmpty:
          case ConfigValueType.StringNonEmpty:
            {
              if (!string.IsNullOrEmpty(value) || (type == ConfigValueType.StringEmpty))
              {
                NameVal.Add(name, value);
                error = false;
              }
              else log.Error("Value for name '{0}' on line {1} can not be empty.", name, lineNumber);

              break;
            }

          case ConfigValueType.OnOffSwitch:
            {
              if ((value == "on") || (value == "off"))
              {
                NameVal.Add(name, value == "on");
                error = false;
              }
              else log.Error("Value for name '{0}' on line {1} can only be either 'on' or 'off'.", name, lineNumber);

              break;
            }

          default:
            log.Error("Internal error parsing line line {0}, type '{1}'.", lineNumber, type);
            break;
        }

        if (error) break;
      }

      if (!error)
      {
        // Check that all values are in NameVal dictionary.
        foreach (string key in NamesDefinition.Keys)
        {
          if (!NameVal.ContainsKey(key))
          {
            log.Error("Missing definition of '{0}'.", key);
            error = true;
            break;
          }
        }
      }

      bool res = !error;
      return res;
    }

    /// <summary>
    /// Initializes the database, or loads database configuration if the database already exists.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool InitializeDbSettings()
    {
      log.Trace("()");

      bool res = false;

      CanIpnsLastSequenceNumber = 0;
      CanProfileServerContactInformationChanged = false;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        log.Trace("Loading database settings.");
        Setting initialized = unitOfWork.SettingsRepository.Get(s => s.Name == "Initialized").FirstOrDefault();
        if ((initialized != null) && (!string.IsNullOrEmpty(initialized.Value)) && (initialized.Value == "true"))
        {
          Setting privateKeyHex = unitOfWork.SettingsRepository.Get(s => s.Name == "PrivateKeyHex").FirstOrDefault();
          Setting publicKeyHex = unitOfWork.SettingsRepository.Get(s => s.Name == "PublicKeyHex").FirstOrDefault();
          Setting expandedPrivateKeyHex = unitOfWork.SettingsRepository.Get(s => s.Name == "ExpandedPrivateKeyHex").FirstOrDefault();
          Setting networkInterface = unitOfWork.SettingsRepository.Get(s => s.Name == "NetworkInterface").FirstOrDefault();
          Setting primaryPort = unitOfWork.SettingsRepository.Get(s => s.Name == "PrimaryPort").FirstOrDefault();
          Setting canIpnsLastSequenceNumber = unitOfWork.SettingsRepository.Get(s => s.Name == "CanIpnsLastSequenceNumber").FirstOrDefault();
          Setting canProfileServerContactInformationHash = unitOfWork.SettingsRepository.Get(s => s.Name == "CanProfileServerContactInformationHash").FirstOrDefault();

          if ((privateKeyHex != null) && (!string.IsNullOrEmpty(privateKeyHex.Value))
            && (publicKeyHex != null) && (!string.IsNullOrEmpty(publicKeyHex.Value))
            && (expandedPrivateKeyHex != null) && (!string.IsNullOrEmpty(expandedPrivateKeyHex.Value))
            && (primaryPort != null)
            && (networkInterface != null) && (!string.IsNullOrEmpty(networkInterface.Value))
            && (canIpnsLastSequenceNumber != null)
            && (canProfileServerContactInformationHash != null) && (!string.IsNullOrEmpty(canProfileServerContactInformationHash.Value)))
          {
            Keys = new KeysEd25519();
            Keys.PrivateKeyHex = privateKeyHex.Value;
            Keys.PrivateKey = Crypto.FromHex(Keys.PrivateKeyHex);

            Keys.PublicKeyHex = publicKeyHex.Value;
            Keys.PublicKey = Crypto.FromHex(Keys.PublicKeyHex);

            Keys.ExpandedPrivateKeyHex = expandedPrivateKeyHex.Value;
            Keys.ExpandedPrivateKey = Crypto.FromHex(Keys.ExpandedPrivateKeyHex);

            bool error = false; 
            if (!UInt64.TryParse(canIpnsLastSequenceNumber.Value, out CanIpnsLastSequenceNumber))
            {
              log.Error("Invalid CanIpnsLastSequenceNumber value '{0}' in the database.", canIpnsLastSequenceNumber.Value);
              error = true;
            }

            if (!error)
            {
              CanProfileServerContactInformationHash = Base58Encoding.Encoder.DecodeRaw(canProfileServerContactInformationHash.Value);
              if (CanProfileServerContactInformationHash == null)
              {
                log.Error("Invalid CanProfileServerContactInformationHash value '{0}' in the database.", canProfileServerContactInformationHash.Value);
                error = true;
              }
            }

            if (!error)
            {
              // Database settings contain information on previous network interface and primary port values.
              // If they are different to what was found in the configuration file, it means the contact 
              // information of the profile server changed. Such a change must be propagated to profile server's 
              // CAN records.
              string configNetworkInterface = ServerInterface.ToString();
              if (configNetworkInterface != networkInterface.Value)
              {
                log.Info("Network interface address in configuration file is different from the database value.");

                CanProfileServerContactInformationChanged = true;
              }

              string configPrimaryPort = ServerRoles.GetRolePort(ServerRole.Primary).ToString();
              if (configPrimaryPort != primaryPort.Value)
              {
                log.Info("Primary port in configuration file is different from the database value.");

                CanProfileServerContactInformationChanged = true;
              }
            }

            res = !error;
          }
          else log.Error("Database settings are corrupted, DB has to be reinitialized.");
        }

        if (!res)
        {
          log.Info("Database settings are not initialized, initializing now ...");

          unitOfWork.SettingsRepository.Clear();
          unitOfWork.Save();

          Keys = Ed25519.GenerateKeys();

          Setting privateKey = new Setting("PrivateKeyHex", Keys.PrivateKeyHex);
          unitOfWork.SettingsRepository.Insert(privateKey);

          Setting publicKey = new Setting("PublicKeyHex", Keys.PublicKeyHex);
          unitOfWork.SettingsRepository.Insert(publicKey);

          Setting expandedPrivateKey = new Setting("ExpandedPrivateKeyHex", Keys.ExpandedPrivateKeyHex);
          unitOfWork.SettingsRepository.Insert(expandedPrivateKey);

          Setting networkInterface = new Setting("NetworkInterface", ServerInterface.ToString());
          unitOfWork.SettingsRepository.Insert(networkInterface);

          Setting primaryPort = new Setting("PrimaryPort", ServerRoles.GetRolePort(ServerRole.Primary).ToString());
          unitOfWork.SettingsRepository.Insert(primaryPort);

          Setting canIpnsLastSequenceNumber = new Setting("CanIpnsLastSequenceNumber", "0");
          unitOfWork.SettingsRepository.Insert(canIpnsLastSequenceNumber);

          initialized = new Setting("Initialized", "true");
          unitOfWork.SettingsRepository.Insert(initialized);


          if (unitOfWork.Save())
          {
            log.Info("Database initialized successfully.");

            CanProfileServerContactInformationChanged = true;
            res = true;
          }
          else log.Error("Unable to save settings to DB.");
        }
      }

      if (res)
      {
        log.Debug("Server public key hex is '{0}'.", Keys.PublicKeyHex);
        log.Debug("Server network ID is '{0}'.", Crypto.Sha256(Keys.PublicKey).ToHex());
        log.Debug("Server network ID in CAN endoing is '{0}'.", Network.CAN.CanApi.PublicKeyToId(Keys.PublicKey).ToBase58());
        log.Debug("Server primary interface is '{0}:{1}'.", ServerInterface, ServerRoles.GetRolePort(ServerRole.Primary));
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
