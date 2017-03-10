using IopCommon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProfileServer.Utils
{
  /// <summary>
  /// Regular expression evaluator with timeout feature.
  /// <para>
  /// It is used for regex matching of a large number of entities against a single pattern 
  /// while controling the time spent on the matching operations.
  /// </para>
  /// <para>
  /// There are two timeout values. The first one if for a single data matching operation, which should be a very small value - i.e. 100 ms.
  /// This prevents a single evaluation whether a certain input data matches the pattern or not to take too much time.
  /// The second timeout value is for overall time spent on matching with the particular object instance.
  /// Once this timeout is reached, the instance no longer performs any matching and just returns that data does not match the pattern.
  /// </para>
  /// </summary>
  public class RegexEval
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Utils.RegexEval");

    /// <summary>Regular expression object.</summary>
    private Regex regex;

    /// <summary>Stopwatch to measure execution time.</summary>
    private Stopwatch watch;

    /// <summary>Number of ticks there remains for matching operations.</summary>
    private long totalTimeRemainingTicks;

    /// <summary>
    /// Initializes the regular expression and stop watch.
    /// </summary>
    /// <param name="RegexStr">Regular expression.</param>
    /// <param name="SingleTimeoutMs">Timeout in milliseconds for a single data matching.</param>
    /// <param name="TotalTimeoutMs">Total timeout in milliseconds for the whole matching operation over the whole set of data.</param>
    public RegexEval(string RegexStr, int SingleTimeoutMs, int TotalTimeoutMs)
    {
      log.Trace("RegexStr:'{0}',SingleTimeoutMs:{1},TotalTimeoutMs:{2}", RegexStr.SubstrMax(), SingleTimeoutMs, TotalTimeoutMs);

      regex = new Regex(RegexStr, RegexOptions.Singleline, TimeSpan.FromMilliseconds(SingleTimeoutMs));
      watch = new Stopwatch();
      totalTimeRemainingTicks = TimeSpan.FromMilliseconds(TotalTimeoutMs).Ticks;

      log.Trace("(-)");
    }

    /// <summary>
    /// Checks whether a string matches a regular expression within a given time.
    /// </summary>
    /// <param name="Data">Input string to match.</param>
    /// <returns>true if the input <paramref name="Data"/> matches the given regular expression within the given time frame
    /// and if the total time for all matching operations with this instance was not reached, false otherwise.</returns>
    public bool Matches(string Data)
    {
      if (Data == null) Data = "";
      log.Trace("Data:'{0}'", Data.SubstrMax());

      bool res = false;
      string reason = "";
      if (totalTimeRemainingTicks > 0)
      {
        try
        {
          watch.Restart();

          res = regex.IsMatch(Data);

          watch.Stop();
          totalTimeRemainingTicks -= watch.ElapsedTicks;
          log.Trace("Total time remaining is {0} ticks.", totalTimeRemainingTicks);
        }
        catch
        {
          // Timeout occurred, no match.
          reason = "[TIMEOUT]";
        }
      }
      else
      {
        // No more time left for this instance, no match.
        reason = "[TOTAL_TIMEOUT]";
      }

      log.Trace("(-){0}:{1}", reason, res);
      return res;
    }
  }
}
