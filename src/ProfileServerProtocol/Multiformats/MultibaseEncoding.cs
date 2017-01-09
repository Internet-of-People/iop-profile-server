using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServerProtocol.Multiformats
{
  public abstract class MultibaseEncoding
  {
    /// <summary>List of identifying characters of the encoding.</summary>
    public abstract char[] Identifiers { get; }

    /// <summary>
    /// The default identifier of the encoding.
    /// </summary>
    public virtual char DefaultIdentifier()
    {
      return Identifiers[0];
    }

    /// <summary>
    /// Encode byte array to a multibase string.
    /// </summary>
    /// <param name="Data">Data to encode.</param>
    /// <returns>Encoded string representation of the input data.</returns>
    public abstract string Encode(byte[] Data);

    /// <summary>
    /// Decode an encoded multibase string.
    /// </summary>
    /// <param name="EncodedString">Input data encoded as a string.</param>
    /// <returns>Byte array representation of the encoded data.</returns>
    public abstract byte[] Decode(string EncodedString);
  }
}
