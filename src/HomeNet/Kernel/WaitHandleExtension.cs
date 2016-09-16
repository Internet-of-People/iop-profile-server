using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HomeNet.Kernel
{
  /// <summary>
  /// This class is used to create a Task from Event object.
  /// The created task completes once the event object is set.
  /// </summary>
  public static class WaitHandleExtension
  {
    private static NLog.Logger log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Creates Task from WaitHandle without timeout.
    /// </summary>
    /// <param name="Handle">Wait handle to trigger task completion.</param>
    /// <returns>Task that completes as the wait handle is set.</returns>
    public static Task AsTask(this WaitHandle Handle)
    {
      return AsTask(Handle, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Creates Task from WaitHandle with a timeout.
    /// </summary>
    /// <param name="Handle">Wait handle to trigger task completion.</param>
    /// <param name="Timeout">Specifies the time out after which the task is cancelled.</param>
    /// <returns>Task that completes as the wait handle is set, or cancelled after the specified period of time.</returns>
    public static Task AsTask(this WaitHandle Handle, TimeSpan Timeout)
    {
      log.Trace("(Handle:{0},Timeout:{1})", Handle, Timeout);

      TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
      RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(Handle, WaitOrTimerCallback, tcs, Timeout, true);
      tcs.Task.ContinueWith(Unregister, registration, TaskScheduler.Default);

      log.Trace("(-):*.HashCode={0}", tcs.Task.GetHashCode());
      return tcs.Task;
    }

    /// <summary>
    /// Callback routine that is called once the wait handle is set or the timeout occurs.
    /// </summary>
    /// <param name="state">Task completion source.</param>
    /// <param name="TimedOut">true if the time out occurred, false if the wait handle was set.</param>
    private static void WaitOrTimerCallback(object state, bool TimedOut)
    {
      log.Trace("(TimedOut:{0})", TimedOut);      

      TaskCompletionSource<object> tcs = (TaskCompletionSource<object>)state;
      if (TimedOut) tcs.TrySetCanceled();
      else tcs.TrySetResult(null);

      log.Trace("(-)");
    }

    /// <summary>
    /// Unregisters wait handle once the task is completed or cancelled.
    /// </summary>
    /// <param name="Task">Task that has been completed or cancelled.</param>
    /// <param name="State">Associated wait handle.</param>
    private static void Unregister(Task<object> Task, object State)
    {
      log.Trace("(Task.HashCode:{0})", Task.GetHashCode());

      RegisteredWaitHandle handle = (RegisteredWaitHandle)State;
      handle.Unregister(null);

      log.Trace("(-)");
    }
  }
}
