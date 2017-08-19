using IopCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IopServerCore.Kernel
{
  /// <summary>
  /// Calls initialization of other components and cares about component shutdown.
  /// </summary>
  public class ComponentManager
  {
    private static Logger log = new Logger("IopServerCore.Kernel.ComponentManager");

    /// <summary>Possible system states from the component life point of view.</summary>
    public enum SystemStateType
    {
      /// <summary>Application starts, component initialization has not been started yet.</summary>
      Startup,

      /// <summary>Components are being initialized.</summary>
      Initiating,

      /// <summary>Application is up and running.</summary>
      Running,

      /// <summary>Application shutdown has been initiated.</summary>
      Shutdown
    };

    /// <summary>Current system state.</summary>
    public SystemStateType SystemState { get; private set; }


    /// <summary>Global shutdown signaling object.</summary>
    public ComponentShutdown GlobalShutdown = new ComponentShutdown(null);

    /// <summary>List of application components for initialization and shutdown.</summary>
    private List<Component> componentList;

    /// <summary>
    /// Initializes component manager, which leads to initialization of all other application components.
    /// </summary>
    /// <param name="ComponentList">List of components to initialize.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool Init(List<Component> ComponentList)
    {
      log.Info("()");

      bool res = false;
      SystemState = SystemStateType.Initiating;

      componentList = ComponentList;

      try
      {
        bool error = false;
        foreach (Component comp in componentList)
        {
          string name = comp.InternalComponentName;
          log.Info("Initializing component '{0}'.", name);
          if (!comp.Init())
          {
            log.Error("Initialization of component '{0}' failed.", name);
            error = true;
            break;
          }
        }

        if (!error)
        {
          SystemState = SystemStateType.Running;
          res = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res) Shutdown();

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Changes shutdown objects to signalled state.
    /// </summary>
    public void SignalShutdown()
    {
      log.Info("()");

      SystemState = SystemStateType.Shutdown;
      GlobalShutdown.SignalShutdown();

      log.Info("(-)");
    }

    /// <summary>
    /// Frees resources allocated by the component, which leads to calling Shutdown methods of all initialized components.
    /// </summary>
    public void Shutdown()
    {
      log.Info("()");

      SignalShutdown();
      try
      {
        List<Component> componentReverseList = new List<Component>(componentList);
        componentReverseList.Reverse();

        foreach (Component comp in componentReverseList)
        {
          if (comp.Initialized)
          {
            string name = comp.InternalComponentName;
            log.Info("Shutting down component '{0}'.", name);
            comp.ShutdownSignaling.SignalShutdown();
            comp.Shutdown();
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Info("(-)");
    }
  }
}
