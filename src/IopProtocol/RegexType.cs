using IopCommon;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IopProtocol
{
  /// <summary>
  /// Validates RegexType expression.
  /// </summary>
  public static class RegexTypeValidator
  {
    private static Logger log = new Logger("IopProtocol.RegexTypeValidator");


    /// <summary>List of characters that are allowed to be escaped in regex or has special allowed meaning with backslash.</summary>
    private static HashSet<char> allowedBackslashedCharacters = new HashSet<char>()
    {
      // Escaped chars
      'x', 'u', '.', '*', '.', '*', '-', '+', '[', ']', '\\', '?', '^', '|', '(', ')', '{', '}',

      // Special meaning chars
      'w', 'W', 's', 'S', 'd', 'D'
    };

    /// <summary>Regular expression to find forbidden substrings in regular expression.</summary>
    private static Regex forbiddenSequenceRegex = new Regex(
       "(" + @"\{[^0-9]*\}" + ")" +     // Do not allow "{name}"
      "|(" + @"[\(\*\+\?\}]\?" + ")",   // Do not allow "(?", "*?", "+?", "??", "}?"
      RegexOptions.Singleline);



    /// <summary>
    /// Checks whether regular expression string has a valid RegexType format according to the protocol specification.
    /// </summary>
    /// <param name="RegexString">Regular expression string to check, must not be null.</param>
    /// <returns>true if the regular expression string is valid, false otherwise.</returns>
    /// <remarks>As the regular expression string is used as an input into System.Text.RegularExpressions.Regex.IsMatch 'input' parameter, 
    /// we are at risk that a malicious attacker submits an expression that supports wider spectrum of rules 
    /// than those required by the protocol, or that the constructed expression will take long time to execute and thus 
    /// it will perform a DoS attack. We eliminate the first problem by checking the actual format of the input regular expression 
    /// against a list of forbidden substrings and we eliminate the DoS problem by setting up a timeout on the total processing time over all search results.
    ///
    /// <para>See https://docs.microsoft.com/en-us/dotnet/articles/standard/base-types/quick-ref for .NET regular expression reference.</para>
    /// </remarks>
    public static bool ValidateRegex(string RegexString)
    {
      log.Trace("(RegexString:'{0}')", RegexString.SubstrMax());

      bool validContent = true;
      StringBuilder sb = new StringBuilder();

      // First, we remove all escaped characters out of the string.
      for (int i = 0; i < RegexString.Length; i++)
      {
        char c = RegexString[i];
        if (c == '\\')
        {
          if (i + 1 >= RegexString.Length)
          {
            log.Trace("Invalid backslash at the end of the regular expression.");
            validContent = false;
            break;
          }

          char cn = RegexString[i + 1];
          if (allowedBackslashedCharacters.Contains(cn))
          {
            switch (cn)
            {
              case 'x':
                // Potential \xnn sequence.
                i += 2;
                if (i + 2 >= RegexString.Length)
                {
                  log.Trace("Invalid '\\x' sequence found at the end of the regular expression.");
                  validContent = false;
                  break;
                }

                if (RegexString[i].IsHexChar() && RegexString[i + 1].IsHexChar())
                {
                  // Replace the sequence with verification neutral 'A' character.
                  sb.Append('A');
                  // Skip one digit, the second will be skipped by for loop.
                  i++;
                }
                else
                {
                  log.Trace("Invalid '\\x' sequence found in the regular expression.");
                  validContent = false;
                }
                break;

              case 'u':
                // Potential \unnnn sequence.
                i += 2;
                if (i + 4 >= RegexString.Length)
                {
                  log.Trace("Invalid '\\u' sequence found at the end of the regular expression.");
                  validContent = false;
                  break;
                }

                if (RegexString[i].IsHexChar() && RegexString[i + 1].IsHexChar() && RegexString[i + 2].IsHexChar() && RegexString[i + 3].IsHexChar())
                {
                  // Replace the sequence with verification neutral 'A' character.
                  sb.Append('A');
                  // Skip three digits, the fourth will be skipped by for loop.
                  i++;
                }
                else
                {
                  log.Trace("Invalid '\\u' sequence found in the regular expression.");
                  validContent = false;
                }
                break;

              default:
                // Replace the sequence with verification neutral 'A' character.
                sb.Append('A');
                // Character is allowed, skip it.
                i++;
                break;
            }
          }
          else
          {
            // Character is not allowed, this is an error.
            log.Trace("Invalid sequence '\\{0}' found in the regular expression.", cn);
            validContent = false;
            break;
          }
        }
        else
        {
          // Other chars just copy to the newly built string.
          sb.Append(c);
        }
      }


      string regexStr = sb.ToString();
      if (validContent)
      {
        // Now we have the string without any escaped characters, so we can run regular expression to find unallowed substrings.
        Match match = forbiddenSequenceRegex.Match(regexStr);
        if (match.Success)
        {
          log.Trace("Forbidden sequence '{0}' found in the regular expression.", match.Groups[1].Length > 0 ? match.Groups[1] : match.Groups[2]);
          validContent = false;
        }
      }

      bool res = validContent;

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
