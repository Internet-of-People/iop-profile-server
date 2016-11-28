using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServerProtocol
{
  /// <summary>
  /// Implements VersionType from the protocol definition.
  /// Version is a represented by 3 bytes as defined by http://semver.org/.
  /// </summary>
  public struct SemVer
  {
    /// <summary>Major version information - see http://semver.org/. </summary>
    public byte Major;

    /// <summary>Minor version information - see http://semver.org/. </summary>
    public byte Minor;

    /// <summary>Patch version information - see http://semver.org/. </summary>
    public byte Patch;



    /// <summary>
    /// Returns version 1.0.0.
    /// </summary>
    /// <returns>Version 1.0.0.</returns>
    public static SemVer V100
    {
      get
      {
        return new SemVer(1, 0, 0);
      }
    }

    /// <summary>
    /// Returns invalid version 0.0.0.
    /// </summary>
    /// <returns>Version 0.0.0.</returns>
    public static SemVer Invalid
    {
      get
      {
        return new SemVer(0, 0, 0);
      }
    }


    /// <summary>
    /// Creates new instance from 3 version bytes.
    /// </summary>
    /// <param name="Major">Major version information.</param>
    /// <param name="Minor">Minor version information.</param>
    /// <param name="Patch">Patch version information.</param>
    public SemVer(byte Major, byte Minor, byte Patch)
    {
      this.Major = Major;
      this.Minor = Minor;
      this.Patch = Patch;
    }

    /// <summary>
    /// Creates new instance from byte array.
    /// </summary>
    /// <param name="VersionInfo">Byte array to create version information from.</param>
    /// <returns>Valid version information or 0.0.0 if the byte array does not represent a valid version.</returns>
    public SemVer(byte[] VersionInfo)
    {
      bool valid = (VersionInfo != null) && (VersionInfo.Length == 3);
      Major = valid ? VersionInfo[0] : (byte)0;
      Minor = valid ? VersionInfo[1] : (byte)0;
      Patch = valid ? VersionInfo[2] : (byte)0;
    }

    /// <summary>
    /// Creates new instance from Google Protobuf ByteString.
    /// </summary>
    /// <param name="VersionInfo">ByteString to create version information from.</param>
    /// <returns>Valid version information or 0.0.0 if the ByteString does not represent a valid version.</returns>
    public SemVer(Google.Protobuf.ByteString VersionInfo):
      this(VersionInfo.ToByteArray())
    {
    }

    /// <summary>
    /// Converts Version to byte[].
    /// </summary>
    /// <returns>Byte array representing the version.</returns>
    public byte[] ToByteArray()
    {
      return new byte[] { Major, Minor, Patch };
    }

    /// <summary>
    /// Converts Version to Google Protobuf ByteString.
    /// </summary>
    /// <returns>ByteString representing the version.</returns>
    public Google.Protobuf.ByteString ToByteString()
    {
      return ProtocolHelper.ByteArrayToByteString(ToByteArray());
    }


    /// <summary>
    /// Checks whether the version is valid. By definition version 0.0.0 is not valid.
    /// </summary>
    /// <returns>true if the version is valid, false otherwise.</returns>
    public bool IsValid()
    {
      return (Major != 0) || (Minor != 0) || (Patch != 0);
    }


    public override string ToString()
    {
      return string.Format("{0}.{1}.{2}", Major, Minor, Patch);
    }

    public override int GetHashCode()
    {
      return (Major << 16) | (Minor << 8) | Patch;
    }

    public override bool Equals(object obj)
    {
      if (!(obj is SemVer))
        return false;

      SemVer ver = (SemVer)obj;
      return (Major == ver.Major) && (Minor == ver.Minor) && (Patch == ver.Patch);
    }
  }
}
