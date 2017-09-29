using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace IopCommon
{
  /// <summary>
  /// NLog wrapper class to enable a simple logging with a prefix to be put in front of each message.
  /// </summary>
  public class Logger
  {
    /// <summary>Name of the logger.</summary>
    private string name;

    /// <summary>NLog logger instance.</summary>
    private NLog.Logger log;

    /// <summary>Prefix to put in front of every message.</summary>
    private string prefix;

    /// <summary>Wrapper class type for the NLog callsite to skip it.</summary>
    private Type wrapperType;

    /// <summary>
    /// Creates a prefixed logger instance for a named logger and a given prefix.
    /// </summary>
    /// <param name="Name">Name of the logger.</param>
    /// <param name="Prefix">Prefix to be put in front of every message.</param>
    public Logger(string Name, string Prefix = null)
    {
      name = Name;
      log = LogManager.GetLogger(Name);
      prefix = Prefix != null ? Prefix : "";
      wrapperType = typeof(Logger);
    }

    /// <summary>
    /// Logs a message on a specific log level.
    /// </summary>
    /// <param name="Level">NLog log level.</param>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    private void logInternal(NLog.LogLevel Level, string Message, params object[] Args)
    {
      string msg = string.Format(prefix + Message, Args);
      log.Log(wrapperType, new LogEventInfo(Level, name, msg));
    }

    /// <summary>
    /// Logs a message on a specific log level.
    /// </summary>
    /// <param name="Level">NLog log level.</param>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    public void LogAtLevel(NLog.LogLevel Level, string Message, params object[] Args)
    {
      logInternal(Level, Message, Args);
    }

    /// <summary>
    /// Logs a prefixed message on the Trace level.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    public void Trace(string Message, params object[] Args)
    {
      logInternal(NLog.LogLevel.Trace, Message, Args);
    }

    /// <summary>
    /// Logs a prefixed message on the Debug level.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    public void Debug(string Message, params object[] Args)
    {
      logInternal(NLog.LogLevel.Debug, Message, Args);
    }

    /// <summary>
    /// Logs a prefixed message on the Info level.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    public void Info(string Message, params object[] Args)
    {
      logInternal(NLog.LogLevel.Info, Message, Args);
    }

    /// <summary>
    /// Logs a prefixed message on the Warn level.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    public void Warn(string Message, params object[] Args)
    {
      logInternal(NLog.LogLevel.Warn, Message, Args);
    }

    /// <summary>
    /// Logs a prefixed message on the Error level.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    public void Error(string Message, params object[] Args)
    {
      logInternal(NLog.LogLevel.Error, Message, Args);
    }

    /// <summary>
    /// Logs a prefixed message on the Fatal level.
    /// </summary>
    /// <param name="Message">Message to log.</param>
    /// <param name="Args">Additional arguments to format a message.</param>
    public void Fatal(string Message, params object[] Args)
    {
      logInternal(NLog.LogLevel.Fatal, Message, Args);
    }

    /// <summary>
    /// Checks if the given logging level is enabled.
    /// </summary>
    /// <param name="LogLevel">Level to be checked.</param>
    /// <returns>true if the logging level is enabled, false otherwise.</returns>
    public bool IsEnabled(NLog.LogLevel LogLevel)
    {
      return log.IsEnabled(LogLevel);
    }

    public NLog.LogLevel LogLevelMsToNlog(Microsoft.Extensions.Logging.LogLevel LogLevel)
    {
      NLog.LogLevel res = NLog.LogLevel.Trace;

      switch (LogLevel)
      {
        case Microsoft.Extensions.Logging.LogLevel.Trace: res = NLog.LogLevel.Trace; break;
        case Microsoft.Extensions.Logging.LogLevel.Debug: res = NLog.LogLevel.Debug; break;
        case Microsoft.Extensions.Logging.LogLevel.Information: res = NLog.LogLevel.Info; break;
        case Microsoft.Extensions.Logging.LogLevel.Warning: res = NLog.LogLevel.Warn; break;
        case Microsoft.Extensions.Logging.LogLevel.Error: res = NLog.LogLevel.Error; break;
        case Microsoft.Extensions.Logging.LogLevel.Critical: res = NLog.LogLevel.Fatal; break;
      }

      return res;
    }

    public T TraceFunc<T>(string message, Func<T> func)
    {
      Trace(message);
      T res = default(T);
      try
      {
          res = func();
      }
      finally
      {
          Trace("(-):" + res);
      }
      return res;
    }

    public async Task<T> TraceFuncAsync<T>(string message, Func<Task<T>> func)
    {
      Trace(message);
      T res = default(T);
      try
      {
          res = await func();
      }
      finally
      {
          Trace("(-):" + res);
      }
      return res;
    }
  }

  /// <summary>
  /// Logger for logs from the database engine.
  /// </summary>
  public class DbLogger : Microsoft.Extensions.Logging.ILogger
  {
    private Logger log;

    /// <summary>
    /// Creates a new DbLogger instance.
    /// </summary>
    /// <param name="CategoryName">The category name for messages produced by the logger.</param>
    public DbLogger(string CategoryName)
    {
      string logName = "IopCommon.DbLogger";
      log = new Logger(logName, CategoryName);
    }

    /// <summary>
    /// Checks if the given logging level is enabled.
    /// </summary>
    /// <param name="LogLevel">Level to be checked.</param>
    /// <returns>true if the logging level is enabled, false otherwise.</returns>
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel LogLevel)
    {
      return log.IsEnabled(log.LogLevelMsToNlog(LogLevel));
    }

    /// <summary>
    /// Writes a log entry.
    /// </summary>
    /// <param name="logLevel">Entry will be written on this level.</param>
    /// <param name="eventId">Id of the event.</param>
    /// <param name="state">The entry to be written. Can be also an object.</param>
    /// <param name="exception">The exception related to this entry.</param>
    /// <param name="formatter">Function to create a string message of the state and exception.</param>
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
      string message = formatter(state, exception);
      NLog.LogLevel level = log.LogLevelMsToNlog(logLevel);
      log.LogAtLevel(level, "{0}", message);
    }

    /// <summary>
    /// Begins a logical operation scope.
    /// </summary>
    /// <param name="state">The identifier for the scope.</param>
    /// <returns>An IDisposable that ends the logical operation scope on dispose.</returns>
    public IDisposable BeginScope<TState>(TState state)
    {
      return null;
    }
  }

  /// <summary>
  /// Implementation of ILoggerProvider that is needed to bind DbLogger to the database engine.
  /// </summary>
  public class DbLoggerProvider : ILoggerProvider
  {
    /// <summary>
    /// Creates a new Microsoft.Extensions.Logging.ILogger instance.
    /// </summary>
    /// <param name="CategoryName">The category name for messages produced by the logger.</param>
    /// <returns>Microsoft.Extensions.Logging.ILogger instance.</returns>
    public Microsoft.Extensions.Logging.ILogger CreateLogger(string CategoryName)
    {
      return new DbLogger(CategoryName);
    }

    /// <summary>
    /// Empty dispose method, which is required by the interface.
    /// </summary>
    public void Dispose()
    {
    }
  }

  /// <summary>
  /// Issues unique identifiers for diagnostic context that we use in logs.
  /// </summary>
  public static class LogDiagnosticContext
  {
    public const string ContextName = "id";

    /// <summary>Last issued value of the diagnostic context.</summary>
    private static int lastValue;

    /// <summary>
    /// Issues a new unique context value by incrementing the context value.
    /// The value is prefixed with space to make it easier to use directly in the logs.
    /// </summary>
    /// <returns>Unique context value to be used in NDC.</returns>
    public static string Create()
    {
      return " " + Interlocked.Increment(ref lastValue).ToString();
    }

    /// <summary>
    /// Installs mapped diagnostic context to the current execution flow.
    /// </summary>
    public static void Start()
    {
#warning TODO: When NLog 5 is released, change this to MappedDiagnosticsLogicalContext and mdlc in config.
      NLog.MappedDiagnosticsContext.Set(ContextName, Create());
    }

    /// <summary>
    /// Uninstalls mapped diagnostic context to the current execution flow.
    /// </summary>
    public static void Stop()
    {
      NLog.MappedDiagnosticsContext.Remove(ContextName);
    }
  }

}
