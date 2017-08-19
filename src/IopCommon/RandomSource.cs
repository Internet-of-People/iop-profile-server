using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IopCommon
{
  /// <summary>
  /// Static cryptographically unsafe random generator instance.
  /// </summary>
  public static class RandomSource
  {
    public static Random Generator = new Random();
  }
}
