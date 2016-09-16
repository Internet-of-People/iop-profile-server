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
  public static class Utils
  {
    /// <summary>Length of the message prefix in bytes that contains the message length.</summary>
    public const int HeaderSize = 4;

    /// <summary>Maximal size of the message.</summary>
    public const int MaxSize = 1 * 1024 * 1024;

    /// <summary>
    /// Converts 
    /// </summary>
    /// <param name="Data"></param>
    /// <returns></returns>
    public static byte[] GetMessageBytes(Message Data)
    {
      int size = Data.CalculateSize();
      byte[] bytes = new byte[HeaderSize + size];
      byte[] header = GetBytesLittleEndian((uint)size);
      Array.Copy(header, 0, bytes, 0, HeaderSize);
      Array.Copy(Data.ToByteArray(), 0, bytes, HeaderSize, size);
      return bytes;
    }

    /// <summary>
    /// Obtains 64-bit Unix timestamp.
    /// </summary>
    /// <returns>64-bit Unix timestamp.</returns>
    public static long GetUnixTimestamp()
    {
      long res = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
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
    /// <param name="Data">4 bytes long byte array with encoded value.</param>
    /// <returns>Decoded integer value.</returns>
    public static uint GetValueLittleEndian(byte[] Data)
    {
      byte b1 = Data[0];
      byte b2 = Data[1];
      byte b3 = Data[2];
      byte b4 = Data[3];

      uint res = b1 + (uint)(b2 << 8) + (uint)(b3 << 16) + (uint)(b4 << 24);
      return res;
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
