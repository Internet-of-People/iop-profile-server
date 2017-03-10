using Iop.Locnet;
using IopCommon;
using IopProtocol;
using IopServerCore.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServer.Network.LOC
{
  public class LocClient : ClientBase
  {
    /// <summary>LOC message builder for the TCP client.</summary>
    private LocMessageBuilder messageBuilder;
    /// <summary>LOC message builder for the TCP client.</summary>
    public LocMessageBuilder MessageBuilder { get { return messageBuilder; } }

    /// <summary>Message reader connected to an open network stream.</summary>
    RawMessageReader messageReader;


    /// <summary>
    /// Initialize the object.
    /// </summary>
    /// <param name="ServerEndPoint">LOC server address and port.</param>
    public LocClient(IPEndPoint ServerEndPoint) :
      base(ServerEndPoint, false)
    {
      log = new Logger("ProfileServer.ProfileServer.Network.LOC.LocClient");
      log.Trace("(ServerEndPoint:'{0}')", ServerEndPoint);

      messageBuilder = new LocMessageBuilder(0, new List<SemVer> { SemVer.V100 });

      log.Trace("(-)");
    }


    /// <summary>
    /// Constructs ProtoBuf message from raw data read from the network stream.
    /// </summary>
    /// <param name="Data">Raw data to be decoded to the message.</param>
    /// <returns>ProtoBuf message or null if the data do not represent a valid message.</returns>
    public override IProtocolMessage CreateMessageFromRawData(byte[] Data)
    {
      return (IProtocolMessage)LocMessageBuilder.CreateMessageFromRawData(Data);
    }

    /// <summary>
    /// Converts an IoP Network protocol message to a binary format.
    /// </summary>
    /// <param name="Message">IoP Network protocol message.</param>
    /// <returns>Binary representation of the message to be sent over the network.</returns>
    public override byte[] MessageToByteArray(IProtocolMessage Message)
    {
      return LocMessageBuilder.MessageToByteArray(Message);
    }


    /// <summary>
    /// Connects to a specific IP address and port and initializes stream.
    /// If TLS is used, client authentication is done as well.
    /// </summary>
    /// <returns>true if the connection was established succcessfully, false otherwise.</returns>
    public override async Task<bool> ConnectAsync()
    {
      log.Trace("()");

      bool res = await base.ConnectAsync();
      if (res)
      {
        messageReader = new RawMessageReader(Stream);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Reads and decodes message from the stream from LOC server.
    /// </summary>
    /// <param name="CancellationToken">Cancallation token for async calls.</param>
    /// <param name="CheckProtocolViolation">If set to true, the function checks whether a protocol violation occurred and if so, it sends protocol violation error to the peer.</param>
    /// <returns>Received message of null if the function fails.</returns>
    public async Task<LocProtocolMessage> ReceiveMessageAsync(CancellationToken CancellationToken, bool CheckProtocolViolation = false)
    {
      log.Trace("()");

      LocProtocolMessage res = null;

      RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(CancellationToken);
      if (rawMessage.Data != null)
      {
        res = (LocProtocolMessage)LocMessageBuilder.CreateMessageFromRawData(rawMessage.Data);
      }
      else log.Debug("Connection to LOC server has been terminated.");

      if (CheckProtocolViolation)
      {
        if ((res == null) || rawMessage.ProtocolViolation)
          await SendProtocolViolation();
      }

      log.Trace("(-):{0}", res != null ? "LocProtocolMessage" : "null");
      return res;
    }


    /// <summary>
    /// Sends ERROR_PROTOCOL_VIOLATION to client with message ID set to 0x0BADC0DE.
    /// </summary>
    /// <param name="Client">Client to send the error to.</param>
    public async Task SendProtocolViolation()
    {
      LocMessageBuilder mb = new LocMessageBuilder(0, new List<SemVer> { SemVer.V100 });
      LocProtocolMessage response = mb.CreateErrorProtocolViolationResponse(new LocProtocolMessage(new Message() { Id = 0x0BADC0DE }));

      await SendMessageAsync(response);
    }


    /// <summary>
    /// Sends a message to the client over the open network stream.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public override async Task<bool> SendMessageAsync(IProtocolMessage Message)
    {
      log.Trace("()");

      bool res = false;
      try
      {
        res = await base.SendMessageAsync(Message);
      }
      catch (Exception e)
      {
        if (e is ObjectDisposedException)
        {
          log.Info("Connection to the LOC server has been terminated.");
        }
        else
        {
          log.Error("Exception occurred (and rethrowing): {0}", e.ToString());
          await Task.Delay(5000);
          throw e;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


  }
}
