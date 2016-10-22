using HomeNetCrypto;
using HomeNetProtocol;
using Iop.Homenode;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace HomeNetProtocolTests
{
  /// <summary>
  /// Implements a simple TCP IoP client with TLS support.
  /// </summary>
  public class ProtocolClient : IDisposable
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("Test.ProtocolClient");

    /// <summary>TCP client for communication with the server.</summary>
    private TcpClient client;

    /// <summary>
    /// Normal or TLS stream for sending and receiving data over TCP client. 
    /// In case of the TLS stream, the underlaying stream is going to be closed automatically.
    /// </summary>
    private Stream stream;

    /// <summary>Message builder for easy creation of protocol message.</summary>
    public MessageBuilder MessageBuilder;

    /// <summary>Client's identity.</summary>
    private KeysEd25519 keys;

    /// <summary>Node's public key received when starting conversation.</summary>
    public byte[] NodeKey;

    /// <summary>Challenge that the node send to the client when starting conversation.</summary>
    public byte[] Challenge;
    
    
    /// <summary>
    /// Initializes the instance.
    /// </summary>
    public ProtocolClient():
      this(0)
    {
    }

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    /// <param name="IdBase">Base for message numbering.</param>
    public ProtocolClient(uint IdBase):
      this(IdBase, new byte[] { 1, 0, 0 })
    {
    }

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    /// <param name="IdBase">Base for message numbering.</param>
    /// <param name="ProtocolVersion">Protocol version in binary form.</param>
    public ProtocolClient(uint IdBase, byte[] ProtocolVersion):
      this(IdBase, ProtocolVersion, Ed25519.GenerateKeys())
    {
    }

    /// <summary>
    /// Initializes the instance.
    /// </summary>
    /// <param name="IdBase">Base for message numbering.</param>
    /// <param name="ProtocolVersion">Protocol version in binary form.</param>
    /// <param name="Keys">Keys that represent the client's identity.</param>
    public ProtocolClient(uint IdBase, byte[] ProtocolVersion, KeysEd25519 Keys)
    {
      client = new TcpClient();
      keys = Keys;
      MessageBuilder = new MessageBuilder(IdBase, new List<byte[]>() { ProtocolVersion }, keys);
    }


    /// <summary>
    /// Connects to the target server address on the specific port and optionally performs TLS handshake.
    /// </summary>
    /// <param name="Address">IP address of the target server.</param>
    /// <param name="Port">TCP port to connect to.</param>
    /// <param name="UseTls">If true, the TLS handshake is performed after the connection is established.</param>
    public async Task ConnectAsync(IPAddress Address, int Port, bool UseTls)
    {
      log.Trace("(Address:'{0}',Port:{1},UseTls:{2})", Address, Port, UseTls);

      await client.ConnectAsync(Address, Port);

      stream = client.GetStream();
      if (UseTls)
      {
        SslStream sslStream = new SslStream(stream, false, PeerCertificateValidationCallback);
        await sslStream.AuthenticateAsClientAsync("", null, SslProtocols.Tls12, false);
        stream = sslStream;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Sends raw binary data over the network stream.
    /// </summary>
    /// <param name="Data">Binary data to send.</param>
    public async Task SendRawAsync(byte[] Data)
    {
      log.Trace("(Message.Length:{0})", Data.Length);

      await stream.WriteAsync(Data, 0, Data.Length);

      log.Trace("(-)");
    }

    /// <summary>
    /// Sends IoP protocol message over the network stream.
    /// </summary>
    /// <param name="Data">Message to send.</param>
    public async Task SendMessageAsync(Message Data)
    {
      log.Trace("()\n{0}", Data);

      byte[] rawData = ProtocolHelper.GetMessageBytes(Data);
      await stream.WriteAsync(rawData, 0, rawData.Length);

      log.Trace("(-)");
    }


    /// <summary>
    /// Reads and parses protocol message from the network stream.
    /// </summary>
    /// <returns>Parsed protocol message or null if the function fails.</returns>
    public async Task<Message> ReceiveMessageAsync()
    {
      log.Trace("()");

      Message res = null;

      byte[] header = new byte[ProtocolHelper.HeaderSize];
      int headerBytesRead = 0;
      int remain = header.Length;

      bool done = false;
      log.Trace("Reading message header.");
      while (!done && (headerBytesRead < header.Length))
      {
        int readAmount = await stream.ReadAsync(header, headerBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the header.");
          done = true;
          break;
        }

        headerBytesRead += readAmount;
        remain -= readAmount;
      }

      uint messageSize = BitConverter.ToUInt32(header, 1);
      log.Trace("Message body size is {0} bytes.", messageSize);

      byte[] messageBytes = new byte[ProtocolHelper.HeaderSize + messageSize];
      Array.Copy(header, messageBytes, header.Length);

      remain = (int)messageSize;
      int messageBytesRead = 0;
      while (!done && (messageBytesRead < messageSize))
      {
        int readAmount = await stream.ReadAsync(messageBytes, ProtocolHelper.HeaderSize + messageBytesRead, remain);
        if (readAmount == 0)
        {
          log.Trace("Connection to server closed while reading the body.");
          done = true;
          break;
        }

        messageBytesRead += readAmount;
        remain -= readAmount;
      }

      res = MessageWithHeader.Parser.ParseFrom(messageBytes).Body;

      log.Trace("(-):\n{0}", res);
      return res;
    }


    /// <summary>
    /// Starts conversation with the server the client is connected to and checks whether the server response contains expected values.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> StartConversationAsync()
    {
      log.Trace("()");

      Message requestMessage = MessageBuilder.CreateStartConversationRequest();
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      byte[] receivedVersion = responseMessage.Response.ConversationResponse.Start.Version.ToByteArray();
      byte[] expectedVersion = new byte[] { 1, 0, 0 };
      bool versionOk = StructuralComparisons.StructuralComparer.Compare(receivedVersion, expectedVersion) == 0;

      bool pubKeyLenOk = responseMessage.Response.ConversationResponse.Start.PublicKey.Length == 32;
      bool challengeOk = responseMessage.Response.ConversationResponse.Start.Challenge.Length == 32;

      NodeKey = responseMessage.Response.ConversationResponse.Start.PublicKey.ToByteArray();
      Challenge = responseMessage.Response.ConversationResponse.Start.Challenge.ToByteArray();

      bool res = idOk && statusOk && versionOk && pubKeyLenOk && challengeOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Establishes a home node for the client's identity using the already opened connection to the node.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> EstablishHomeNodeAsync()
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      Message requestMessage = MessageBuilder.CreateHomeNodeRequestRequest(null);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool homeNodeRequestOk = idOk && statusOk;

      bool res = startConversationOk && homeNodeRequestOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs a check-in process for the client's identity using the already opened connection to the node.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> CheckInAsync()
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      Message requestMessage = MessageBuilder.CreateCheckInRequest(Challenge);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool checkInOk = idOk && statusOk;

      bool res = startConversationOk && checkInOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Performs an identity verification process for the client's identity using the already opened connection to the node.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> VerifyIdentityAsync()
    {
      log.Trace("()");

      bool startConversationOk = await StartConversationAsync();

      Message requestMessage = MessageBuilder.CreateVerifyIdentityRequest(Challenge);
      await SendMessageAsync(requestMessage);
      Message responseMessage = await ReceiveMessageAsync();

      bool idOk = responseMessage.Id == requestMessage.Id;
      bool statusOk = responseMessage.Response.Status == Status.Ok;

      bool verifyIdentityOk = idOk && statusOk;

      bool res = startConversationOk && verifyIdentityOk;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Closes an open connection and reinitialize the TCP client so that it can be used again.
    /// </summary>
    public void CloseConnection()
    {
      if (stream != null) stream.Dispose();
      if (client != null) client.Dispose();
      client = new TcpClient();
      NodeKey = null;
      Challenge = null;
      MessageBuilder.ResetId();
    }


    /// <summary>
    /// Obtains network stream of the client.
    /// </summary>
    /// <returns>Network stream of the client.</returns>
    public Stream GetStream()
    {
      return stream;
    }


    /// <summary>
    /// Obtains network identifier of the client's identity.
    /// </summary>
    /// <returns>Network identifier in its binary form.</returns>
    public byte[] GetIdentityId()
    {
      return Crypto.Sha1(keys.PublicKey);
    }

    /// <summary>
    /// Obtains keys that represent the client's identity.
    /// </summary>
    /// <returns>Keys that represent the client's identity.</returns>
    public KeysEd25519 GetIdentityKeys()
    {
      return keys;
    }


    /// <summary>
    /// Callback routine that validates server TLS certificate.
    /// As we do not perform certificate validation, we just return true.
    /// </summary>
    /// <param name="sender"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="certificate"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="chain"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <param name="sslPolicyErrors"><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</param>
    /// <returns><see cref="System.Net.Security.RemoteCertificateValidationCallback"/> Delegate.</returns>
    public static bool PeerCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
      return true;
    }

    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

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
        if (stream != null) stream.Dispose();
        stream = null;

        if (client != null) client.Dispose();
        client = null;

        disposed = true;
      }
    }
  }
}
