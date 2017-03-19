using IopCommon;
using IopServerCore.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Kernel
{
  /// <summary>
  /// Kernel is the core of the application logic.
  /// It is responsible for the application startup, which includes initialization of all other components.
  /// </summary>
  public static class Kernel 
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Kernel.Kernel");


    /// <summary>
    /// Initialization of all system components. The application can not run if the initialization process fails.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool Init()
    {
      log.Info("()");

      bool res = false;

      List<Component> componentList = new List<Component>()
      {
        new Config(),
        new Cron(),
        new Data.Database(),
        new Data.ImageManager(),
        new Network.Server(),
        new Network.ContentAddressNetwork(),
        new Network.LocationBasedNetwork(),
        new Network.NeighborhoodActionProcessor(),
      };

      res = Base.Init(componentList);

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources allocated by the component.
    /// </summary>
    public static void Shutdown()
    {
      log.Info("()");

      Base.Shutdown();

      log.Info("(-)");
    }
  }
}
