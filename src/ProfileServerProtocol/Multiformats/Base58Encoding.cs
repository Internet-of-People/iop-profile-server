using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ProfileServerProtocol.Multiformats
{
  /// <summary>
  /// Types of alphabets used in base58 encoding.
  /// </summary>
  public enum Base58Alphabet
  {
    Bitcoin,
    Flickr
  }

  /// <summary>
  /// Base58 encoding implementation based on https://github.com/tabrath/cs-multibase.
  /// </summary>
  public class Base58Encoding : MultibaseEncoding
  {
    /// <summary>Bitcoin's Base58 alphabet.</summary>
    public const string BitcoinAlphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

    /// <summary>Flickr's Base58 alphabet.</summary>
    public const string FlickrAlphabet = "123456789abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>List of identifying characters of the encoding.</summary>
    private static char[] identifiers = new char[] { 'z', 'Z' };
    public override char[] Identifiers { get { return identifiers; } }

    /// <summary>Base58 decoding map to speedup decoding.</summary>
    private static Dictionary<string, byte[]> decodeMap;

    /// <summary>Default static encoder.</summary>
    public static Base58Encoding Encoder = new Base58Encoding();

    public override string Encode(byte[] Data)
    {
      return Encode(Data, Base58Alphabet.Bitcoin);
    }

    /// <summary>
    /// Encode byte array to a string.
    /// </summary>
    /// <param name="Data">Data to encode.</param>
    /// <param name="Alphabet">Encoding alphabet to use.</param>
    /// <returns>Encoded string representation of the input data.</returns>
    private string Encode(byte[] Data, Base58Alphabet Alphabet = Base58Alphabet.Bitcoin)
    {
      string res = null;
      switch (Alphabet)
      {
        case Base58Alphabet.Bitcoin:
          res = "z" + EncodeRaw(Data, Alphabet);
          break;

        case Base58Alphabet.Flickr:
          res = "Z" + EncodeRaw(Data, Alphabet);
          break;
      }

      return res;
    }


    /// <summary>
    /// Encode raw byte array (without prefix) to a string.
    /// </summary>
    /// <param name="Data">Data to encode.</param>
    /// <param name="Alphabet">Encoding alphabet to use.</param>
    /// <returns>Encoded string representation of the input data.</returns>
    public string EncodeRaw(byte[] Data, Base58Alphabet Alphabet = Base58Alphabet.Bitcoin)
    {
      string res = null;
      switch (Alphabet)
      {
        case Base58Alphabet.Bitcoin:
          res = EncodeRaw(Data, BitcoinAlphabet);
          break;

        case Base58Alphabet.Flickr:
          res = EncodeRaw(Data, FlickrAlphabet);
          break;
      }

      return res;
    }


    /// <summary>
    /// Decodes a raw encoded string (without prefix) to byte array.
    /// </summary>
    /// <param name="EncodedString">Input data encoded as a string.</param>
    /// <param name="Alphabet">Decoding alphabet to use.</param>
    /// <returns>Byte array representation of the encoded data.</returns>
    public byte[] DecodeRaw(string EncodedString, Base58Alphabet Alphabet = Base58Alphabet.Bitcoin)
    {
      byte[] res = null;

      try
      {
        switch (Alphabet)
        {
          case Base58Alphabet.Bitcoin:
            res = DecodeRaw(EncodedString, BitcoinAlphabet);
            break;

          case Base58Alphabet.Flickr:
            res = DecodeRaw(EncodedString, FlickrAlphabet);
            break;
        }
      }
      catch
      {
      }

      return res;
    }



    public override byte[] Decode(string EncodedString)
    {
      byte[] res = null;
      try
      {
        char id = EncodedString[0];
        string body = EncodedString.Substring(1);

        switch (id)
        {
          case 'z':
            res = DecodeRaw(body, BitcoinAlphabet);
            break;

          case 'Z':
            res = DecodeRaw(body, FlickrAlphabet);
            break;
        }
      }
      catch
      {
      }

      return res;
    }


    /// <summary>
    /// Encode raw byte array (without prefix) to a string.
    /// </summary>
    /// <param name="Data">Data to encode.</param>
    /// <param name="Alphabet">Encoding alphabet to use.</param>
    /// <returns>Encoded string representation of the input data.</returns>
    private string EncodeRaw(byte[] Data, string Alphabet)
    {
      char[] charArr = Data.TakeWhile(c => c == 0)
        .Select(ch => Alphabet[0])
        .Concat(ParseBigInt(Data.Aggregate<byte, BigInteger>(0, (current, t) => current * 256 + t), Alphabet))
        .Reverse()
        .ToArray();
      return new string(charArr);
    }

    /// <summary>
    /// Parses next character from the input data.
    /// </summary>
    /// <param name="IntData"></param>
    /// <param name="Alphabet">Encoding alphabet to use.</param>
    /// <returns>Current char from the input.</returns>
    private static IEnumerable<char> ParseBigInt(BigInteger IntData, string Alphabet)
    {
      int len = Alphabet.Length;
      while (IntData > 0)
      {
        int rem = (int)(IntData % len);
        IntData /= len;
        yield return Alphabet[rem];
      }
    }


    /// <summary>
    /// Creates decoding map.
    /// </summary>
    /// <param name="Alphabet">Encoding alphabet to use.</param>
    /// <returns>Decoding map.</returns>
    private static byte[] CreateDecodeMap(string Alphabet)
    {
      byte[] map = Enumerable.Range(0, 256).Select(b => (byte)0xFF).ToArray();

      for (int i = 0; i < Alphabet.Length; i++)
        map[Alphabet[i]] = (byte)i;

      return map;
    }

    /// <summary>
    /// Obtains a decoding map. If it does not exists, it is created.
    /// </summary>
    /// <param name="Alphabet">Encoding alphabet to use.</param>
    /// <returns>Decoding map.</returns>
    private static byte[] GetDecodeMap(string Alphabet)
    {
      if (decodeMap == null)
        decodeMap = new Dictionary<string, byte[]>(StringComparer.Ordinal);

      byte[] map;
      if (decodeMap.TryGetValue(Alphabet, out map))
        return map;

      map = CreateDecodeMap(Alphabet);
      decodeMap.Add(Alphabet, map);
      return map;
    }

    /// <summary>
    /// Decodes a raw encoded string (without prefix) to byte array.
    /// </summary>
    /// <param name="EncodedString">Input data encoded as a string.</param>
    /// <param name="Alphabet">Encoding alphabet to use.</param>
    /// <returns>Byte array representation of the encoded data.</returns>
    private static byte[] DecodeRaw(string EncodedString, string Alphabet)
    {
      byte[] decodeMap = GetDecodeMap(Alphabet);
      int len = Alphabet.Length;

      byte[] res = EncodedString.TakeWhile(c => c == Alphabet[0])
        .Select(_ => (byte)0)
        .Concat(EncodedString.Select(c => decodeMap[c])
        .Aggregate<byte, BigInteger>(0, (current, c) => current * len + c)
        .ToByteArray()
        .Reverse()
        .SkipWhile(c => c == 0))
        .ToArray();

      return res;
    }
  }
}

