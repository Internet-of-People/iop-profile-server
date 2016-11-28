using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetProtocol
{
  /// <summary>
  /// Implements various helper extension methods.
  /// </summary>
  public static class Extensions
  {
    /// <summary>
    /// Converts degrees to radians.
    /// </summary>
    /// <param name="Value">Value in degrees.</param>
    /// <returns>Value in radians.</returns>
    public static double ToRadians(this double Value)
    {
      return (Math.PI / 180) * Value;
    }

    /// <summary>
    /// Converts radians to degrees.
    /// </summary>
    /// <param name="Value">Value in radians.</param>
    /// <returns>Value in degrees.</returns>
    public static double ToDegrees(this double Value)
    {
      return Value * (180.0 / Math.PI);
    }

    /// <summary>
    /// Method for approximate comparison of two doubles.
    /// </summary>
    /// <param name="X">First value.</param>
    /// <param name="Y">Second value.</param>
    /// <param name="Epsilon">Acceptable variance.</param>
    /// <returns>Returns true if <paramref name="X"/> is approximately equal to <paramref name="Y"/> with the given acceptable variance <paramref name="Epsilon"/>.</returns>
    public static bool ApproxEqual(this double X, double Y, double Epsilon = 0.00000001)
    {
      return Math.Abs(X - Y) < Epsilon;
    }
  }
}
