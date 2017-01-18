using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServerProtocolTests
{
  /// <summary>
  /// Represents a request sent by the node to some other party before the node received a response to it.
  /// </summary>
  public class UnfinishedRequest
  {
    /// <summary>Request message sent by the node.</summary>
    public Message RequestMessage;

    /// <summary>Message specific context that the node can use to store information required for processing of the future response.</summary>
    public object Context;

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    public UnfinishedRequest(Message RequestMessage, object Context)
    {
      this.RequestMessage = RequestMessage;
      this.Context = Context;
    }
  }
}
