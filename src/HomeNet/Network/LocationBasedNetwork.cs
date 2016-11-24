using System;
using HomeNet.Kernel;
using System.Collections.Generic;
using HomeNet.Config;
using System.Net;
using System.Threading;
using HomeNet.Utils;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.IO;
using HomeNetProtocol;
using Iop.Locnet;

namespace HomeNet.Network
{
  /// <summary>
  /// Location based network (LBN) is a part of IoP that the profile server relies on.
  /// When the node starts, this component connects to LBN and obtains information about the node's neighborhood.
  /// Then it keep receiving updates from LBN about changes in the neighborhood structure.
  /// The profile server needs to share its database of hosted identities with its neighbors and it also accepts 
  /// requests to share foreign profiles and consider them during its own search queries.
  /// </summary>
  public class LocationBasedNetwork : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Network.LocationBasedNetwork");

    /// <summary>Event that is set when LbnConnectionThread is not running.</summary>
    private ManualResetEvent lbnConnectionThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for communication with LBN.</summary>
    private Thread lbnConnectionThread;

    /// <summary>TCP client to connect with LBN server.</summary>
    private TcpClient client;

    /// <summary>Network stream of the TCP connection to LBN server.</summary>
    private NetworkStream stream;

    /// <summary>Lock object for writing to the stream.</summary>
    private SemaphoreSlim streamWriteLock = new SemaphoreSlim(1);

    /// <summary>LBN message builder for the TCP client.</summary>
    private MessageBuilderLocNet messageBuilder;


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        lbnConnectionThread = new Thread(new ThreadStart(LbnConnectionThread));
        lbnConnectionThread.Start();

        res = true;
        Initialized = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();

        CloseClient();

        if ((lbnConnectionThread != null) && !lbnConnectionThreadFinished.WaitOne(10000))
          log.Error("LBN connection thread did not terminated in 10 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      CloseClient();

      if ((lbnConnectionThread != null) && !lbnConnectionThreadFinished.WaitOne(10000))
        log.Error("LBN connection thread did not terminated in 10 seconds.");

      log.Info("(-)");
    }

    /// <summary>
    /// Frees resources used by the TCP client.
    /// </summary>
    private void CloseClient()
    {
      log.Info("()");

      if (stream != null) stream.Dispose();
      if (client != null) client.Dispose();

      log.Info("(-)");
    }


    /// <summary>
    /// Thread that is responsible for connection to LBN and processing LBN updates.
    /// If the LBN is not reachable, the thread will wait until it is reachable.
    /// If connection to LBN is established and closed for any reason, the thread will try to reconnect.
    /// </summary>
    private async void LbnConnectionThread()
    {
      log.Info("()");

      lbnConnectionThreadFinished.Reset();

      while (!ShutdownSignaling.IsShutdown)
      {
        // Connect to LBN server.
        if (await Connect())
        {
          // Announce our primary server interface to LBN.
          if (await AnnouncePrimaryServerRole())
          {
            // Receive initial set of neighborhood nodes and process updates.
            await ReceiveMessageLoop();
          }
        }
      }

      CloseClient();

      lbnConnectionThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Attempts to connect to LBN server in a loop.
    /// </summary>
    /// <returns>true if the function succeeded, false if connection was established before the component shutdown.</returns>
    private async Task<bool> Connect()
    {
      bool res = false;

      // Close TCP connection and dispose client in case it is connected.
      CloseClient();

      // Create new TCP client.
      client = new TcpClient();
      client.NoDelay = true;
      client.LingerState = new LingerOption(true, 0);
      messageBuilder = new MessageBuilderLocNet(0, new List<byte[]> { new byte[] { 1, 0, 0 } });

      while (!res && !ShutdownSignaling.IsShutdown)
      {
        try
        {
          log.Trace("Connecting to LBN server '{0}'.", Base.Configuration.LbnEndPoint);

          await client.ConnectAsync(Base.Configuration.LbnEndPoint.Address, Base.Configuration.LbnEndPoint.Port);
          stream = client.GetStream();
          res = true;
        }
        catch
        {
          log.Error("Unable to connect to LBN server '{0}', waiting 10 seconds and then retrying.", Base.Configuration.LbnEndPoint);
        }

        if (!res)
          await Task.Delay(10000, ShutdownSignaling.ShutdownCancellationTokenSource.Token);
      }

      return res;
    }

    /// <summary>
    /// Announces profile server's primary server role interface to the LBN server.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> AnnouncePrimaryServerRole()
    {
      log.Info("()");

#warning TODO: Implement primary server role announcement
      bool res = true;
      await Task.Delay(1);
      log.Fatal("TODO: UNIMPLEMENTED");

      log.Info("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Reads update messages from network stream and processes them in a loop until the connection terminates 
    /// or until an action (such as a protocol violation) that leads to termination of the connection occurs.
    /// </summary>
    private async Task ReceiveMessageLoop()
    {
      log.Info("()");

      try
      {
        RawMessageReader messageReader = new RawMessageReader(stream);
        while (!ShutdownSignaling.IsShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessage(ShutdownSignaling.ShutdownCancellationTokenSource.Token);
          bool disconnect = rawMessage.Disconnect;
          bool protocolViolation = rawMessage.ProtocolViolation;
          if (rawMessage.Data != null)
          {
            Message message = CreateMessageFromRawData(rawMessage.Data);
            if (message != null) disconnect = !await ProcessMessageAsync(message);
            else protocolViolation = true;
          }

          if (protocolViolation)
          {
            await SendProtocolViolation();
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

      log.Info("(-)");
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
        log.Warn("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Trace("(-):{0}", res != null ? "Message" : "null");
      return res;
    }

    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation()
    {
      MessageBuilderLocNet mb = new MessageBuilderLocNet(0, new List<byte[]>() { new byte[] { 1, 0, 0 } });
      Message response = mb.CreateErrorProtocolViolationResponse(new Message() { Id = 0x0BADC0DE });

      await SendMessageAsync(response);
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
      log.Trace("Sending response to client:\n{0}", msgStr.SubstrMax(512));
      byte[] responseBytes = ProtocolHelper.GetMessageBytes(Message);

      await streamWriteLock.WaitAsync();
      try
      {
        if (stream != null)
        {
          await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
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
    /// Processing of a message received from a client.
    /// </summary>
    /// <param name="IncomingMessage">Full ProtoBuf message to be processed.</param>
    /// <returns>true if the conversation with the client should continue, false if a protocol violation error occurred and the client should be disconnected.</returns>
    public async Task<bool> ProcessMessageAsync(Message IncomingMessage)
    {
      bool res = false;
      log.Debug("()");
      try
      {
        string msgStr = IncomingMessage.ToString();
        log.Trace("Received message type is {0}, message ID is {1}:\n{2}", IncomingMessage.MessageTypeCase, IncomingMessage.Id, msgStr.SubstrMax(512));
        switch (IncomingMessage.MessageTypeCase)
        {
          case Message.MessageTypeOneofCase.Request:
            {
              Message responseMessage = messageBuilder.CreateErrorProtocolViolationResponse(IncomingMessage);
              Request request = IncomingMessage.Request;

              log.Trace("Request type is {0}, version is {1}.", request.RequestTypeCase, ProtocolHelper.VersionBytesToString(request.Version.ToByteArray()));
              switch (request.RequestTypeCase)
              {
                case Request.RequestTypeOneofCase.LocalService:
                  break;

                default:
                  log.Warn("Invalid request type '{0}'.", request.RequestTypeCase);
                  break;
              }


              if (responseMessage != null)
              {
                // Send response to client.
                res = await SendMessageAsync(responseMessage);
              }
              else
              {
                // If there is no response to send immediately to the client,
                // we want to keep the connection open.
                res = true;
              }
              break;
            }

          case Message.MessageTypeOneofCase.Response:
            {
              Response response = IncomingMessage.Response;
              log.Trace("Response status is {0}, details are '{1}', response type is {0}.", response.Status, response.Details, response.ResponseTypeCase);

              switch (response.ResponseTypeCase)
              {
                default:
                  log.Error("Unknown response type '{0}'.", response.ResponseTypeCase);
                  // Connection will be closed in ReceiveMessageLoop.
                  break;
              }

              break;
            }

          default:
            log.Error("Unknown message type '{0}', connection to the client will be closed.", IncomingMessage.MessageTypeCase);
            await SendProtocolViolation();
            // Connection will be closed in ReceiveMessageLoop.
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred, connection to the client will be closed: {0}", e.ToString());
        await SendProtocolViolation();
        // Connection will be closed in ReceiveMessageLoop.
      }

      log.Debug("(-):{0}", res);
      return res;
    }
  }
}
