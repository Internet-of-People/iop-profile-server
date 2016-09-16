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

namespace HomeNet.Network
{
  public enum ClientStatus { ReadingHeader, ReadingBody }

  /// <summary>
  /// Client class represents any kind of TCP client that connects to one of the node's TCP servers.
  /// </summary>
  public class Client : IDisposable
  {
    private PrefixLogger log;

    /// <summary>Role server assigned client identifier.</summary>
    public uint Id;

    /// <summary>Source IP address and port of the client.</summary>
    public EndPoint RemoteEndPoint;

    /// <summary>TCP client class that holds the connection and allows communication with the client.</summary>
    public TcpClient TcpClient;

    /// <summary>Network or SSL stream to the client.</summary>
    public Stream Stream;

    /// <summary>true if the client is connected to the TLS port, false otherwise.</summary>
    public bool UseTls;

    /// <summary>UTC time before next message has to come over the connection from this client or the node can close the connection due to inactivity.</summary>
    public DateTime NextKeepAliveTime;

    /// <summary>TcpRoleServer.ClientKeepAliveIntervalSeconds for end user client device connections, 
    /// TcpRoleServer.NodeKeepAliveIntervalSeconds for node to node or unknown connections.</summary>
    public int KeepAliveIntervalSeconds;

    /// <summary>
    /// Creates the encapsulation for a new TCP server client.
    /// </summary>
    /// <param name="TcpClient">TCP client class that holds the connection and allows communication with the client.</param>
    /// <param name="UseTls">true if the client is connected to the TLS port, false otherwise.</param>
    /// <param name="KeepAliveIntervalSeconds">Number of seconds for the connection to this client to be without any message until the node can close it for inactivity.</param>
    public Client(TcpClient TcpClient, bool UseTls, int KeepAliveIntervalSeconds)
    {
      this.TcpClient = TcpClient;
      RemoteEndPoint = this.TcpClient.Client.RemoteEndPoint;

      string logPrefix = string.Format("[{0}]", RemoteEndPoint);
      string logName = "HomeNet.Network.Client";
      this.log = new PrefixLogger(logName, logPrefix);

      log.Trace("(UseTls:{0},KeepAliveIntervalSeconds:{1})", UseTls, KeepAliveIntervalSeconds);

      this.KeepAliveIntervalSeconds = KeepAliveIntervalSeconds;
      NextKeepAliveTime = DateTime.UtcNow.AddSeconds(this.KeepAliveIntervalSeconds);

      this.TcpClient.LingerState = new LingerOption(true, 0);
      this.UseTls = UseTls;
      Stream = this.TcpClient.GetStream();
      if (this.UseTls)
        Stream = new SslStream(Stream, false, PeerCertificateValidationCallback);

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
          if (Stream != null) Stream.Dispose();
          Stream = null;

          if (TcpClient != null) TcpClient.Dispose();
          TcpClient = null;

          disposed = true;
        }
      }
    }
  }
}
