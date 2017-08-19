using IopCommon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IopServerCore.Kernel
{
  /// <summary>
  /// Kernel.Base is the core of the application logic.
  /// It is responsible for the application startup, which includes initialization of all other components.
  /// </summary>
  public static class Base
  {
    private static Logger log = new Logger("IopServerCore.Kernel.Base");

    /// <summary>Component manager instance that is used for initialization and shutdown of the components.</summary>
    public static ComponentManager ComponentManager = new ComponentManager();

    /// <summary>Mapping of component instances to their names.</summary>
    public static Dictionary<string, Component> ComponentDictionary;


    /// <summary>
    /// Initialization of Base component. The application can not run if the initialization process fails.
    /// </summary>
    /// <param name="Components">Ordered list of components that are going to be initialized.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public static bool Init(List<Component> ComponentList)
    {
      log.Info("()");

      // Make sure the current directory is set to the directory of the main executable.
      string path = System.Reflection.Assembly.GetEntryAssembly().Location;
      path = Path.GetDirectoryName(path);
      Directory.SetCurrentDirectory(path);


      ComponentDictionary = new Dictionary<string, Component>(StringComparer.Ordinal);

      foreach (Component component in ComponentList)
        ComponentDictionary.Add(component.InternalComponentName, component);

      bool res = ComponentManager.Init(ComponentList);

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Frees resources allocated by the component.
    /// </summary>
    public static void Shutdown()
    {
      log.Info("()");

      ComponentManager.Shutdown();

      log.Info("(-)");
    }
  }
}
