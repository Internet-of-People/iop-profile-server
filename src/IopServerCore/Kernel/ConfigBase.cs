using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using IopCommon;
using System.Security.Cryptography.X509Certificates;
using IopCrypto;

namespace IopServerCore.Kernel
{
  /// <summary>
  /// Types of values allowed in the configuration file.
  /// </summary>
  public enum ConfigValueType
  {
    /// <summary>Boolean switch "on" or "off".</summary>
    OnOffSwitch,

    /// <summary>String value that can not be empty.</summary>
    StringNonEmpty,

    /// <summary>String value that can be empty.</summary>
    StringEmpty,

    /// <summary>Integer value.</summary>
    Int,

    /// <summary>TCP/UDP port number - i.e. integer in range 0-65535.</summary>
    Port,

    /// <summary>IPv4 or IPv6 address or string "any".</summary>
    IpAddress
  }


  /// <summary>
  /// Description of each server role interface.
  /// </summary>
  public class RoleServerConfiguration
  {
    /// <summary>Roles of this server as a combination of server-specific flags.</summary>
    public uint Roles;

    /// <summary>Are the services on this port encrypted?</summary>
    public bool Encrypted;

    /// <summary>true if the server is operating on TCP protocol, false if on UDP.</summary>
    public bool IsTcpServer;

    /// <summary>Port on which this server provides its services.</summary>
    public int Port;

    /// <summary>Number of milliseconds after which the server's client is considered inactive and its connection can be terminated.</summary>
    public int ClientKeepAliveTimeoutMs;
  }


  /// <summary>
  /// Base class for global configuration component that makes it easy to load configuration from file.
  /// </summary>
  public abstract class ConfigBase : Component
  {
    /// <summary>Name of the component, this has to match the real component name derived from the ConfigBase class.</summary>
    public const string ComponentName = "Kernel.Config";

    /// <summary>Instance logger.</summary>
    protected Logger log = new Logger("IopServerCore." + ComponentName);

    /// <summary>
    /// <para>Loads global configuration from a string array that corresponds to lines of configuration file.</para>
    /// <seealso cref="LoadConfigurationFromFile"/>
    /// </summary>
    /// <param name="Lines">Server configuration as a string array.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public abstract bool LoadConfigurationFromStringArray(string[] Lines);

    /// <summary>Configuration settings mapped by their name.</summary>
    /// <remarks>The derived class is responsible for filling this with actual configuration values.</remarks>
    public Dictionary<string, object> Settings = new Dictionary<string, object>(StringComparer.Ordinal);



    /// <summary>
    /// Initializes the component.
    /// </summary>
    public ConfigBase() :
      base(ComponentName)
    {
    }


    /// <summary>
    /// Loads global configuration from a file.
    /// </summary>
    /// <param name="FileName">Name of the configuration file. This can be both relative or full path to the file.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    /// <remarks>See the configuration file comments for the file format and accepted values.</remarks>
    public bool LoadConfigurationFromFile(string FileName)
    {
      log.Trace("()");

      bool res = false;
      try
      {
        if (File.Exists(FileName))
        {
          string[] lines = File.ReadAllLines(FileName);
          if (LoadConfigurationFromStringArray(lines))
          {
            res = true;
          }
        }
        else log.Error("Unable to find configuration file '{0}'.", FileName);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Converts an array of configuration lines to a dictionary name-value structure,
    /// while checking if all names are defined, values are valid, and no required name is missing.
    /// </summary>
    /// <param name="Lines">Array of configuration lines.</param>
    /// <param name="NamesDefinition">Definition of configuration names.</param>
    /// <param name="NameVal">Empty dictionary to be filled with name-value pairs if the function succeeds.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public bool LinesToNameValueDictionary(string[] Lines, Dictionary<string, ConfigValueType> NamesDefinition, Dictionary<string, object> NameVal)
    {
      bool error = false;
      int lineNumber = 0;
      foreach (string aline in Lines)
      {
        lineNumber++;
        string line = aline.Trim();
        if ((line.Length == 0) || (line[0] == '#')) continue;

        int epos = line.IndexOf('=');
        if (epos == -1)
        {
          log.Error("Line {0} does not contain equal sign.", lineNumber);
          error = true;
          break;
        }

        string name = line.Substring(0, epos).Trim();
        string value = line.Substring(epos + 1).Trim();

        if (string.IsNullOrEmpty(name))
        {
          log.Error("No name before equal sign on line {0}.", lineNumber);
          error = true;
          break;
        }

        if (NameVal.ContainsKey(name))
        {
          log.Error("Name '{0}' redefined on line {1}.", name, lineNumber);
          error = true;
          break;
        }

        if (!NamesDefinition.ContainsKey(name))
        {
          log.Error("Unknown name '{0}' on line {1}.", name, lineNumber);
          error = true;
          break;
        }

        error = true;
        ConfigValueType type = NamesDefinition[name];
        switch (type)
        {
          case ConfigValueType.Int:
            {
              int val;
              if (int.TryParse(value, out val))
              {
                NameVal.Add(name, val);
                error = false;
              }
              else log.Error("Invalid integer value '{0}' on line {1}.", value, lineNumber);
              break;
            }

          case ConfigValueType.Port:
            {
              int val;
              if (int.TryParse(value, out val))
              {
                if ((val >= 0) || (val <= 65535))
                {
                  NameVal.Add(name, val);
                  error = false;
                }
                else log.Error("Invalid port value '{0}' on line {1}.", value, lineNumber);
              }
              else log.Error("Invalid port value '{0}' on line {1}.", value, lineNumber);

              break;
            }

          case ConfigValueType.IpAddress:
            {
              IPAddress val = IPAddress.Any;
              if (IPAddress.TryParse(value, out val))
              {
                NameVal.Add(name, val);
                error = false;
              }
              else log.Error("Invalid IP address interface value '{0}' on line {1}.", value, lineNumber);

              break;
            }

          case ConfigValueType.StringEmpty:
          case ConfigValueType.StringNonEmpty:
            {
              if (!string.IsNullOrEmpty(value) || (type == ConfigValueType.StringEmpty))
              {
                NameVal.Add(name, value);
                error = false;
              }
              else log.Error("Value for name '{0}' on line {1} can not be empty.", name, lineNumber);

              break;
            }

          case ConfigValueType.OnOffSwitch:
            {
              if ((value == "on") || (value == "off"))
              {
                NameVal.Add(name, value == "on");
                error = false;
              }
              else log.Error("Value for name '{0}' on line {1} can only be either 'on' or 'off'.", name, lineNumber);

              break;
            }

          default:
            log.Error("Internal error parsing line line {0}, type '{1}'.", lineNumber, type);
            break;
        }

        if (error) break;
      }

      if (!error)
      {
        // Check that all values are in NameVal dictionary.
        foreach (string key in NamesDefinition.Keys)
        {
          if (!NameVal.ContainsKey(key))
          {
            log.Error("Missing definition of '{0}'.", key);
            error = true;
            break;
          }
        }
      }

      bool res = !error;
      return res;
    }
  }
}
