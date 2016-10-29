using Google.Protobuf;
using Iop.Homenode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetProtocol
{
  /// <summary>
  /// Helper functions and constants used for handling protocol messages.
  /// </summary>
  public static class ProtocolHelper
  {
    /// <summary>Length of the message prefix in bytes that contains the message length.</summary>
    public const int HeaderSize = 5;

    /// <summary>Maximal size of the message.</summary>
    public const int MaxSize = 1 * 1024 * 1024;

    /// <summary>Size in bytes of an authentication challenge data.</summary>
    public const int ChallengeDataSize = 32;

    /// <summary>
    /// Converts an IoP protocol message to a binary format.
    /// </summary>
    /// <param name="Data">IoP protocol message.</param>
    /// <returns>Binary representation of the message to be sent over the network.</returns>
    public static byte[] GetMessageBytes(Message Data)
    {
      MessageWithHeader mwh = new MessageWithHeader();
      mwh.Body = Data;
      // We have to initialize the header before calling CalculateSize.
      mwh.Header = 1;
      mwh.Header = (uint)mwh.CalculateSize() - HeaderSize;
      return mwh.ToByteArray();
    }

    /// <summary>
    /// Obtains 64-bit Unix timestamp with milliseconds precision.
    /// </summary>
    /// <returns>64-bit Unix timestamp with milliseconds precision.</returns>
    public static long GetUnixTimestampMs()
    {
      long res = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
      return res;
    }

    /// <summary>
    /// Converts 64-bit Unix timestamp with milliseconds precision to DateTime.
    /// </summary>
    /// <returns>Corresponding DateTime.</returns>
    public static DateTime UnixTimestampMsToDateTime(long UnixTimeStampMs)
    {
      return new DateTime(1970, 1, 1).AddMilliseconds(UnixTimeStampMs);
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
    /// Checks whether the version in binary form is a valid version.
    /// Note that version 0.0.0 is not a valid version.
    /// </summary>
    /// <param name="Version">Version information in binary form</param>
    /// <returns>true if the version is valid, false otherwise.</returns>
    public static bool IsValidVersion(byte[] Version)
    {
      return !((Version == null) || (Version.Length != 3) || ((Version[0] == 0) && (Version[1] == 0) && (Version[2] == 0)));
    }


    /// <summary>
    /// Converts 3 byte representation of version information to string.
    /// </summary>
    /// <param name="Version">Version information in binary form.</param>
    /// <returns>Version information as string.</returns>
    public static string VersionBytesToString(byte[] Version)
    {
      string res = "<INVALID>";
      
      if (Version.Length == 3)
        res = string.Format("{0}.{1}.{2}", Version[0], Version[1], Version[2]);

      return res;        
    }

    /// <summary>
    /// Converts version to Protobuf ByteString format.
    /// </summary>
    /// <param name="Major">Major version.</param>
    /// <param name="Minor">Minor version.</param>
    /// <param name="Patch">Patch version.</param>
    /// <returns>Version in ByteString format to be used directly in Protobuf message.</returns>
    public static ByteString VersionToByteString(byte Major, byte Minor, byte Patch)
    {
      return ByteArrayToByteString(new byte[] { Major, Minor, Patch });
    }

    /// <summary>
    /// Converts binary version to Protobuf ByteString format.
    /// </summary>
    /// <param name="Version">Binary version information.</param>
    /// <returns>Version in ByteString format to be used directly in Protobuf message.</returns>
    public static ByteString VersionToByteString(byte[] Version)
    {
      return ByteArrayToByteString(new byte[] { Version[0], Version[1], Version[2] });
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
