using ProfileServer.Utils;
using IopCrypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using IopCommon;
using IopProtocol;

namespace UnitTests
{
  /// <summary>
  /// Tests of helper classes and methods.
  /// </summary>
  public class UtilsTests
  {
    /// <summary>
    /// Tests ValidateProfileSearchRegex method with regular expressions that should pass the validation.
    /// </summary>
    [Fact]
    public void RegexValidator_ValidateProfileSearchRegex_ValidTest()
    {
      List<string> inputs = new List<string>()
      {
        @"",
        @".*",
        @"(^|;)key=([^=]+;)?value($|,|;)",
        @"(^|;)key=([^=]+;)?(value1|value2|value3)($|,|;)",
        @"^key=\x01\x02\xAB\xab\u1234\u12aB[0-9a-zA-Z]+[123]*(a|b){4,}$",
        @"^key=value\.*\*+\-?\+{1,2}\[\]{6}\\{3,10000}\?\^\|\(\)\{\}$",
        @"(^|;)key=([^=]+;)?(中文|Français|Deutsch|日本語|Русский язык|Español|عربي|Esperanto|한국어|BIG5)($|,|;)",
        @"key=\w\W+\s*\S+\d{4}\D{4,5}",
        @"(^|;)key=abc{34}\?($|,|;)",
      };

      foreach (string input in inputs)
        Assert.True(RegexTypeValidator.ValidateProfileSearchRegex(input));
    }

    /// <summary>
    /// Tests ValidateProfileSearchRegex method with regular expressions that should fail the validation.
    /// </summary>
    [Fact]
    public void RegexValidator_ValidateProfileSearchRegex_InvalidTest()
    {
      List<string> inputs = new List<string>()
      {
        @"\a",
        @"\b",
        @"\B",
        @"\",
        @"\t",
        @"\r",
        @"\n",
        @"\x",
        @"\f",
        @"\e",
        @"\A",
        @"\z",
        @"\Z",
        @"\G",
        @"\1",
        @"\2",
        @"\12",
        @"\123",
        @"\1234",
        @"\x1",
        @"\x1x",
        @"\xx1",
        @"\xxa",
        @"aa\xa",
        @"aewrgaer\u",
        @"\u1",
        @"asdfdsaf\u12",
        @"\u123",
        @"\ux115",
        @"\u1x15",
        @"\u11x5",
        @"\u111x",
        @"\X12",
        @"\U1234",
        @"\c",
        @"\c1",
        @"\cC",
        @"(^|;)key=([^=]+;)?va(?'alpha')lue($|,|;)",
        @"(^|;)key=([^=]+;)?(?<double>A)B<double>($|,|;)",
        @"(^|;)key=([^=]+;)?\k<double>A($|,|;)",
        @"(^|;)key=([^=]+;)?Write(?:Line)?($|,|;)",
        @"(^|;)key=([^=]+;)?A\d{2}(?i:\w+)c?($|,|;)",
        @"(^|;)key=([^=]+;)?\w+(?=\.)($|,|;)",
        @"\\\\\\\\\\\\\\\\\\\\\",
        @"(^|;)key=\d*?\.\d($|,|;)",
        @"(^|;)key=be+?($|,|;)",
        @"(^|;)key=rai??n($|,|;)",
        @"(^|;)key=\d{3}?($|,|;)",
        @"(^|;)key=\d{2,}?($|,|;)",
        @"(^|;)key=\d{3,5}?($|,|;)",
        @"(^|;)key=${name}($|,|;)",
        @"(^|;)key=(?# comment)($|,|;)",
        @"tooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooongtooooooooooooolooooooooooooooooooooooooong",
      };

      foreach (string input in inputs)
      {
        bool validLength = (Encoding.UTF8.GetByteCount(input) <= PsMessageBuilder.MaxProfileSearchExtraDataLengthBytes);
        if (validLength)
          Assert.False(RegexTypeValidator.ValidateProfileSearchRegex(input));
      }
    }


    /// <summary>
    /// Tests StructuralEqualityComparer for byte arrays.
    /// </summary>
    [Fact]
    public void StructuralEqualityComparer()
    {
      List<byte[]> inputs = new List<byte[]>()
      {
        null,
        null,
        new byte[] { },
        new byte[] { },
        new byte[] { 1 },
        new byte[] { 1, 2, 3 },
        new byte[] { 4, 5, 6 },
        new byte[] { 1, 2, 3 },
      };

      List<bool> expectedResults = new List<bool>()
      {
        // null #1 vs others
        true, false, false, false, false, false, false,

        // null #2 vs others
        false, false, false, false, false, false,

        // empty #1 array vs others
        true, false, false, false, false,

        // empty #2 array vs others
        false, false, false, false,

        // { 1 } vs others
        false, false, false,

        // { 1, 2, 3 } vs others
        false, true,

        // { 4, 5, 6 } vs others
        false,
      };

      int index = 0;
      for (int i = 0; i < inputs.Count - 1; i++)
      {
        for (int j = i + 1; j < inputs.Count; j++)
        {
          Assert.Equal(expectedResults[index], StructuralEqualityComparer<byte[]>.Default.Equals(inputs[i], inputs[j]));

          index++;
        }
      }
    }

  }
}
