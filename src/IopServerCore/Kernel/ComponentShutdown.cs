using IopCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IopServerCore.Kernel
{
  /// <summary>
  /// Implementation of shutdown signaling for a separated component
  /// with a connection to a global shutdown signaling.
  /// </summary>
  public class ComponentShutdown
  {
    private static Logger log = new Logger("IopServerCore.Kernel.ComponentShutdown");

    /// <summary>Component-defined shutdown flag. This can be used to complement the global shutdown flag.</summary>
    public volatile bool IsShutdown = false;

    /// <summary>Event that is set when the component shutdown is initiated.</summary>
    public ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

    /// <summary>Cancellation token source for asynchronous tasks that is being triggered when the component shutdown is initiated.</summary>
    public CancellationTokenSource ShutdownCancellationTokenSource = new CancellationTokenSource();

    /// <summary>Registration that connects our shutdown signaling to the global shutdown signaling.</summary>
    private RegisteredWaitHandle registration;


    /// <summary>
    /// Initializes the shutdown signaling with optional connection to a global shutdown signaling object.
    /// </summary>
    /// <param name="GlobalShutdown">
    /// Global shutdown signaling object that the newly created shutdown signaling object will be connected to.
    /// This can be null if we are initializing the global shutdown object that exists on itself.
    /// </param>
    public ComponentShutdown(ComponentShutdown GlobalShutdown)
    {
      if (GlobalShutdown == null)
        return;

      registration = ThreadPool.RegisterWaitForSingleObject(GlobalShutdown.ShutdownEvent, WaitOrTimerCallback, this, Timeout.InfiniteTimeSpan, true);
    }

    /// <summary>
    /// Callback routine that is called once the global shutdown event is set.
    /// </summary>
    /// <param name="state">Component shutdown instance.</param>
    /// <param name="TimedOut">Not used.</param>
    private static void WaitOrTimerCallback(object state, bool TimedOut)
    {
      log.Trace("()");

      ComponentShutdown cs = (ComponentShutdown)state;
      cs.registration.Unregister(null);

      log.Trace("(-)");
    }


    /// <summary>
    /// Initiates component shutdown.
    /// </summary>
    public void SignalShutdown()
    {
      log.Trace("()");

      IsShutdown = true;
      ShutdownEvent.Set();
      ShutdownCancellationTokenSource.Cancel();

      log.Trace("(-)");
    }
  }
}
