using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Database representation of node settings in key-value format.
  /// </summary>
  public class Setting
  {
    /// <summary>Setting/key name.</summary>
    [Key]
    public string Name { get; set; }

    /// <summary>Setting value in a string format.</summary>
    [Required(AllowEmptyStrings = true)]
    public string Value { get; set; }

    /// <summary>
    /// Parameterless constructor creates invalid entry.
    /// </summary>
    public Setting() : 
      this("INVALID")
    {
    }

    /// <summary>
    /// Constructor to create empty value.
    /// </summary>
    /// <param name="Name">Setting/key name.</param>
    public Setting(string Name) :
      this(Name, "")
    {
    }

    /// <summary>
    /// Constructor for string values.
    /// </summary>
    /// <param name="Name">Setting/key name.</param>
    /// <param name="Value">String value.</param>
    public Setting(string Name, string Value)
    {
      this.Name = Name;
      this.Value = Value;
    }

    /// <summary>
    /// Constructor for integer values.
    /// </summary>
    /// <param name="Name">Setting/key name.</param>
    /// <param name="Value">Integer value.</param>
    public Setting(string Name, int Value) : 
      this(Name, Value.ToString())
    {
    }
  }
}
