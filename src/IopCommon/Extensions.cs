﻿using IopCommon.Multiformats;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace IopCommon
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
    /// Converts integer to uppercase hexadecimal string with "0x" prefix.
    /// </summary>
    /// <param name="Value">Integer value.</param>
    /// <returns>Hexadecimal representation of the integer value.</returns>
    public static string ToHex(this int Value)
    {
      return string.Format("0x{0:X8}", Value);
    }

    /// <summary>
    /// Converts long to uppercase hexadecimal string with "0x" prefix.
    /// </summary>
    /// <param name="Value">long value.</param>
    /// <returns>Hexadecimal representation of the long value.</returns>
    public static string ToHex(this long Value)
    {
      return string.Format("0x{0:X16}", Value);
    }

    /// <summary>
    /// Converts ulong to uppercase hexadecimal string with "0x" prefix.
    /// </summary>
    /// <param name="Value">ulong value.</param>
    /// <returns>Hexadecimal representation of the ulong value.</returns>
    public static string ToHex(this ulong Value)
    {
      return string.Format("0x{0:X16}", Value);
    }


    /// <summary>
    /// Converts byte array to uppercase hexadecimal string.
    /// </summary>
    /// <param name="Value">ulong value.</param>
    /// <returns>Hexadecimal representation of the ulong value.</returns>
    public static string ToHex(this byte[] Value)
    {
      if (Value == null) return "";

      StringBuilder hex = new StringBuilder(Value.Length * 2);
      foreach (byte b in Value)
        hex.AppendFormat("{0:X2}", b);
      return hex.ToString();
    }


    /// <summary>
    /// Converts a binary data to an uppercase hexadecimal string representation with string length limit.
    /// </summary>
    /// <param name="Data">Data to convert to hex string.</param>
    /// <param name="Limit">Maximal number of chars of the final string.</param>
    /// <returns>Uppercase hex string representing the data.</returns>
    public static string ToHex(this byte[] Data, int Limit)
    {
      return Data.ToHex().SubstrMax(Limit);
    }


    /// <summary>
    /// Converts a binary data to a base58 string representation.
    /// </summary>
    /// <param name="Data">Data to convert to base58 string.</param>
    /// <param name="IncludePrefix">true if multibase prefix should be included.</param>
    /// <returns>Base58 string representing the data.</returns>
    public static string ToBase58(this byte[] Data, bool IncludePrefix = false)
    {
      return IncludePrefix ? Base58Encoding.Encoder.Encode(Data) : Base58Encoding.Encoder.EncodeRaw(Data);
    }

    /// <summary>
    /// Converts a binary data to a base64 URL form with padding string representation.
    /// </summary>
    /// <param name="Data">Data to convert to base64 string.</param>
    /// <param name="IncludePrefix">true if multibase prefix should be included.</param>
    /// <returns>Base64 URL form with padding string representing the data.</returns>
    public static string ToBase64UrlPad(this byte[] Data, bool IncludePrefix = false)
    {
      string res = Convert.ToBase64String(Data).Replace('+', '-').Replace('/', '_');
      return IncludePrefix ? 'U' + res : res;
    }


    /// <summary>
    /// Converts a hexadecimal string representation of data to a byte array.
    /// </summary>
    /// <param name="HexString">Hex string.</param>
    /// <returns>Data in binary format.</returns>
    public static byte[] FromHex(this string HexString)
    {
      byte[] res = new byte[HexString.Length / 2];
      for (int index = 0; index < res.Length; index++)
      {
        string byteValue = HexString.Substring(index * 2, 2);
        res[index] = byte.Parse(byteValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
      }

      return res;
    }

    /// <summary>
    /// Returns integer value of a hexadecimal character.
    /// </summary>
    /// <param name="HexChar"></param>
    /// <returns>Integer value of the hexadecimal character.</returns>
    private static int GetHexVal(char HexChar)
    {
      int val = HexChar;
      return val - (val < 58 ? '0' : (val < 97 ? 55 : 87));
    }
  }


  /// <summary>
  /// Implements various helper extension methods on IPAddress class.
  /// </summary>
  public static class IPAddressExtensions
  {
    /// <summary>
    /// Converts IP address to BigInteger.
    /// </summary>
    /// <param name="Ip">IP address to convert.</param>
    /// <returns>BigInteger representation of the IP address.</returns>
    public static BigInteger ToBigInteger(this IPAddress Ip)
    {
      BigInteger result = 0;
      byte[] bytes = Ip.GetAddressBytes();

      int bitIndex = 8 * (bytes.Length - 1);
      for (int i = 0; i < bytes.Length; i++)
      {
        result |= (BigInteger)(bytes[i]) << bitIndex;
        bitIndex -= 8;
      }

      return result;
    }

    /// <summary>
    /// Checks whether an IP address is within a range of two inclusive boundary addresses.
    /// </summary>
    /// <param name="Address">IP address to check.</param>
    /// <param name="IpFrom">IP address defining the lower bound of the range.</param>
    /// <param name="IpTo">IP address defining the upper bound of the range.</param>
    /// <returns>true if the IP address is within the given range.</returns>
    public static bool IsWithinRange(this IPAddress Address, IPAddress IpFrom, IPAddress IpTo)
    {
      BigInteger addrVal = Address.ToBigInteger();
      BigInteger fromVal = IpFrom.ToBigInteger();
      BigInteger toVal = IpTo.ToBigInteger();

      return (fromVal <= addrVal) && (addrVal <= toVal);
    }

    /// <summary>
    /// Converts IP address to a string in uncompressed format.
    /// This format is the same for IPv4 as the usual format of the default ToString method,
    /// so the real difference is only for IPv6 addresses.
    /// </summary>
    /// <param name="Address">IP address to convert.</param>
    /// <returns>String representation of an IP address.</returns>
    public static string ToUncompressedString(this IPAddress Address)
    {
      byte[] bytes = Address.GetAddressBytes();
      string result = Address.AddressFamily == AddressFamily.InterNetwork ? Address.ToString() : string.Format("{0:x2}{1:x2}:{2:x2}{3:x2}:{4:x2}{5:x2}:{6:x2}{7:x2}:{8:x2}{9:x2}:{10:x2}{11:x2}:{12:x2}{13:x2}:{14:x2}{15:x2}",
        bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7], bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15]);
      return result;
    }

    /// <summary>
    /// Parses a range string "IP1 - IP2" to two IP addresses that defines the range.
    /// </summary>
    /// <param name="RangeString">Range string to parse.</param>
    /// <param name="RangeFrom">If the function is successful, this is filled with the IP address that defines the lower bound of the range.</param>
    /// <param name="RangeTo">If the function is successful, this is filled with the IP address that defines the upper bound of the range.</param>
    /// <returns>true if the function is successful, false otherwise.</returns>
    public static bool RangeStringToAddresses(string RangeString, out IPAddress RangeFrom, out IPAddress RangeTo)
    {
      bool res = false;

      RangeFrom = null;
      RangeTo = null;

      string[] parts = RangeString.Split(new char[] { '-' });
      if ((parts != null) && (parts.Length == 2))
      {
        IPAddress ipFrom, ipTo;
        if (IPAddress.TryParse(parts[0].Trim(), out ipFrom) && IPAddress.TryParse(parts[1].Trim(), out ipTo))
        {
          RangeFrom = ipFrom;
          RangeTo = ipTo;
          res = true;
        }
      }

      return res;
    }

    /// <summary>
    /// Checks whether an IP address is within a specific IP address range.
    /// </summary>
    /// <param name="IpAddress">IP address to check.</param>
    /// <param name="RangeString">IP address range string in format "IP1 - IP2".</param>
    /// <returns>true if the IP address is within the range, false otherwise.</returns>
    public static bool IsWithinRangeString(this IPAddress IpAddress, string RangeString)
    {
      bool res = false;
      IPAddress rangeFrom, rangeTo;
      if (RangeStringToAddresses(RangeString, out rangeFrom, out rangeTo))
        res = IpAddress.IsWithinRange(rangeFrom, rangeTo);

      return res;
    }


    /// <summary>List of local/reserved IPv4 address ranges.</summary>
    public static List<string> ReservedIpV4Addresses = new List<string>()
    {
      "0.0.0.0 - 0.0.0.0",
      "10.0.0.0 - 10.255.255.255",
      "100.64.0.0 - 100.127.255.255",
      "127.0.0.0 - 127.255.255.255",
      "169.254.0.0 - 169.254.255.255",
      "172.16.0.0 - 172.31.255.255",
      "192.0.0.0 - 192.0.0.255",
      "192.0.2.0 - 192.0.2.255",
      "192.88.99.0 - 192.88.99.255",
      "192.168.0.0 - 192.168.255.255",
      "198.18.0.0 - 198.19.255.255",
      "198.51.100.0 - 198.51.100.255",
      "203.0.113.0 - 203.0.113.255",
      "224.0.0.0 - 239.255.255.255"
    };

    /// <summary>List of local/reserved IPv4 address ranges.</summary>
    public static List<string> ReservedIpV6Addresses = new List<string>()
    {
      "::0 - ::1",
      "100:: - 100::ffff:ffff:ffff:ffff",
      "2001:db8:: - 2001:db8:ffff:ffff:ffff:ffff:ffff:ffff",
      "fc00:: - fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff",
    };


    /// <summary>
    /// Checks if an IP address belongs to one of the reserved or local network ranges.
    /// </summary>
    /// <param name="Address">IP address to check.</param>
    /// <returns>true if the IP address is reserved or local.</returns>
    public static bool IsReservedOrLocal(this IPAddress Address)
    {
      bool res = false;

      foreach (string range in ReservedIpV4Addresses)
      {
        if (Address.IsWithinRangeString(range))
        {
          res = true;
          break;
        }
      }


      if (!res)
      {
        foreach (string range in ReservedIpV6Addresses)
        {
          if (Address.IsWithinRangeString(range))
          {
            res = true;
            break;
          }
        }
      }

      return res;
    }


    /// <summary>
    /// Safely constructs IPv4 or IPv6 address from byte array.
    /// </summary>
    /// <param name="AddressBytes">Byte array that represent IP address.</param>
    /// <returns>IP address constructed from byte array or null if the byte array does not represent a valid IPv4 or IPv6 address.</returns>
    public static IPAddress IpFromBytes(byte[] AddressBytes)
    {
      IPAddress res = null;
      try
      {
        IPAddress addr = new IPAddress(AddressBytes);
        if ((addr.AddressFamily == AddressFamily.InterNetwork) || (addr.AddressFamily == AddressFamily.InterNetworkV6))
          res = addr;
      }
      catch 
      {
      }
      return res;
    }
  }
}
