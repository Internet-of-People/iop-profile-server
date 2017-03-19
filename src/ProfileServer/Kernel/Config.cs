using ProfileServer.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using IopCrypto;
using ProfileServer.Data.Models;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.IO;
using ProfileServer.Network;
using System.Globalization;
using IopCommon.Multiformats;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Network.CAN;

namespace ProfileServer.Kernel
{

  /// <summary>
  /// Provides access to the global configuration. The configuration is partly stored in the configuration file 
  /// and partly in the database. The instance of the configuration class is accessible via <c>Kernel.Base.Configuration</c>.
  /// </summary>
  /// <remarks>
  /// Loading configuration is essential for the profile server's startup. If any part of it fails, the profile server will refuse to start.
  /// </remarks>
  public class Config : ConfigBase
  {
    /// <summary>Instance logger.</summary>
    protected new Logger log = new Logger("ProfileServer." + ComponentName);

    /// <summary>Default name of the configuration file.</summary>
    public const string ConfigFileName = "ProfileServer.conf";

    /// <summary>Instance of the configuration component to be easily referenced by other components.</summary>
    public static Config Configuration;

    /// <summary>Certificate to be used for TCP TLS server. </summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public X509Certificate TcpServerTlsCertificate;

    /// <summary>Description of role servers.</summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public ConfigServerRoles ServerRoles;

    /// <summary>Cryptographic keys of the server that can be used for signing messages and verifying signatures.</summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public KeysEd25519 Keys;

    /// <summary>Specification of a machine's network interface, on which the profile server will listen.</summary>
    /// <remarks>This has to initialized by the derived class.</remarks>
    public IPAddress BindToInterface;

    /// <summary>External IP address of the server from its network peers' point of view.</summary>
    public IPAddress ExternalServerAddress;

    /// <summary>Path to the directory where images are stored.</summary>
    public string ImageDataFolder;

    /// <summary>Path to the directory where temporary files are stored.</summary>
    public string TempDataFolder;

    /// <summary>Maximal total number of identities hosted by this profile server.</summary>
    public int MaxHostedIdentities;

    /// <summary>Maximum number of relations that an identity is allowed to have. Must be lower than RelatedIdentity.MaxIdentityRelations.</summary>
    public int MaxIdenityRelations;

    /// <summary>Maximum number of parallel neighborhood initialization processes that the profile server is willing to process.</summary>
    public int NeighborhoodInitializationParallelism;

    /// <summary>End point of the Location Based Network server.</summary>
    public IPEndPoint LocEndPoint;

    /// <summary>End point of the Content Address Network server.</summary>
    public IPEndPoint CanEndPoint;

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

    /// <summary>
    /// CAN hash of profile server's contact information object in CAN loaded from the database.
    /// This information may not reflect the current contact information hash. That one is stored in ContentAddressNetwork.canContactInformationHash.
    /// This is used for initialization only.
    /// </summary>
    public byte[] CanProfileServerContactInformationHash;

    /// <summary>True if the profile server's contact information loaded from the database differs from the one loaded from the configuration file.</summary>
    public bool CanProfileServerContactInformationChanged;





    public override bool Init()
    {
      log.Trace("()");

      bool res = false;
      Configuration = this;

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
    /// <para>Loads global configuration from a string array that corresponds to lines of configuration file.</para>
    /// <seealso cref="LoadConfigurationFromFile"/>
    /// </summary>
    /// <param name="Lines">Profile server configuration as a string array.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public override bool LoadConfigurationFromStringArray(string[] Lines)
    {
      log.Trace("()");

      bool error = false;
      if ((Lines != null) && (Lines.Length > 0))
      {
        bool testModeEnabled = false;
        string tcpServerTlsCertificateFileName = null;
        X509Certificate tcpServerTlsCertificate = null;
        ConfigServerRoles serverRoles = null;
        IPAddress externalServerAddress = null;
        IPAddress bindToInterface = null;
        string imageDataFolder = null;
        string tempDataFolder = null;
        int maxHostedIdentities = 0;
        int maxIdentityRelations = 0;
        int neighborhoodInitializationParallelism = 0;
        int locPort = 0;
        int canPort = 0;
        IPEndPoint locEndPoint = null;
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
          { "external_server_address",                 ConfigValueType.IpAddress      },
          { "bind_to_interface",                       ConfigValueType.IpAddress      },
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
          { "loc_port",                                ConfigValueType.Port           },
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
          externalServerAddress = (IPAddress)nameVal["external_server_address"];
          bindToInterface = (IPAddress)nameVal["bind_to_interface"];
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

          locPort = (int)nameVal["loc_port"];
          canPort = (int)nameVal["can_api_port"];

          neighborProfilesExpirationTimeSeconds = (int)nameVal["neighbor_profiles_expiration_time"];
          followerRefreshTimeSeconds = (int)nameVal["follower_refresh_time"];

          maxNeighborhoodSize = (int)nameVal["max_neighborhood_size"];
          maxFollowerServersCount = (int)nameVal["max_follower_servers_count"];


          serverRoles = new ConfigServerRoles();          
          error = !(serverRoles.AddRoleServer(primaryInterfacePort, (uint)ServerRole.Primary, false, Server.ServerKeepAliveIntervalMs)
                 && serverRoles.AddRoleServer(serverNeighborInterfacePort, (uint)ServerRole.ServerNeighbor, true, Server.ServerKeepAliveIntervalMs)
                 && serverRoles.AddRoleServer(clientNonCustomerInterfacePort, (uint)ServerRole.ClientNonCustomer, true, Server.ClientKeepAliveIntervalMs)
                 && serverRoles.AddRoleServer(clientCustomerInterfacePort, (uint)ServerRole.ClientCustomer, true, Server.ClientKeepAliveIntervalMs)
                 && serverRoles.AddRoleServer(clientAppServiceInterfacePort, (uint)ServerRole.ClientAppService, true, Server.ClientKeepAliveIntervalMs));
        }

        if (!error)
        {
          if (!testModeEnabled && externalServerAddress.IsReservedOrLocal())
          {
            log.Error("external_server_address must be an IP address of external, publicly accessible interface.");
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
            if (locPort == rsc.Port)
            {
              log.Error("loc_port {0} collides with port of server role {1}.", locPort, rsc.Roles);
              error = true;
              break;
            }
          }

          if (!error)
            locEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), locPort);
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

          if (canPort == locPort)
          {
            log.Error("can_api_port {0} collides with loc_port.", canPort);
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
          Settings["TestModeEnabled"] = TestModeEnabled;

          ExternalServerAddress = externalServerAddress;
          Settings["ExternalServerAddress"] = ExternalServerAddress;

          BindToInterface = bindToInterface;
          Settings["BindToInterface"] = BindToInterface;

          ServerRoles = serverRoles;
          Settings["ServerRoles"] = ServerRoles;

          TcpServerTlsCertificate = tcpServerTlsCertificate;
          Settings["TcpServerTlsCertificate"] = TcpServerTlsCertificate;

          ImageDataFolder = imageDataFolder;
          Settings["ImageDataFolder"] = ImageDataFolder;

          TempDataFolder = tempDataFolder;
          Settings["TempDataFolder"] = TempDataFolder;

          MaxHostedIdentities = maxHostedIdentities;
          Settings["MaxHostedIdentities"] = MaxHostedIdentities;

          MaxIdenityRelations = maxIdentityRelations;
          Settings["MaxIdenityRelations"] = MaxIdenityRelations;

          NeighborhoodInitializationParallelism = neighborhoodInitializationParallelism;
          Settings["NeighborhoodInitializationParallelism"] = NeighborhoodInitializationParallelism;

          LocEndPoint = locEndPoint;
          Settings["LocEndPoint"] = LocEndPoint;

          CanEndPoint = canEndPoint;
          Settings["CanEndPoint"] = CanEndPoint;

          NeighborProfilesExpirationTimeSeconds = neighborProfilesExpirationTimeSeconds;
          Settings["NeighborProfilesExpirationTimeSeconds"] = NeighborProfilesExpirationTimeSeconds;

          FollowerRefreshTimeSeconds = followerRefreshTimeSeconds;
          Settings["FollowerRefreshTimeSeconds"] = FollowerRefreshTimeSeconds;

          MaxNeighborhoodSize = maxNeighborhoodSize;
          Settings["MaxNeighborhoodSize"] = MaxNeighborhoodSize;

          MaxFollowerServersCount = maxFollowerServersCount;
          Settings["MaxFollowerServersCount"] = MaxFollowerServersCount;


          log.Info("New configuration loaded successfully.");
        }
      }
      else log.Error("Configuration file is empty.");

      bool res = !error;

      log.Trace("(-):{0}", res);
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
          Setting externalServerAddress = unitOfWork.SettingsRepository.Get(s => s.Name == "ExternalServerAddress").FirstOrDefault();
          Setting primaryPort = unitOfWork.SettingsRepository.Get(s => s.Name == "PrimaryPort").FirstOrDefault();
          Setting canIpnsLastSequenceNumber = unitOfWork.SettingsRepository.Get(s => s.Name == "CanIpnsLastSequenceNumber").FirstOrDefault();
          Setting canProfileServerContactInformationHash = unitOfWork.SettingsRepository.Get(s => s.Name == "CanProfileServerContactInformationHash").FirstOrDefault();

          bool havePrivateKey = (privateKeyHex != null) && !string.IsNullOrEmpty(privateKeyHex.Value);
          bool havePublicKey = (publicKeyHex != null) && !string.IsNullOrEmpty(publicKeyHex.Value);
          bool haveExpandedPrivateKey = (expandedPrivateKeyHex != null) && !string.IsNullOrEmpty(expandedPrivateKeyHex.Value);
          bool havePrimaryPort = primaryPort != null;
          bool haveExternalServerAddress = (externalServerAddress != null) && !string.IsNullOrEmpty(externalServerAddress.Value);
          bool haveCanIpnsLastSequenceNumber = canIpnsLastSequenceNumber != null;
          bool haveCanContactInformationHash = (canProfileServerContactInformationHash != null) && !string.IsNullOrEmpty(canProfileServerContactInformationHash.Value);

          if (havePrivateKey
            && havePublicKey
            && haveExpandedPrivateKey
            && havePrimaryPort
            && haveExternalServerAddress
            && haveCanIpnsLastSequenceNumber)
          {
            Keys = new KeysEd25519();
            Keys.PrivateKeyHex = privateKeyHex.Value;
            Keys.PrivateKey = Keys.PrivateKeyHex.FromHex();

            Keys.PublicKeyHex = publicKeyHex.Value;
            Keys.PublicKey = Keys.PublicKeyHex.FromHex();

            Keys.ExpandedPrivateKeyHex = expandedPrivateKeyHex.Value;
            Keys.ExpandedPrivateKey = Keys.ExpandedPrivateKeyHex.FromHex();

            bool error = false;
            if (!UInt64.TryParse(canIpnsLastSequenceNumber.Value, out CanIpnsLastSequenceNumber))
            {
              log.Error("Invalid CanIpnsLastSequenceNumber value '{0}' in the database.", canIpnsLastSequenceNumber.Value);
              error = true;
            }

            if (!error)
            {
              if (haveCanContactInformationHash)
              {
                CanProfileServerContactInformationHash = Base58Encoding.Encoder.DecodeRaw(canProfileServerContactInformationHash.Value);
                if (CanProfileServerContactInformationHash == null)
                {
                  log.Error("Invalid CanProfileServerContactInformationHash value '{0}' in the database.", canProfileServerContactInformationHash.Value);
                  error = true;
                }
              }
              else CanProfileServerContactInformationChanged = true;
            }

            if (!error)
            {
              // Database settings contain information on previous external network address and primary port values.
              // If they are different to what was found in the configuration file, it means the contact 
              // information of the profile server changed. Such a change must be propagated to profile server's 
              // CAN records.
              string configExternalServerAddress = ExternalServerAddress.ToString();
              if (configExternalServerAddress != externalServerAddress.Value)
              {
                log.Info("Network interface address in configuration file is different from the database value.");

                CanProfileServerContactInformationChanged = true;
              }

              string configPrimaryPort = ServerRoles.GetRolePort((uint)ServerRole.Primary).ToString();
              if (configPrimaryPort != primaryPort.Value)
              {
                log.Info("Primary port in configuration file is different from the database value.");

                CanProfileServerContactInformationChanged = true;
              }
            }

            res = !error;
          }
          else
          {
            log.Error("Database settings are corrupted, DB has to be reinitialized.");
            if (!havePrivateKey) log.Debug("Private key is missing.");
            if (!havePublicKey) log.Debug("Public key is missing.");
            if (!haveExpandedPrivateKey) log.Debug("Expanded private key is missing.");
            if (!havePrimaryPort) log.Debug("Primary port is missing.");
            if (!haveExternalServerAddress) log.Debug("External server address is missing.");
            if (!haveCanIpnsLastSequenceNumber) log.Debug("Last CAN IPNS sequence number is missing.");
            if (!haveCanContactInformationHash) log.Debug("CAN contact information hash is missing.");
          }
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

          Setting externalServerAddress = new Setting("ExternalServerAddress", ExternalServerAddress.ToString());
          unitOfWork.SettingsRepository.Insert(externalServerAddress);

          Setting primaryPort = new Setting("PrimaryPort", ServerRoles.GetRolePort((uint)ServerRole.Primary).ToString());
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
        Settings["Keys"] = Keys;
        Settings["CanIpnsLastSequenceNumber"] = CanIpnsLastSequenceNumber;
        Settings["CanProfileServerContactInformationHash"] = CanProfileServerContactInformationHash;
        Settings["CanProfileServerContactInformationChanged"] = CanProfileServerContactInformationChanged;        

        log.Debug("Server public key hex is '{0}'.", Keys.PublicKeyHex);
        log.Debug("Server network ID is '{0}'.", Crypto.Sha256(Keys.PublicKey).ToHex());
        log.Debug("Server network ID in CAN encoding is '{0}'.", CanApi.PublicKeyToId(Keys.PublicKey).ToBase58());
        log.Debug("Server primary external contact is '{0}:{1}'.", ExternalServerAddress, ServerRoles.GetRolePort((uint)ServerRole.Primary));
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
