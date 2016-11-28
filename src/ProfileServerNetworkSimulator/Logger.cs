using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetSimulator
{
  /// <summary>
  /// NLog wrapper class to enable a simple logging with a prefix to be put in front of each message.
  /// </summary>
  public class PrefixLogger
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
    public PrefixLogger(string Name, string Prefix)
    {
      name = Name;
      log = NLog.LogManager.GetLogger(Name);
      prefix = Prefix;
      wrapperType = typeof(PrefixLogger);
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
      log.Log(wrapperType, new NLog.LogEventInfo(Level, name, msg));
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
  }
}
