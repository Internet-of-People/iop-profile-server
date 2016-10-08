using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chaos.NaCl;
using System.Security.Cryptography;
using System.Text;

namespace HomeNetCrypto
{
  /// <summary>
  /// Structure to hold node's public/private key pairs and extended private key in binary and hex formats.
  /// </summary>
  public class KeysEd25519
  {
    /// <summary>64 byte public key.</summary>
    public byte[] PublicKey;

    /// <summary>32 byte private key, also known as private key seed.</summary>
    public byte[] PrivateKey;

    /// <summary>64 byte extended private key.</summary>
    public byte[] ExpandedPrivateKey;

    /// <summary>Public key in uppercase hex format.</summary>
    public string PublicKeyHex;

    /// <summary>Private key in uppercase hex format.</summary>
    public string PrivateKeyHex;

    /// <summary>Expanded private key in uppercase hex format.</summary>
    public string ExpandedPrivateKeyHex;
  }

  /// <summary>
  /// Provides basic EdDSA cryptographic operations based on Chaos.NaCl library.
  /// </summary>
  public class Ed25519
  {
    /// <summary>
    /// Generates new keys using random seed.
    /// </summary>
    /// <returns>Structure holding newly generated keys.</returns>
    public static KeysEd25519 GenerateKeys()
    {
      byte[] seed = new byte[32];
      Crypto.Rng.GetBytes(seed);

      return GenerateKeys(seed);
    }

    /// <summary>
    /// Generates new keys using a given seed (private key).
    /// </summary>
    /// <param name="PrivateKey">32 byte private key seed to generate public key and extended private key from.</param>
    /// <returns>Structure holding newly generated keys.</returns>
    public static KeysEd25519 GenerateKeys(byte[] PrivateKey)
    {
      byte[] publicKey;
      byte[] expandedPrivateKey;
      Chaos.NaCl.Ed25519.KeyPairFromSeed(out publicKey, out expandedPrivateKey, PrivateKey);

      KeysEd25519 res = new KeysEd25519();
      res.PublicKey = publicKey;
      res.PublicKeyHex = CryptoBytes.ToHexStringUpper(res.PublicKey);
      res.PrivateKey = PrivateKey;
      res.PrivateKeyHex = CryptoBytes.ToHexStringUpper(res.PrivateKey);
      res.ExpandedPrivateKey = expandedPrivateKey;
      res.ExpandedPrivateKeyHex = CryptoBytes.ToHexStringUpper(res.ExpandedPrivateKey);

      return res;
    }

    /// <summary>
    /// Signs a UTF8 string message using an extended private key.
    /// </summary>
    /// <param name="Message">Message to be signed.</param>
    /// <param name="ExpandedPrivateKey">Extended private key.</param>
    /// <returns>64 byte signature of the message.</returns>
    public static byte[] Sign(string Message, byte[] ExpandedPrivateKey)
    {
      byte[] message = Encoding.UTF8.GetBytes(Message);
      return Sign(message, ExpandedPrivateKey);
    }

    /// <summary>
    /// Signs a binary message using an extended private key.
    /// </summary>
    /// <param name="Message">Message to be signed.</param>
    /// <param name="ExpandedPrivateKey">Extended private key.</param>
    /// <returns>64 byte signature of the message.</returns>
    public static byte[] Sign(byte[] Message, byte[] ExpandedPrivateKey)
    {
      return Chaos.NaCl.Ed25519.Sign(Message, ExpandedPrivateKey);
    }


    /// <summary>
    /// Verifies a signature for a specific UTF8 string message using a public key.
    /// </summary>
    /// <param name="Signature">64 byte signature of the message.</param>
    /// <param name="Message">Message that was signed.</param>
    /// <param name="PublicKey">Public key that corresponds to the private key used to sign the message.</param>
    /// <returns>true if the signature represents a valid cryptographic signature of the message using the private key for which the public key was provided.</returns>
    public static bool Verify(byte[] Signature, string Message, byte[] PublicKey)
    {
      byte[] message = Encoding.UTF8.GetBytes(Message);
      return Verify(Signature, message, PublicKey);
    }

    /// <summary>
    /// Verifies a signature for a specific binary message using a public key.
    /// </summary>
    /// <param name="Signature">64 byte signature of the message.</param>
    /// <param name="Message">Message that was signed.</param>
    /// <param name="PublicKey">Public key that corresponds to the private key used to sign the message.</param>
    /// <returns>true if the signature represents a valid cryptographic signature of the message using the private key for which the public key was provided.</returns>
    public static bool Verify(byte[] Signature, byte[] Message, byte[] PublicKey)
    {
      return Chaos.NaCl.Ed25519.Verify(Signature, Message, PublicKey);
    }
  }


  /// <summary>
  /// Helper cryptographic functions.
  /// </summary>
  public class Crypto
  {
    /// <summary>Cryptographically secure random number generator.</summary>
    public static RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    public static SHA1 Sha1Engine = SHA1.Create();

    /// <summary>
    /// Converts a binary data to an uppercase hexadecimal string representation.
    /// </summary>
    /// <param name="Data">Data to convert to hex string.</param>
    /// <returns>Uppercase hex string representing the data.</returns>
    public static string ToHex(byte[] Data)
    {
      return CryptoBytes.ToHexStringUpper(Data);
    }

    /// <summary>
    /// Converts an uppercase hexadecimal string representation of data to a byte array.
    /// </summary>
    /// <param name="Data">Uppercase hex string.</param>
    /// <returns>Data in binary format.</returns>
    public static byte[] FromHex(string Data)
    {
      return CryptoBytes.FromHexString(Data);     
    }


    /// <summary>
    /// Computes SHA1 hash of binary data.
    /// </summary>
    /// <param name="Data">Data to be hashed.</param>
    /// <returns>SHA1 hash in binary form.</returns>
    public static byte[] Sha1(byte[] Data)
    {
      byte[] res = null;
      lock (Sha1Engine)
      {
        res = Sha1Engine.ComputeHash(Data);
      }
      return res;
    }
  }
}