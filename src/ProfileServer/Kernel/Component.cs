using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServer.Kernel
{
  /// <summary>
  /// Defines basic implementation of the application component.
  /// </summary>
  public abstract class Component
  {
    /// <summary>Indication of whether the component has been initialized successfully or not (yet).</summary>
    public bool Initialized;

    /// <summary>Shutdown signaling object.</summary>
    public ComponentShutdown ShutdownSignaling;

    /// <summary>
    /// Initializes a component and connects its shutdown signaling to the global shutdown.
    /// </summary>
    /// <param name="GlobalShutdown">Global shutdown signaling object.</param>
    public Component()
    {
      ShutdownSignaling = new ComponentShutdown(Base.Components.GlobalShutdown);
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
