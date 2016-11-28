using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Utils
{
  /// <summary>
  /// Asynchronous file operations.
  /// </summary>
  public static class FileAsync
  {
    /// <summary>
    /// Opens a binary file, reads the contents of the file into a byte array, and then closes the file.
    /// </summary>
    /// <param name="FileName">Name of the file to read.</param>
    /// <returns>Byte array containing the contents of the file.</returns>
    public static async Task<byte[]> ReadAllBytesAsync(string FileName)
    {
      byte[] res = null;
      using (FileStream file = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true))
      {
        res = new byte[file.Length];
        await file.ReadAsync(res, 0, (int)file.Length);
      }
      return res;
    }

    /// <summary>
    /// Creates a new file, writes the specified byte array to the file, and then closes the file. If the target file already exists, it is overwritten.
    /// </summary>
    /// <param name="FileName">Name of the file to write.</param>
    /// <param name="Data">Bytes to write to the file.</param>
    public static async Task WriteAllBytesAsync(string FileName, byte[] Data)
    {
      using (FileStream file = new FileStream(FileName, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
      {
        await file.WriteAsync(Data, 0, Data.Length);
      }
    }
  }
}
