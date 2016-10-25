using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using HomeNetProtocol;
using System.Net;
using System.Runtime.InteropServices;
using HomeNet.Kernel;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using HomeNet.Utils;
using HomeNet.Data.Models;
using Iop.Homenode;
using HomeNetCrypto;
using System.Security.Authentication;

namespace HomeNet.Network
{
  /// <summary>
  /// On the lowest socket level, the receiving part of the client can either be reading the message prefix header or the body.
  /// </summary>
  public enum ClientStatus
  {
    /// <summary>Server is waiting for the message header to be read from the socket.</summary>
    ReadingHeader,

    /// <summary>Server read the message header and is now waiting for the message body to be read.</summary>
    ReadingBody
  }

  
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
    Authenticated
  };

  /// <summary>
  /// Client class represents any kind of TCP client that connects to one of the node's TCP servers.
  /// </summary>
  public class Client : IDisposable
  {
    private PrefixLogger log;

    
    /// <summary>Maximum number of bytes that application service name can occupy.</summary>
    public const int MaxApplicationServiceNameLengthBytes = 32;

    /// <summary>Maximum number of application services that a client can have enabled within a session.</summary>
    public const int MaxClientApplicationServices = 50;

    /// <summary>Maximal number of unconfirmed messages that one relay client can send to the other one.</summary>
    public const int MaxUnfinishedRequests = 20;


    /// <summary>Role server assigned client identifier.</summary>
    public ulong Id;

    /// <summary>Source IP address and port of the client.</summary>
    public EndPoint RemoteEndPoint;

    /// <summary>TCP client class that holds the connection and allows communication with the client.</summary>
    public TcpClient TcpClient;

    /// <summary>Network or SSL stream to the client.</summary>
    public Stream Stream;

    /// <summary>Lock object for writing to the stream.</summary>
    public SemaphoreSlim StreamWriteLock = new SemaphoreSlim(1);

    /// <summary>true if the client is connected to the TLS port, false otherwise.</summary>
    public bool UseTls;

    /// <summary>UTC time before next message has to come over the connection from this client or the node can close the connection due to inactivity.</summary>
    public DateTime NextKeepAliveTime;

    /// <summary>TcpRoleServer.ClientKeepAliveIntervalSeconds for end user client device connections, 
    /// TcpRoleServer.NodeKeepAliveIntervalSeconds for node to node or unknown connections.</summary>
    public int KeepAliveIntervalSeconds;

    /// <summary>Protocol message builder.</summary>
    public MessageBuilder MessageBuilder;


    /// <summary>If set to true, the client should be disconnected as soon as possible.</summary>
    public bool ForceDisconnect;

    // Client Context Section
    
    /// <summary>Current status of the conversation with the client.</summary>
    public ClientConversationStatus ConversationStatus;

    /// <summary>Client's public key.</summary>
    public byte[] PublicKey;

    /// <summary>Client's public key hash.</summary>
    public byte[] IdentityId;

    /// <summary>Random data used for client's authentication.</summary>
    public byte[] AuthenticationChallenge;

    /// <summary>true if the network client represents identity hosted by the node that is checked-in, false otherwise.</summary>
    public bool IsOurCheckedInClient;

    /// <summary>Client's application services available for the current session.</summary>
    public ApplicationServices ApplicationServices;

    /// <summary>
    /// If the client is connected to clAppService because of the application service call,
    /// this represents the relay object. Otherwise, this is null, including the situation 
    /// when this client is connected to clCustomerPort and is a callee of the relay, 
    /// but this connection is not the one being relayed.
    /// </summary>
    public RelayConnection Relay;

    // \Client Context Section


    /// <summary>List of unprocessed requests that we expect to receive responses to mapped by Message.id.</summary>
    private Dictionary<uint, UnfinishedRequest> unfinishedRequests = new Dictionary<uint, UnfinishedRequest>();

    /// <summary>Lock for access to unfinishedRequests list.</summary>
    private object unfinishedRequestsLock = new object();


    /// <summary>Server to which the client is connected to.</summary>
    private TcpRoleServer server;

    /// <summary>Component responsible for processing logic behind incoming messages.</summary>
    private MessageProcessor messageProcessor;


    /// <summary>
    /// Creates the encapsulation for a new TCP server client.
    /// </summary>
    /// <param name="Server">Role server that the client connected to.</param>
    /// <param name="TcpClient">TCP client class that holds the connection and allows communication with the client.</param>
    /// <param name="Id">Unique identifier of the client's connection.</param>
    /// <param name="UseTls">true if the client is connected to the TLS port, false otherwise.</param>
    /// <param name="KeepAliveIntervalSeconds">Number of seconds for the connection to this client to be without any message until the node can close it for inactivity.</param>
    public Client(TcpRoleServer Server, TcpClient TcpClient, ulong Id, bool UseTls, int KeepAliveIntervalSeconds)
    {
      this.TcpClient = TcpClient;
      this.Id = Id;
      RemoteEndPoint = this.TcpClient.Client.RemoteEndPoint;

      server = Server;
      string logPrefix = string.Format("[{0}<=>{1}|0x{2:X16}] ", server.EndPoint, RemoteEndPoint, Id);
      string logName = "HomeNet.Network.Client";
      this.log = new PrefixLogger(logName, logPrefix);

      log.Trace("(UseTls:{0},KeepAliveIntervalSeconds:{1})", UseTls, KeepAliveIntervalSeconds);

      messageProcessor = new MessageProcessor(server, logPrefix);

      this.KeepAliveIntervalSeconds = KeepAliveIntervalSeconds;
      NextKeepAliveTime = DateTime.UtcNow.AddSeconds(this.KeepAliveIntervalSeconds);
    
      this.TcpClient.LingerState = new LingerOption(true, 0);
      this.TcpClient.NoDelay = true;

      this.UseTls = UseTls;
      Stream = this.TcpClient.GetStream();
      if (this.UseTls)
        Stream = new SslStream(Stream, false, PeerCertificateValidationCallback);

      MessageBuilder = new MessageBuilder(server.IdBase, new List<byte[]>() { new byte[] { 1, 0, 0 } }, Base.Configuration.Keys);
      ConversationStatus = ClientConversationStatus.NoConversation;
      IsOurCheckedInClient = false;

      ApplicationServices = new ApplicationServices(logPrefix);

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
          await sslStream.AuthenticateAsServerAsync(Base.Configuration.TcpServerTlsCertificate, false, SslProtocols.Tls12, false);
        }

        Stream clientStream = Stream;
        byte[] messageHeaderBuffer = new byte[ProtocolHelper.HeaderSize];
        byte[] messageBuffer = null;
        ClientStatus clientStatus = ClientStatus.ReadingHeader;
        uint messageSize = 0;
        int messageHeaderBytesRead = 0;
        int messageBytesRead = 0;

        while (!server.ShutdownSignaling.IsShutdown)
        {
          Task<int> readTask = null;
          int remain = 0;

          log.Trace("Client status is '{0}'.", clientStatus);
          switch (clientStatus)
          {
            case ClientStatus.ReadingHeader:
              {
                remain = ProtocolHelper.HeaderSize - messageHeaderBytesRead;
                readTask = clientStream.ReadAsync(messageHeaderBuffer, messageHeaderBytesRead, remain, server.ShutdownSignaling.ShutdownCancellationTokenSource.Token);
                break;
              }

            case ClientStatus.ReadingBody:
              {
                remain = (int)messageSize - messageBytesRead;
                readTask = clientStream.ReadAsync(messageBuffer, ProtocolHelper.HeaderSize + messageBytesRead, remain, server.ShutdownSignaling.ShutdownCancellationTokenSource.Token);
                break;
              }

            default:
              log.Error("Invalid client status '{0}'.", clientStatus);
              break;
          }

          if (readTask == null)
            break;

          log.Trace("{0} bytes remains to be read.", remain);

          int readAmount = await readTask;
          if (readAmount == 0)
          {
            log.Info("Connection has been closed.");
            break;
          }

          log.Trace("Read completed: {0} bytes.", readAmount);

          bool protoViolationDisconnect = false;
          bool disconnect = false;
          switch (clientStatus)
          {
            case ClientStatus.ReadingHeader:
              {
                messageHeaderBytesRead += readAmount;
                if (readAmount == remain)
                {
                  if (messageHeaderBuffer[0] == 0x0D)
                  {
                    uint hdr = ProtocolHelper.GetValueLittleEndian(messageHeaderBuffer, 1);
                    if (hdr + ProtocolHelper.HeaderSize <= ProtocolHelper.MaxSize)
                    {
                      messageSize = hdr;
                      clientStatus = ClientStatus.ReadingBody;
                      messageBuffer = new byte[ProtocolHelper.HeaderSize + messageSize];
                      Array.Copy(messageHeaderBuffer, messageBuffer, messageHeaderBuffer.Length);
                      log.Trace("Reading of message header completed. Message size is {0} bytes.", messageSize);
                    }
                    else
                    {
                      log.Warn("Client claimed message of size {0} which exceeds the maximum.", hdr + ProtocolHelper.HeaderSize);
                      protoViolationDisconnect = true;
                    }
                  }
                  else
                  {
                    log.Warn("Message has invalid format - it's first byte is 0x{0:X2}, should be 0x0D.", messageHeaderBuffer[0]);
                    protoViolationDisconnect = true;
                  }
                }
                break;
              }

            case ClientStatus.ReadingBody:
              {
                messageBytesRead += readAmount;
                if (readAmount == remain)
                {
                  clientStatus = ClientStatus.ReadingHeader;
                  messageBytesRead = 0;
                  messageHeaderBytesRead = 0;
                  log.Trace("Reading of message size {0} completed.", messageSize);

                  Message incomingMessage = CreateMessageFromRawData(messageBuffer);
                  if (incomingMessage != null) disconnect = !await messageProcessor.ProcessMessageAsync(this, incomingMessage);
                  else protoViolationDisconnect = true;
                }
                break;
              }
          }

          if (protoViolationDisconnect)
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
        if ((e is ObjectDisposedException) || (e is IOException)) log.Info("Connection to client has been terminated.");
        else log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public Message CreateMessageFromRawData(byte[] Data)
    {
      log.Trace("()");

      Message res = null;
      try
      {
        res = MessageWithHeader.Parser.ParseFrom(Data).Body;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Trace("(-):{0}", res != null ? "Message" : "null");
      return res;
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
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public async Task<bool> SendMessageAsync(Message Message)
    {
      log.Trace("()");

      bool res = await SendMessageInternalAsync(Message);
      if (res)
      {
        // If the message was sent successfully to the target, we close the connection only in case of protocol violation error.
        res = Message.Response.Status != Status.ErrorProtocolViolation;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the message was sent successfully to the target recipient.</returns>
    private async Task<bool> SendMessageInternalAsync(Message Message)
    {
      log.Trace("()");

      bool res = false;

      string msgStr = Message.ToString();
      log.Trace("Sending response to client:\n{0}", msgStr.Substring(0, Math.Min(msgStr.Length, 512)));
      byte[] responseBytes = ProtocolHelper.GetMessageBytes(Message);

      await StreamWriteLock.WaitAsync();
      try
      {
        if (Stream != null)
        {
          await Stream.WriteAsync(responseBytes, 0, responseBytes.Length);
          res = true;
        }
        else log.Info("Connection to the client has been terminated.");
      }
      catch (IOException)
      {
        log.Info("Connection to the client has been terminated.");
      }
      finally
      {
        StreamWriteLock.Release();
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Handles client disconnection. Destroys objects that are connected to this client 
    /// and frees the resources.
    /// </summary>
    public async Task HandleDisconnect()
    {
      log.Trace("()");

      if (Relay != null)
      {
        // This connection is on clAppService port. There might be the other peer still connected 
        // to this relay, so we have to make sure that other peer is disconnected as well.
        await Relay.HandleDisconnectedClient(this, true);
      }

      log.Trace("(-)");
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
        if (unfinishedRequests.Count < MaxUnfinishedRequests)
        {
          unfinishedRequests.Add(Request.RequestMessage.Id, Request);
          res = true;
        }
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



    /// <summary>
    /// Closes connection if it is opened and frees used resources.
    /// </summary>
    public async Task CloseConnection()
    {
      log.Trace("()");

      await StreamWriteLock.WaitAsync();

      CloseConnectionLocked();

      StreamWriteLock.Release();

      log.Trace("(-)");
    }


    /// <summary>
    /// Closes connection if it is opened and frees used resources, assuming StreamWriteLock is acquired.
    /// </summary>
    public void CloseConnectionLocked()
    {
      log.Trace("()");

      Stream stream = Stream;
      Stream = null;
      if (stream != null) stream.Dispose();

      TcpClient tcpClient = TcpClient;
      TcpClient = null;
      if (tcpClient != null) tcpClient.Dispose();

      log.Trace("(-)");
    }


    /// <summary>
    /// Callback routine that validates client connection to TLS port.
    /// As we do not perform client certificate validation, we just return true to allow everyone to connect to our server.
    /// </summary>
    /// <param name="sender"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="certificate"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="chain"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="sslPolicyErrors"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <returns><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</returns>
    public static bool PeerCertificateValidationCallback(Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
      return true;
    }


    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class.
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected virtual void Dispose(bool Disposing)
    {
      if (disposed) return;

      if (Disposing)
      {
        lock (disposingLock)
        {
          CloseConnection().Wait();
          disposed = true;
        }
      }
    }
  }
}
