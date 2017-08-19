using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using IopCommon;
using System.Threading.Tasks;

namespace IopServerCore.Kernel
{
  /// <summary>Type of job handler to be called when the job timer triggers.</summary>
  public delegate void CronJobHandler();

  /// <summary>
  /// Description of a job to execute periodically.
  /// </summary>
  public class CronJob : IDisposable
  {
    /// <summary>Name of the job.</summary>
    public string Name;

    /// <summary>How quickly (in milliseconds) after the component start will its timer signal for the first time.</summary>
    public int StartDelay;

    /// <summary>Interval (in milliseconds) of the job timer.</summary>
    public int Interval;

    /// <summary>Job handler to be called when the job timer triggers.</summary>
    public CronJobHandler HandlerAsync;

    /// <summary>Timer that signals the event when it triggers.</summary>
    private Timer timer;

    /// <summary>Event that is signalled when the job should be executed.</summary>
    private AutoResetEvent @event = new AutoResetEvent(false);
    /// <summary>Event that is signalled when the job should be executed.</summary>
    public AutoResetEvent Event { get { return @event; } }



    /// <summary>Creates and starts the job's timer.</summary>
    /// <param name="TimerCallback">Callback routine to call when the timer triggers.</param>
    public void SetTimer(TimerCallback TimerCallback)
    {
      timer = new Timer(TimerCallback, @event, StartDelay, Interval);
    }

    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>Lock object to protect access to the object while disposing.</summary>
    private object disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class.
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected virtual void Dispose(bool Disposing)
    {
      bool disposedAlready = false;
      lock (disposingLock)
      {
        disposedAlready = disposed;
        disposed = true;
      }
      if (disposedAlready) return;

      if (Disposing)
      {
        if (timer != null) timer.Dispose();
        timer = null;
      }
    }
  }


  /// <summary>
  /// The Cron component is responsible for executing jobs in periodical fashion.
  /// </summary>
  public class Cron : Component
  {
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Kernel.Cron";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("IopServerCore." + ComponentName);


    /// <summary>Lock object to protect access to jobs.</summary>
    private object jobsLock = new object();

    /// <summary>List of cron jobs mapped by their names.</summary>
    private Dictionary<string, CronJob> jobs = new Dictionary<string, CronJob>();

    /// <summary>Event that is set when executiveThread is not running.</summary>
    private ManualResetEvent executiveThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is waiting for signals to perform checks.</summary>
    private Thread executiveThread;

    /// <summary>Event that is signalled when a new job was added to the list of jobs.</summary>
    private AutoResetEvent newJobEvent = new AutoResetEvent(false);


    /// <summary>
    /// Initializes the component.
    /// </summary>
    public Cron():
      base(ComponentName)
    {
    }



    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        executiveThread = new Thread(new ThreadStart(ExecutiveThread));
        executiveThread.Start();

        res = true;
        Initialized = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();

        if ((executiveThread != null) && !executiveThreadFinished.WaitOne(10000))
          log.Error("Executive thread have not terminated in 10 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      lock (jobsLock)
      {
        foreach (CronJob job in jobs.Values)
          job.Dispose();
      }

      if ((executiveThread != null) && !executiveThreadFinished.WaitOne(10000))
        log.Error("Executive thread did not terminated in 10 seconds.");

      log.Info("(-)");
    }



    /// <summary>
    /// Thread that is waiting for signals to perform checks.
    /// </summary>
    private void ExecutiveThread()
    {
      log.Info("()");

      executiveThreadFinished.Reset();

      List<WaitHandle> handleList = new List<WaitHandle>();
      handleList.Add(ShutdownSignaling.ShutdownEvent);
      handleList.Add(newJobEvent);
      
      WaitHandle[] handles = handleList.ToArray();

      while (!ShutdownSignaling.IsShutdown)
      {
        log.Info("Waiting for event.");

        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == ShutdownSignaling.ShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        if (handles[index] == newJobEvent)
        {
          handleList.Clear();
          handleList.Add(ShutdownSignaling.ShutdownEvent);
          handleList.Add(newJobEvent);

          lock (jobsLock)
          {
            foreach (CronJob cronJob in jobs.Values)
              handleList.Add(cronJob.Event);
          }

          handles = handleList.ToArray();
          continue;
        }

        CronJob job = null;
        lock (jobsLock)
        {
          foreach (CronJob cronJob in jobs.Values)
          {
            if (handles[index] == cronJob.Event)
            {
              job = cronJob;
              break;
            }
          }
        }

        log.Trace("Job '{0}' activated.", job.Name);
        #warning TODO: Async void is bad. Use Task.Wait() here at least
        job.HandlerAsync();
      }

      executiveThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Callback routine of cron timers.
    /// We simply set an event to be handled by maintenance thread, not to occupy the timer for a long time.
    /// </summary>
    /// <param name="State">Event to signal.</param>
    private void SignalTimerCallback(object State)
    {
      log.Trace("()");

      AutoResetEvent eventToSignal = (AutoResetEvent)State;
      eventToSignal.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Signals an event of the given cron job.
    /// </summary>
    /// <param name="JobName"></param>
    public void SignalEvent(string JobName)
    {
      log.Trace("(JobName:'{0}')", JobName);

      lock (jobsLock)
      {
        AutoResetEvent signalEvent = jobs[JobName].Event;
        signalEvent.Set();
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Adds a single job to cron.
    /// </summary>
    /// <param name="Job">Job to add to cron.</param>
    public void AddJob(CronJob Job)
    {
      AddJobs(new List<CronJob>() { Job });
    }


    /// <summary>
    /// Adds multiple jobs to cron.
    /// </summary>
    /// <param name="Jobs">Jobs to add to cron.</param>
    public void AddJobs(List<CronJob> Jobs)
    {
      log.Trace("()");

      foreach (CronJob job in Jobs)
        job.SetTimer(SignalTimerCallback);

      lock (jobsLock)
      {
        foreach (CronJob job in Jobs)
          jobs.Add(job.Name, job);
      }

      newJobEvent.Set();

      log.Trace("(-)");
    }
  }
}
