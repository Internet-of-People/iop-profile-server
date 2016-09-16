using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeNet.Kernel
{
  /// <summary>
  /// Calls initialization of other components and cares about component shutdown.
  /// </summary>
  public class ComponentManager
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

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

    private SystemStateType systemState = SystemStateType.Startup;
    /// <summary>Current system state.</summary>
    public SystemStateType SystemState { get { return systemState; } }

    /// <summary>true if the application shutdown has been initiated.</summary>
    public bool IsShutdown { get { return SystemState == SystemStateType.Shutdown; } }

    /// <summary>Task that completes once ShutdownEvent is set.</summary>
    public Task ShutdownTask;

    /// <summary>Event that is set when the shutdown is initiated.</summary>
    public ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

    /// <summary>Event that is set when the shutdown process is finished.</summary>
    public ManualResetEvent ShutdownFinishedEvent = new ManualResetEvent(false);

    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the system shutdown is initiated.</summary>
    public CancellationTokenSource ShutdownCancellationTokenSource = new CancellationTokenSource();

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
      systemState = SystemStateType.Initiating;
      ShutdownTask = WaitHandleExtension.AsTask(ShutdownEvent);

      componentList = ComponentList;

      try
      {
        bool error = false;
        foreach (Component comp in componentList)
        {
          string name = comp.GetType().Name;
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
          systemState = SystemStateType.Running;
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

      systemState = SystemStateType.Shutdown;
      ShutdownEvent.Set();
      ShutdownTask.Wait();
      ShutdownCancellationTokenSource.Cancel();

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
            string name = comp.GetType().Name;
            log.Info("Shutting down component '{0}'.", name);
            comp.SignalLocalShutdown();
            comp.Shutdown();
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      ShutdownFinishedEvent.Set();

      log.Info("(-)");
    }
  }
}
