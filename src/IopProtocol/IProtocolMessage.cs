using Google.Protobuf;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IopProtocol
{
  /// <summary>
  /// Interface that every IoP protocol message must implement.
  /// </summary>
  public interface IProtocolMessage<out T>
  {
    /// <summary>Protocol specific message.</summary>
    T Message { get; }

    /// <summary>Unique message identifier within a session.</summary>
    uint Id { get; }
  }
}
