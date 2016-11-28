using HomeNetCrypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace UnitTests
{
  public class CryptoTests
  {
    /// <summary>
    /// Tests key generation and signing using Ed25519.
    /// </summary>
    [Fact]
    public void Ed25519GenerateSignVerifyTest()
    {
      byte[] privateKey = Encoding.UTF8.GetBytes("12345678901234567890123456789012");
      KeysEd25519 keys = Ed25519.GenerateKeys(privateKey);
      string message = "This is message";

      byte[] signature = Ed25519.Sign(message, keys.ExpandedPrivateKey);

      string signatureString = Crypto.ToHex(signature);

      Assert.Equal("85F2D841785E01E1D7C87E6354E8FBF525227A1C3C10C5F58FEE1BDA6C126EE941FD84AE76188AD0FB2B5FBBDE839F9097E7D8AE79463F4B0A534E80C916C70D", signatureString);
      Assert.Equal(true, Ed25519.Verify(signature, message, keys.PublicKey));
      Assert.Equal(false, Ed25519.Verify(signature, message + "x", keys.PublicKey));
    }

    /// <summary>
    /// Provides test vectors for Ed25519 key generation.
    /// </summary>
    [Fact]
    public void Ed25519KeyPairs()
    {
      byte[] privateKey = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f,
                                       0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f };
      KeysEd25519 keys = Ed25519.GenerateKeys(privateKey);

      Assert.Equal("000102030405060708090A0B0C0D0E0F000102030405060708090A0B0C0D0E0F9B62773323EF41A11834824194E55164D325EB9CDCC10DDDA7D10ADE4FBD8F6D", keys.ExpandedPrivateKeyHex);
      Assert.Equal("9B62773323EF41A11834824194E55164D325EB9CDCC10DDDA7D10ADE4FBD8F6D", keys.PublicKeyHex);
    }
  }
}
