using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IopServerCore.Kernel
{
  /// <summary>
  /// Defines basic implementation of the application component.
  /// </summary>
  public abstract class Component
  {
    /// <summary>Name of the component.</summary>
    public string InternalComponentName { get; set; }

    /// <summary>Indication of whether the component has been initialized successfully or not (yet).</summary>
    public bool Initialized;

    /// <summary>Shutdown signaling object.</summary>
    public ComponentShutdown ShutdownSignaling;


    /// <summary>
    /// Initializes the component and connects its shutdown signaling to the global shutdown.
    /// </summary>
    /// <param name="Name">Name of the component.</param>
    public Component(string Name)
    {
      InternalComponentName = Name;
      ShutdownSignaling = new ComponentShutdown(Base.ComponentManager.GlobalShutdown);
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
  }
}
