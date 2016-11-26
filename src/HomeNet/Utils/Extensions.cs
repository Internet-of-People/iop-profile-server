using System;
using System.Threading;
using System.Threading.Tasks;

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


    /// <summary>
    /// Converts a binary data to an uppercase hexadecimal string representation.
    /// </summary>
    /// <param name="Data">Data to convert to hex string.</param>
    /// <returns>Uppercase hex string representing the data.</returns>
    public static string ToHex(this byte[] Data)
    {
      return HomeNetCrypto.Crypto.ToHex(Data);
    }

    /// <summary>
    /// Converts a binary data to an uppercase hexadecimal string representation with string length limit.
    /// </summary>
    /// <param name="Data">Data to convert to hex string.</param>
    /// <param name="Limit">Maximal number of chars of the final string.</param>
    /// <returns>Uppercase hex string representing the data.</returns>
    public static string ToHex(this byte[] Data, int Limit)
    {
      return HomeNetCrypto.Crypto.ToHex(Data).SubstrMax(Limit);
    }

    /// <summary>
    /// Converts an ulong value to hexadecimal string.
    /// </summary>
    /// <param name="Value">Value to convert to hex string.</param>
    /// <returns>Uppercase hex string representing the value.</returns>
    public static string ToHex(this ulong Value)
    {
      return string.Format("0x{0:X16}", Value);
    }
  }
}
