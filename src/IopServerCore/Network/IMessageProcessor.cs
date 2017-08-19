using IopProtocol;
using System.Threading.Tasks;

namespace IopServerCore.Network
{
  /// <summary>
  /// Interface for server specific message processor.
  /// </summary>
  public interface IMessageProcessor<TMessage>
  {
    /// <summary>
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="Client">TCP client who send the message.</param>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    Task<bool> ProcessMessageAsync(ClientBase<TMessage> Client, IProtocolMessage<TMessage> IncomingMessage);

    /// <summary>
    /// Sends protocol violation error to client.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    Task SendProtocolViolation(ClientBase<TMessage> Client);
  }
}
