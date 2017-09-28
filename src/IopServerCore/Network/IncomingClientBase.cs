using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Net;
using System.Threading;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore.Storage;
using IopProtocol;
using IopCommon;
using System.Net.Security;
using IopServerCore.Kernel;
using System.Security.Cryptography.X509Certificates;

namespace IopServerCore.Network
{
  /// <summary>
  /// Incoming client class represents any kind of TCP client that connects to one of the server's TCP servers.
  /// </summary>
  public abstract class IncomingClientBase<TMessage> : ClientBase<TMessage>, IDisposable
  {
    /// <summary>Maximal number of unconfirmed messages that the server is willing to maintain before it refuses to send any more messages to a client without it responding back.</summary>
    public const int MaxUnfinishedRequests = 20;

    /// <summary>Role server assigned client identifier.</summary>
    public ulong Id { get; }

    /// <summary>UTC time before next message has to come over the connection from this client or the server can close the connection due to inactivity.</summary>
    public DateTime NextKeepAliveTime { get; private set; }

    /// <summary>
    /// This is either Server.ClientKeepAliveIntervalMs for end user client device connections, 
    /// or Server.ServerKeepAliveIntervalMs for server to server or unknown connections.
    /// </summary>
    private readonly int _keepAliveIntervalMs;

    private byte[] _publicKey;

    // Client Context Section
    /// <summary>Client's public key.</summary>
    public byte[] PublicKey
    {
      get { return _publicKey; }
      
      set
      {
        _publicKey = value;
        IdentityId = IopCrypto.Crypto.Sha256(value);
      }
    }

    /// <summary>Client's public key hash.</summary>
    public byte[] IdentityId { get; private set; }

    /// <summary>Random data used for client's authentication.</summary>
    public byte[] AuthenticationChallenge;

    /// <summary>true if the network client represents an identity that is currently online and authenticated, false otherwise.</summary>
    public bool IsAuthenticatedOnlineClient;

    /// <summary>List of unprocessed requests that we expect to receive responses to mapped by Message.id.</summary>
    private readonly Dictionary<uint, UnfinishedRequest<TMessage>> _unfinishedRequests = new Dictionary<uint, UnfinishedRequest<TMessage>>();

    /// <summary>Lock for access to unfinishedRequests list.</summary>
    private readonly object _unfinishedRequestsLock = new object();

    // \Client Context Section


    /// <summary>Shutdown signaling from the component that created the client.</summary>
    private readonly ComponentShutdown _shutdownSignaling;

    /// <summary>Module responsible for processing logic behind incoming messages.</summary>
    private readonly IMessageProcessor<TMessage> _messageProcessor;



    /// <summary>
    /// Creates the instance for a new TCP server client.
    /// </summary>
    /// <param name="TcpClient">TCP client class that holds the connection and allows communication with the client.</param>
    /// <param name="MessageProcessor">Message processor </param>
    /// <param name="Id">Unique identifier of the client's connection.</param>
    /// <param name="UseTls">true if the client is connected to the TLS port, false otherwise.</param>
    /// <param name="KeepAliveIntervalMs">Number of milliseconds for the connection to this client to be without any message until the server can close it for inactivity.</param>
    /// <param name="IdBase">Number to start message identifier series with.</param>
    /// <param name="ShutdownSignaling">Shutdown signaling from the component that created the client.</param>
    /// <param name="LogPrefix">Prefix for log entries created by the client.</param>
    public IncomingClientBase(TcpClient TcpClient, IMessageProcessor<TMessage> MessageProcessor, ulong Id, bool UseTls, int KeepAliveIntervalMs, uint IdBase, ComponentShutdown ShutdownSignaling, string LogPrefix) :
      base(TcpClient, UseTls, IdBase)
    {
      this.Id = Id;
      _shutdownSignaling = ShutdownSignaling;
      log = new Logger("IopServerCore.Network.IncomingClient", LogPrefix);

      log.Trace("(UseTls:{0},KeepAliveIntervalMs:{1})", UseTls, KeepAliveIntervalMs);

      _messageProcessor = MessageProcessor;

      this._keepAliveIntervalMs = KeepAliveIntervalMs;

      IsAuthenticatedOnlineClient = false;

      log.Trace("(-)");
    }

    public override void KeptAlive()
    {
      NextKeepAliveTime = DateTime.UtcNow.AddSeconds(_keepAliveIntervalMs);
    }


    /// <summary>
    /// Reads messages from the client stream and processes them in a loop until the client disconnects 
    /// or until an action (such as a protocol violation) that leads to disconnecting of the client occurs.
    /// </summary>
    public async Task ReceiveMessageLoop()
    {
      log.Trace("()");

      try
      {
        if (UseTls)
        {
          SslStream sslStream = (SslStream)Stream;
          ConfigBase config = (ConfigBase)Base.ComponentDictionary[ConfigBase.ComponentName];
          await sslStream.AuthenticateAsServerAsync((X509Certificate)config.Settings["TcpServerTlsCertificate"], false, SslProtocols.Tls12, false);
        }

        RawMessageReader messageReader = new RawMessageReader(Stream);
        while (!_shutdownSignaling.IsShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(_shutdownSignaling.ShutdownCancellationTokenSource.Token);
          if (rawMessage.ProtocolViolation)
          {
            await _messageProcessor.SendProtocolViolation(this);
            break;
          }

          if (rawMessage.Data == null)
            break;

          IProtocolMessage<TMessage> message = CreateMessageFromRawData(rawMessage.Data);
          if (message == null) {
            await _messageProcessor.SendProtocolViolation(this);
            break;
          }

          var success = await _messageProcessor.ProcessMessageAsync(this, message);
          if (!success)
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-)");
    }




    /// <summary>
    /// Sends a request message to the client over the open network stream and stores the request to the list of unfinished requests.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <param name="Context">Caller's defined context to store information required for later response processing.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public async Task<bool> SendMessageAndSaveUnfinishedRequestAsync(IProtocolMessage<TMessage> Message, object Context)
    {
      log.Trace("()");
      bool res = false;

      var unfinishedRequest = new UnfinishedRequest<TMessage>(Message, Context);
      if (AddUnfinishedRequest(unfinishedRequest))
      {
        res = await SendMessageInternalAsync(Message);
        if (!res) RemoveUnfinishedRequest(unfinishedRequest.RequestMessage.Id);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Handles client disconnection. Destroys objects that are connected to this client 
    /// and frees the resources.
    /// </summary>
    public virtual async Task HandleDisconnect()
    {
      log.Trace("()");

      await EmptyStream();

      log.Trace("(-)");
    }


    /// <summary>
    /// Adds unfinished request to the list of unfinished requests.
    /// </summary>
    /// <param name="Request">Request to add to the list.</param>
    /// <returns>true if the function succeeds, false if the number of unfinished requests is over the limit.</returns>
    public bool AddUnfinishedRequest(UnfinishedRequest<TMessage> Request)
    {
      log.Trace("(Request.RequestMessage.Id:{0})", Request.RequestMessage.Id);

      bool res = false;
      lock (_unfinishedRequestsLock)
      {
        if (_unfinishedRequests.Count < MaxUnfinishedRequests)
        {
          _unfinishedRequests.Add(Request.RequestMessage.Id, Request);
          res = true;
        }
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res, _unfinishedRequests.Count);
      return res;
    }

    /// <summary>
    /// Removes unfinished request from the list of unfinished requests.
    /// </summary>
    /// <param name="Id">Identifier of the unfinished request message to remove from the list.</param>
    public bool RemoveUnfinishedRequest(uint Id)
    {
      log.Trace("(Id:{0})", Id);

      bool res = false;
      lock (_unfinishedRequestsLock)
      {
        res = _unfinishedRequests.Remove(Id);
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res, _unfinishedRequests.Count);
      return res;
    }


    /// <summary>
    /// Finds an unfinished request message by its ID and removes it from the list.
    /// </summary>
    /// <param name="Id">Identifier of the message to find.</param>
    /// <returns>Unfinished request with the given ID, or null if no such request is in the list.</returns>
    public UnfinishedRequest<TMessage> GetAndRemoveUnfinishedRequest(uint Id)
    {
      log.Trace("(Id:{0})", Id);

      UnfinishedRequest<TMessage> res = null;
      lock (_unfinishedRequestsLock)
      {
        if (_unfinishedRequests.TryGetValue(Id, out res))
          _unfinishedRequests.Remove(Id);
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res != null ? "UnfinishedRequest" : "null", _unfinishedRequests.Count);
      return res;
    }


    /// <summary>
    /// Finds all unfinished request message and removes them from the list.
    /// </summary>
    /// <returns>Unfinished request messages of the client.</returns>
    public List<UnfinishedRequest<TMessage>> GetAndRemoveUnfinishedRequests()
    {
      log.Trace("()");

      var res = new List<UnfinishedRequest<TMessage>>();
      lock (_unfinishedRequestsLock)
      {
        res = _unfinishedRequests.Values.ToList();
        _unfinishedRequests.Clear();
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }



    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected override void Dispose(bool Disposing)
    {
      bool disposedAlready = false;
      lock (disposingLock)
      {
        disposedAlready = disposed;
        disposed = true;
      }
      if (disposedAlready) return;

      if (Disposing)
      {
        base.Dispose(Disposing);
      }
    }
  }
}
