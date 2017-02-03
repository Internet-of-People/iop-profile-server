using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Kernel
{
  /// <summary>
  /// Kernel.Base is the core of the application logic.
  /// It is responsible for the application startup, which includes initialization of all other components.
  /// It also allows other components to access global variables, such as configuration.
  /// </summary>
  public static class Base
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Kernel.Base");

    /// <summary>Component manager instance that is used for initialization and shutdown of the components.</summary>
    public static ComponentManager Components;

    /// <summary>Mapping of component instances to their names.</summary>
    public static Dictionary<string, Component> ComponentDictionary;

    /// <summary>Global application configuration.</summary>
    public static Config.Config Configuration;


    /// <summary>
    /// Initialization of Base component. The application can not run if the initialization process fails.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool Init()
    {
      log.Info("()");

      bool res = false;

      Components = new ComponentManager();

      Configuration = new Config.Config();
      ComponentDictionary = new Dictionary<string, Component>(StringComparer.Ordinal)
      {
        { "Config.Config", Configuration },
        { "Data.Database", new Data.Database() },
        { "Data.ImageManager", new Data.ImageManager() },
        { "Network.Server", new Network.Server() },
        { "Network.ContentAddressNetwork.CanApi", new Network.CAN.CanApi() },
        { "Network.ContentAddressNetwork", new Network.CAN.ContentAddressNetwork() },
        { "Network.LocationBasedNetwork", new Network.LocationBasedNetwork() },
        { "Network.NeighborhoodActionProcessor", new Network.NeighborhoodActionProcessor() },
        { "Network.Cron", new Network.Cron() },
      };

      // The component list specifies the order in which the components are going to be initialized.
      List<Component> componentList = new List<Component>()
      {
        ComponentDictionary["Config.Config"],
        ComponentDictionary["Data.Database"],
        ComponentDictionary["Data.ImageManager"],
        ComponentDictionary["Network.Server"],
        ComponentDictionary["Network.ContentAddressNetwork.CanApi"],
        ComponentDictionary["Network.ContentAddressNetwork"],
        ComponentDictionary["Network.LocationBasedNetwork"],
        ComponentDictionary["Network.NeighborhoodActionProcessor"],
        ComponentDictionary["Network.Cron"],
      };

      res = Components.Init(componentList);

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources allocated by the component.
    /// </summary>
    public static void Shutdown()
    {
      log.Info("()");

      Components.Shutdown();

      log.Info("(-)");
    }
  }
}
