﻿using Google.Protobuf;
using IopCommon;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace IopServerCore.Network
{
  /// <summary>
  /// Base class for TCP clients following IoP message protocol.
  /// </summary>
  public abstract class ClientBase<TMessage> : IDisposable
  {
    /// <summary>Instance logger.</summary>
    protected Logger log;

    /// <summary>IP address and port of the other end point of the connection.</summary>
    private IPEndPoint remoteEndPoint;
    /// <summary>IP address and port of the other end point of the connection.</summary>
    public IPEndPoint RemoteEndPoint { get { return remoteEndPoint; } }

    /// <summary>TCP client class that holds the connection and allows communication with the client.</summary>
    private TcpClient tcpClient;
    /// <summary>TCP client class that holds the connection and allows communication with the client.</summary>
    public TcpClient TcpClient { get { return tcpClient; } }

    /// <summary>Network or SSL stream to the client.</summary>
    private Stream stream;
    /// <summary>Network or SSL stream to the client.</summary>
    public Stream Stream { get { return stream; } }


    /// <summary>If set to true, the client should be disconnected as soon as possible.</summary>
    public bool ForceDisconnect;


    /// <summary>true if the client is connected to the TLS port, false otherwise.</summary>
    private bool useTls;
    /// <summary>true if the client is connected to the TLS port, false otherwise.</summary>
    public bool UseTls { get { return useTls; } }

    /// <summary>Lock object for writing to the stream.</summary>
    private SemaphoreSlim streamWriteLock = new SemaphoreSlim(1);


    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public abstract IProtocolMessage<TMessage> CreateMessageFromRawData(byte[] Data);

    /// <summary>
    /// Converts an IoP Network protocol message to a binary format.
    /// </summary>
    /// <param name="Message">IoP Network protocol message.</param>
    /// <returns>Binary representation of the message to be sent over the network.</returns>
    public abstract byte[] MessageToByteArray(IProtocolMessage<TMessage> Message);



    /// <summary>
    /// Initiates the instance of the TCP client using information about the targer peer it is going to connect to.
    /// </summary>
    /// <param name="RemoteEndPoint">End point of the target peer the client is going to connect to.</param>
    /// <param name="UseTls">true if TLS should be used, false otherwise.</param>
    /// <param name="IdBase">Number to start message identifier series with.</param>
    public ClientBase(IPEndPoint RemoteEndPoint, bool UseTls, uint IdBase = 0)
    {
      useTls = UseTls;
      remoteEndPoint = RemoteEndPoint;
      tcpClient = new TcpClient();
      tcpClient.LingerState = new LingerOption(true, 0);
      tcpClient.NoDelay = true;
    }

    /// <summary>
    /// Initiates the instance of the TCP client from existing and already connected TcpClient.
    /// </summary>
    /// <param name="Client">TCP client that is already connected to the other peer.</param>
    /// <param name="UseTls">true if TLS should be used, false otherwise.</param>
    /// <param name="IdBase">Number to start message identifier series with.</param>
    public ClientBase(TcpClient Client, bool UseTls, uint IdBase = 0)
    {
      tcpClient = Client;
      tcpClient.LingerState = new LingerOption(true, 0);
      tcpClient.NoDelay = true;

      remoteEndPoint = (IPEndPoint)Client.Client.RemoteEndPoint;

      useTls = UseTls;
      stream = TcpClient.GetStream();
      if (useTls)
        stream = new SslStream(stream, false, PeerCertificateValidationCallback);
    }



    /// <summary>
    /// Connects to a specific IP address and port and initializes stream.
    /// If TLS is used, client authentication is done as well.
    /// </summary>
    /// <returns>true if the connection was established succcessfully, false otherwise.</returns>
    public virtual async Task<bool> ConnectAsync()
    {
      log.Trace("(Address:{0},Port:{1})", remoteEndPoint.Address, remoteEndPoint.Port);

      bool res = false;
      try
      {
        await TcpClient.ConnectAsync(remoteEndPoint.Address, remoteEndPoint.Port);
        stream = TcpClient.GetStream();
        if (UseTls)
        {
          SslStream sslStream = new SslStream(stream, false, PeerCertificateValidationCallback);
          await sslStream.AuthenticateAsClientAsync("", null, SslProtocols.Tls12, false);
          stream = sslStream;
        }
        res = true;
      }
      catch (Exception e)
      {
        log.Debug("Unable to connect to {0}, error exception: {1}", remoteEndPoint, e.ToString());
      }


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public virtual async Task<bool> SendMessageAsync(IProtocolMessage<TMessage> Message)
    {
      log.Trace("()");

      bool res = await SendMessageInternalAsync(Message);

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the message was sent successfully to the target recipient.</returns>
    protected async Task<bool> SendMessageInternalAsync(IProtocolMessage<TMessage> Message)
    {
      log.Trace("()");

      bool res = false;

      string msgStr = Message.ToString();
      log.Trace("Sending message:\n{0}", msgStr.SubstrMax(512));
      byte[] messageBytes = MessageToByteArray(Message);

      await streamWriteLock.WaitAsync();
      try
      {
        if (Stream != null)
        {
          await Stream.WriteAsync(messageBytes, 0, messageBytes.Length);
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
        streamWriteLock.Release();
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// The function attempts to read all pending data on the stream in order to clear it. 
    /// This is necessary in order for all pending outgoing data to be delivered to the other party
    /// before we close the connection.
    /// </summary>
    public async Task EmptyStream()
    {
      log.Trace("()");

      bool clear = false;
      byte[] buf = new byte[8192];

      await streamWriteLock.WaitAsync();

      while (!clear)
      {
        try
        {
          Task<int> readTask = Stream.ReadAsync(buf, 0, buf.Length);
          if (readTask.Wait(50)) clear = readTask.Result < buf.Length;
          else clear = true;
        }
        catch
        {
          clear = true;
        }
      }

      streamWriteLock.Release();
      log.Trace("(-)");
    }



    /// <summary>
    /// Closes connection if it is opened and frees used resources.
    /// </summary>
    public async Task CloseConnectionAsync()
    {
      log.Trace("()");

      await streamWriteLock.WaitAsync();

      CloseConnectionLocked();

      streamWriteLock.Release();

      log.Trace("(-)");
    }

    /// <summary>
    /// Closes connection if it is opened and frees used resources.
    /// </summary>
    public void CloseConnection()
    {
      log.Trace("()");

      streamWriteLock.Wait();

      CloseConnectionLocked();

      streamWriteLock.Release();

      log.Trace("(-)");
    }

    
    /// <summary>
    /// Closes connection if it is opened and frees used resources, assuming StreamWriteLock is acquired.
    /// </summary>
    public void CloseConnectionLocked()
    {
      log.Trace("()");

      Stream streamToClose = stream;
      stream = null;
      if (streamToClose != null) streamToClose.Dispose();

      TcpClient clientToClose = tcpClient;
      tcpClient = null;
      if (clientToClose != null) clientToClose.Dispose();

      log.Trace("(-)");
    }


    /// <summary>
    /// Sets a new value to client's end point and allows it to connect to different end point.
    /// </summary>
    /// <param name="EndPoint">New target end point.</param>
    public void SetRemoteEndPoint(IPEndPoint EndPoint)
    {
      log.Trace("()");

      streamWriteLock.Wait();

      CloseConnectionLocked();
      remoteEndPoint = EndPoint;
      tcpClient = new TcpClient();
      tcpClient.LingerState = new LingerOption(true, 0);
      tcpClient.NoDelay = true;

      streamWriteLock.Release();

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
      bool disposedAlready = false;
      lock (disposingLock)
      {
        disposedAlready = disposed;
        disposed = true;
      }
      if (disposedAlready) return;

      if (Disposing)
      {
        CloseConnection();
      }
    }
  }
}
