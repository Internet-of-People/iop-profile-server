using IopProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IopServerCore.Network
{
  /// <summary>
  /// Represents a request sent by the IoP server to some other party before the server received a response to it.
  /// </summary>
  public class UnfinishedRequest<TMessage>
  {
    /// <summary>Request message sent by the server.</summary>
    public IProtocolMessage<TMessage> RequestMessage;

    /// <summary>Message specific context that the server can use to store information required for processing of the future response.</summary>
    public object Context;

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    public UnfinishedRequest(IProtocolMessage<TMessage> RequestMessage, object Context)
    {
      this.RequestMessage = RequestMessage;
      this.Context = Context;
    }
  }
}
