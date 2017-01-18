using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Equality compararer for byte arrays and similar types.
  /// </summary>
  public class StructuralEqualityComparer<T> : IEqualityComparer<T>
  {
    /// <summary>
    /// Checks whether two objects are structurally equal.
    /// </summary>
    /// <param name="x">First object to compare.</param>
    /// <param name="y">Second object to compare.</param>
    /// <returns>true if the objects are equal, false otherwise.</returns>
    public bool Equals(T x, T y)
    {
      return StructuralComparisons.StructuralEqualityComparer.Equals(x, y);
    }

    /// <summary>
    /// Obtains hash code for an object.
    /// </summary>
    /// <param name="obj">Object to get hash code for.</param>
    /// <returns>Integer hash code.</returns>
    public int GetHashCode(T obj)
    {
      return StructuralComparisons.StructuralEqualityComparer.GetHashCode(obj);
    }

    /// <summary>Defines a default generic structural comparer.</summary>
    private static StructuralEqualityComparer<T> defaultComparer;
    public static StructuralEqualityComparer<T> Default
    {
      get
      {
        StructuralEqualityComparer<T> comparer = defaultComparer;
        if (comparer == null)
        {
          comparer = new StructuralEqualityComparer<T>();
          defaultComparer = comparer;
        }
        return comparer;
      }
    }
  }
}
