using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeNet.Kernel
{
  /// <summary>
  /// Defines basic implementation of the application component.
  /// </summary>
  public abstract class Component
  {
    /// <summary>Indication of whether the component has been initialized successfully or not (yet).</summary>
    public bool Initialized;

    /// <summary>Component-defined shutdown flag. This can be used to complement the global shutdown flag.</summary>
    protected bool isLocalShutdown;

    /// <summary>Task that completes once ShutdownEvent is set.</summary>
    protected Task localShutdownTask;

    /// <summary>Event that is set when the component shutdown is initiated.</summary>
    protected ManualResetEvent localShutdownEvent = new ManualResetEvent(false);

    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the component shutdown is initiated.</summary>
    public CancellationTokenSource localShutdownCancellationTokenSource = new CancellationTokenSource();

    public Component()
    {
      localShutdownTask = WaitHandleExtension.AsTask(localShutdownEvent);
    }

    /// <summary>
    /// Initialization method of the component, which must succeeds, otherwise the component can't be used, 
    /// which usually means that the application fails to start.
    /// </summary>
    public abstract bool Init();

    /// <summary>
    /// Stops the work of the component and frees resources used by the component.
    /// This method must be called if the initialization succeeded, but it should be implemented in a way 
    /// that it can be called even if the initialization failed, or even if it has been called already.
    /// </summary>
    public abstract void Shutdown();

    /// <summary>true if local or global shutdown is in progress.</summary>
    public bool IsShutdown
    {
      get
      {
        return isLocalShutdown || Base.Components.IsShutdown;
      }
    }

    /// <summary>Initiates component shutdown.</summary>
    public void SignalLocalShutdown()
    {
      isLocalShutdown = true;
      localShutdownEvent.Set();
      localShutdownTask.Wait();
      localShutdownCancellationTokenSource.Cancel();
    }
  }
}
