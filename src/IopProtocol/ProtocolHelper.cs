using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IopProtocol
{
  /// <summary>
  /// Helper functions and constants used for handling protocol messages across all parts of IoP protocol.
  /// </summary>
  public static class ProtocolHelper
  {
    /// <summary>Length of the message prefix in bytes that contains the message length.</summary>
    public const int HeaderSize = 5;

    /// <summary>Maximal size of the message.</summary>
    public const int MaxMessageSize = 1 * 1024 * 1024;

    /// <summary>Length in bytes of SHA256 hash.</summary>
    public const int HashLengthBytes = 32;

    /// <summary>Length in bytes of network identifiers.</summary>
    public const int NetworkIdentifierLength = HashLengthBytes;

    /// <summary>Length of IPv6 address in binary format.</summary>
    public const int IpAddressMaxLengthBytes = 16;

    /// <summary>Maximum number of bytes that public key can occupy.</summary>
    public const int MaxPublicKeyLengthBytes = 128;

    /// <summary>Maximum number of bytes that a signature can occupy.</summary>
    public const int MaxSignatureLengthBytes = 100;


    /// <summary>
    /// Obtains 64-bit Unix timestamp with milliseconds precision.
    /// </summary>
    /// <returns>64-bit Unix timestamp with milliseconds precision.</returns>
    public static long GetUnixTimestampMs()
    {
      return DateTimeToUnixTimestampMs(DateTime.UtcNow);
    }

    /// <summary>
    /// Converts 64-bit Unix timestamp with milliseconds precision to DateTime.
    /// </summary>
    /// <param name="UnixTimeStampMs">Unix timestamp to convert.</param>
    /// <returns>Corresponding DateTime or null if the given timestamp is not valid.</returns>
    public static DateTime? UnixTimestampMsToDateTime(long UnixTimeStampMs)
    {
      DateTime? res = null;
      try
      {
        res = new DateTime(1970, 1, 1).AddMilliseconds(UnixTimeStampMs);
      }
      catch
      {

      }
      return res;
    }


    /// <summary>
    /// Converts DateTime to 64-bit Unix timestamp with milliseconds precision.
    /// </summary>
    /// <param name="Date">Date time information to convert.</param>
    /// <returns>64-bit Unix timestamp with milliseconds precision.</returns>
    public static long DateTimeToUnixTimestampMs(DateTime Date)
    {
      long res = (long)(Date.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
      return res;
    }


    /// <summary>
    /// Encodes a 32-bit unsigned integer value to a little endian byte array.
    /// </summary>
    /// <param name="Value">Integer value to encode.</param>
    /// <returns>4 bytes long byte array with encoded value.</returns>
    public static byte[] GetBytesLittleEndian(uint Value)
    {
      byte b1 = (byte)((Value >> 0) & 0xff);
      byte b2 = (byte)((Value >> 8) & 0xff);
      byte b3 = (byte)((Value >> 16) & 0xff);
      byte b4 = (byte)((Value >> 24) & 0xff);

      byte[] res = new byte[] { b1, b2, b3, b4 };
      return res;
    }

    /// <summary>
    /// Decodes 32-bit unsigned integer value from a little endian byte array.
    /// </summary>
    /// <param name="Data">Byte array containing 4 byte long subarray with encoded value.</param>
    /// <param name="Offset">Offset of the 4 byte long subarray within the array.</param>
    /// <returns>Decoded integer value.</returns>
    public static uint GetValueLittleEndian(byte[] Data, int Offset)
    {
      byte b1 = Data[Offset + 0];
      byte b2 = Data[Offset + 1];
      byte b3 = Data[Offset + 2];
      byte b4 = Data[Offset + 3];

      uint res = b1 + (uint)(b2 << 8) + (uint)(b3 << 16) + (uint)(b4 << 24);
      return res;
    }

    /// <summary>
    /// Converts byte array to Protobuf ByteString.
    /// </summary>
    /// <param name="Data">Byte array to convert.</param>
    /// <returns>Protobuf ByteString representation of byte array.</returns>
    public static ByteString ByteArrayToByteString(byte[] Data)
    {
      return ByteString.CopyFrom(Data);
    }
  }
}
