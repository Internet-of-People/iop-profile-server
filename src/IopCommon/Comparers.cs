using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IopCommon
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


  /// <summary>
  /// Binary comparer for byte arrays.
  /// </summary>
  public static class ByteArrayComparer
  {
    /// <summary>
    /// Compares two byte arrays.
    /// </summary>
    /// <param name="X">First array to compare.</param>
    /// <param name="Y">Second array to compare.</param>
    /// <returns>-1 if X < Y
    /// <para>0 if X == Y</para>
    /// <para>1 if X > Y</para>.
    /// </returns>
    public static int Compare(byte[] X, byte[] Y)
    {
      if ((X == null) && (Y == null)) return 0;
      if (X == null) return -1;
      if (Y == null) return 1;

      if (X.Length != Y.Length) return X.Length.CompareTo(Y.Length);

      int res = 0;
      for (int i = 0; i < X.Length; i++)
      {
        int c = X[i].CompareTo(Y[i]);
        if (c != 0)
        {
          res = c;
          break;
        }
      }

      return res;
    }


    /// <summary>
    /// Checks whether contents of two byte arrays are binary equal.
    /// </summary>
    /// <param name="X">First array to compare.</param>
    /// <param name="Y">Second array to compare.</param>
    /// <returns>true if the arrays are equal, false otherwise.</returns>
    public static bool Equals(byte[] X, byte[] Y)
    {
      return Compare(X, Y) == 0;
    }
  }
}
