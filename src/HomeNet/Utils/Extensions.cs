using System;

namespace HomeNet.Utils
{
  /// <summary>
  /// Implements various helper extension methods.
  /// </summary>
  public static class Extensions
  {
    /// <summary>
    /// Checks whether a character is a hex character.
    /// </summary>
    /// <param name="C">Character to check.</param>
    /// <returns>true if <paramref name="C"/> is a hex character.</returns>
    public static bool IsHexChar(this char C)
    {
      return ((C >= '0') && (C <= '9'))
        || ((C >= 'a') && (C <= 'f'))
        || ((C >= 'A') && (C <= 'F'));
    }

    /// <summary>
    /// Returns substring of an input string no longer than a given limit.
    /// If string is longer than the limit, the returned substring ends with '…' character.
    /// </summary>
    /// <param name="Str">Input string.</param>
    /// <param name="Limit">Maximal number of chars of the substring.</param>
    /// <returns></returns>
    public static string SubstrMax(this string Str, int Limit = 256)
    {
      if (Str.Length <= Limit)
        return Str;
      
      return Str.Substring(0, Limit - 1) + "…";
    }
  }
}
