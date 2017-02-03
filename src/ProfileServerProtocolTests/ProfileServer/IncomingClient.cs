using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using ProfileServerProtocol;
using System.Net;
using System.Runtime.InteropServices;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Iop.Profileserver;
using ProfileServerCrypto;
using System.Security.Authentication;

namespace ProfileServerProtocolTests
{
  /// <summary>Different states of conversation between the client and the server.</summary>
  public enum ClientConversationStatus
  {
    /// <summary>Client has not initiated a conversation yet.</summary>
    NoConversation,

    /// <summary>There is an established conversation with the client, but no authentication has been done.</summary>
    ConversationStarted,

    /// <summary>There is an established conversation with the non-customer client and the verification process has already been completed.</summary>
    Verified,

    /// <summary>There is an established conversation with the client and the authentication process has already been completed.</summary>
    Authenticated,

    /// <summary>The conversation status of the client is ConversationStarted, Verified, or Authenticated.</summary>
    ConversationAny
  };

  /// <summary>
  /// Incoming client class represents any kind of TCP client that connects to one of the profile server's TCP servers.
  /// </summary>
  public class IncomingClient : ClientBase, IDisposable
  {
    /// <summary>Current status of the conversation with the client.</summary>
    public ClientConversationStatus ConversationStatus;

    /// <summary>Client's public key.</summary>
    public byte[] PublicKey;

    /// <summary>Client's public key hash.</summary>
    public byte[] IdentityId;

    /// <summary>Random data used for client's authentication.</summary>
    public byte[] AuthenticationChallenge;


    /// <summary>Server to which the client is connected to.</summary>
    private ProfileServer server;

    /// <summary>Role of the server the client is connected to.</summary>
    private ServerRole serverRole;
    /// <summary>Role of the server the client is connected to.</summary>
    public ServerRole ServerRole { get { return serverRole; } }

    /// <summary>Component responsible for processing logic behind incoming messages.</summary>
    private MessageProcessor messageProcessor;


    /// <summary>List of unprocessed requests that we expect to receive responses to mapped by Message.id.</summary>
    private Dictionary<uint, UnfinishedRequest> unfinishedRequests = new Dictionary<uint, UnfinishedRequest>();

    /// <summary>Lock for access to unfinishedRequests list.</summary>
    private object unfinishedRequestsLock = new object();


    /// <summary>
    /// Creates the instance for a new TCP server client.
    /// </summary>
    /// <param name="Server">Role server that the client connected to.</param>
    /// <param name="TcpClient">TCP client class that holds the connection and allows communication with the client.</param>
    /// <param name="UseTls">true if the client is connected to the TLS port, false otherwise.</param>
    /// <param name="Role">Role of the server the client connected to.</param>
    public IncomingClient(ProfileServer Server, TcpClient TcpClient, bool UseTls, ServerRole Role) :
      base(TcpClient, UseTls, 0, Server.Keys)
    {
      log = NLog.LogManager.GetLogger(string.Format("Test.ProfileServer.{0}.IncomingClient", Server.Name));
      log.Trace("(UseTls:{0})", UseTls);

      serverRole = Role;
      server = Server;
      messageProcessor = new MessageProcessor(server);

      ConversationStatus = ClientConversationStatus.NoConversation;

      log.Trace("(-)");
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
          await sslStream.AuthenticateAsServerAsync(server.TlsCertificate, false, SslProtocols.Tls12, false);
        }

        RawMessageReader messageReader = new RawMessageReader(Stream);
        while (!server.IsShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(server.ShutdownCancellationTokenSource.Token);
          bool disconnect = rawMessage.Data == null;
          bool protocolViolation = rawMessage.ProtocolViolation;
          if (rawMessage.Data != null)
          {
            Message message = CreateMessageFromRawData(rawMessage.Data);
            if (message != null) disconnect = !await messageProcessor.ProcessMessageAsync(this, message);
            else protocolViolation = true;
          }

          if (protocolViolation)
          {
            await messageProcessor.SendProtocolViolation(this);
            break;
          }

          if (disconnect)
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
    public async Task<bool> SendMessageAndSaveUnfinishedRequestAsync(Message Message, object Context)
    {
      log.Trace("()");
      bool res = false;

      UnfinishedRequest unfinishedRequest = new UnfinishedRequest(Message, Context);
      if (AddUnfinishedRequest(unfinishedRequest))
      {
        res = await SendMessageInternalAsync(Message);
        if (res)
        {
          // If the message was sent successfully to the target, we close the connection only in case it was a protocol violation error response.
          if (Message.MessageTypeCase == Message.MessageTypeOneofCase.Response)
            res = Message.Response.Status != Status.ErrorProtocolViolation;
        }
        else RemoveUnfinishedRequest(unfinishedRequest.RequestMessage.Id);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Adds unfinished request to the list of unfinished requests.
    /// </summary>
    /// <param name="Request">Request to add to the list.</param>
    /// <returns>true if the function succeeds, false if the number of unfinished requests is over the limit.</returns>
    public bool AddUnfinishedRequest(UnfinishedRequest Request)
    {
      log.Trace("(Request.RequestMessage.Id:{0})", Request.RequestMessage.Id);

      bool res = false;
      lock (unfinishedRequestsLock)
      {
        unfinishedRequests.Add(Request.RequestMessage.Id, Request);
        res = true;
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res, unfinishedRequests.Count);
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
      lock (unfinishedRequestsLock)
      {
        res = unfinishedRequests.Remove(Id);
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res, unfinishedRequests.Count);
      return res;
    }


    /// <summary>
    /// Finds an unfinished request message by its ID and removes it from the list.
    /// </summary>
    /// <param name="Id">Identifier of the message to find.</param>
    /// <returns>Unfinished request with the given ID, or null if no such request is in the list.</returns>
    public UnfinishedRequest GetAndRemoveUnfinishedRequest(uint Id)
    {
      log.Trace("(Id:{0})", Id);

      UnfinishedRequest res = null;
      lock (unfinishedRequestsLock)
      {
        if (unfinishedRequests.TryGetValue(Id, out res))
          unfinishedRequests.Remove(Id);
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res != null ? "UnfinishedRequest" : "null", unfinishedRequests.Count);
      return res;
    }


    /// <summary>
    /// Finds all unfinished request message and removes them from the list.
    /// </summary>
    /// <returns>Unfinished request messages of the client.</returns>
    public List<UnfinishedRequest> GetAndRemoveUnfinishedRequests()
    {
      log.Trace("()");

      List<UnfinishedRequest> res = new List<UnfinishedRequest>();
      lock (unfinishedRequestsLock)
      {
        res = unfinishedRequests.Values.ToList();
        unfinishedRequests.Clear();
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
