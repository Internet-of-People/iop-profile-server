using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServerProtocol
{
  /// <summary>
  /// On the lowest socket level, the receiving part of the client can either be reading the message prefix header or the body.
  /// </summary>
  public enum ReaderStatus
  {
    /// <summary>Peer is waiting for the message header to be read from the socket.</summary>
    ReadingHeader,

    /// <summary>Peer has read the message header and is now waiting for the message body to be read.</summary>
    ReadingBody
  }

  /// <summary>
  /// Result of reading message from the raw stream.
  /// </summary>
  public class RawMessageResult
  {
    /// <summary>Actual message being read if the reading process was successful.</summary>
    public byte[] Data = null;
    
    /// <summary>Indication of whether a protocol violation error has been detected and we should inform the other side about it.</summary>
    public bool ProtocolViolation = false;

    public override string ToString()
    {
      return string.Format("RawMessage.Length={0},ProtocolViolation={1}", Data != null ? Data.Length.ToString() : "n/a", ProtocolViolation);
    }
  }


  /// <summary>
  /// Implements the ability of reading a message from a raw stream in the format that is common 
  /// to IoP network protocols. As long as the given protocol is based on MessageWithHeader with 5 byte header,
  /// it is compatible with this reader.
  /// </summary>
  public class RawMessageReader
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServerProtocol.RawMessageReader");

    /// <summary>Network connection stream.</summary>
    private Stream rawStream;

    /// <summary>Buffer for message header.</summary>
    private byte[] messageHeaderBuffer = new byte[ProtocolHelper.HeaderSize];
    
    /// <summary>Buffer for whole message.</summary>
    private byte[] messageBuffer = null;

    /// <summary>Status of the message reader.</summary>
    private ReaderStatus readerStatus = ReaderStatus.ReadingHeader;

    /// <summary>Size of the message the reader expects to read.</summary>
    private uint messageSize = 0;

    /// <summary>Number of header bytes read from the stream.</summary>
    private int messageHeaderBytesRead = 0;

    /// <summary>Number of bytes read from the message body.</summary>
    private int messageBytesRead = 0;


    /// <summary>
    /// Initializes message reader using a network stream.
    /// </summary>
    /// <param name="RawStream">Stream of the connection to read from.</param>
    public RawMessageReader(Stream RawStream)
    {
      log.Trace("()");

      rawStream = RawStream;

      log.Trace("(-)");
    }


    /// <summary>
    /// Reads a message from the stream.
    /// </summary>
    public async Task<RawMessageResult> ReceiveMessageAsync(CancellationToken CancelToken)
    {
      log.Trace("()");

      bool disconnect = false;
      RawMessageResult res = new RawMessageResult();
      try
      {
        while ((res.Data == null) && !disconnect)
        {
          Task<int> readTask = null;
          int remain = 0;

          log.Trace("Reader status is '{0}'.", readerStatus);
          switch (readerStatus)
          {
            case ReaderStatus.ReadingHeader:
              {
                remain = ProtocolHelper.HeaderSize - messageHeaderBytesRead;
                readTask = rawStream.ReadAsync(messageHeaderBuffer, messageHeaderBytesRead, remain, CancelToken);
                break;
              }

            case ReaderStatus.ReadingBody:
              {
                remain = (int)messageSize - messageBytesRead;
                readTask = rawStream.ReadAsync(messageBuffer, ProtocolHelper.HeaderSize + messageBytesRead, remain, CancelToken);
                break;
              }

            default:
              log.Error("Invalid client status '{0}'.", readerStatus);
              break;
          }

          if (readTask != null)
          {
            log.Trace("{0} bytes remains to be read.", remain);

            int readAmount = await readTask;
            if (readAmount != 0)
            {
              log.Trace("Read completed: {0} bytes.", readAmount);

              switch (readerStatus)
              {
                case ReaderStatus.ReadingHeader:
                  {
                    messageHeaderBytesRead += readAmount;
                    if (readAmount == remain)
                    {
                      if (messageHeaderBuffer[0] == 0x0D)
                      {
                        uint hdr = ProtocolHelper.GetValueLittleEndian(messageHeaderBuffer, 1);
                        if (hdr + ProtocolHelper.HeaderSize <= ProtocolHelper.MaxMessageSize)
                        {
                          messageSize = hdr;
                          readerStatus = ReaderStatus.ReadingBody;
                          messageBuffer = new byte[ProtocolHelper.HeaderSize + messageSize];
                          Array.Copy(messageHeaderBuffer, messageBuffer, messageHeaderBuffer.Length);
                          log.Trace("Reading of message header completed. Message size is {0} bytes.", messageSize);
                        }
                        else
                        {
                          log.Warn("Client claimed message of size {0} which exceeds the maximum.", hdr + ProtocolHelper.HeaderSize);
                          res.ProtocolViolation = true;
                        }
                      }
                      else
                      {
                        log.Warn("Message has invalid format - it's first byte is 0x{0:X2}, should be 0x0D.", messageHeaderBuffer[0]);
                        res.ProtocolViolation = true;
                      }
                    }
                    break;
                  }

                case ReaderStatus.ReadingBody:
                  {
                    messageBytesRead += readAmount;
                    if (readAmount == remain)
                    {
                      readerStatus = ReaderStatus.ReadingHeader;
                      messageBytesRead = 0;
                      messageHeaderBytesRead = 0;
                      log.Trace("Reading of message size {0} completed.", messageSize);

                      res.Data = messageBuffer;
                    }
                    break;
                  }

                default:
                  log.Error("Invalid message reader status {0}.", readerStatus);
                  disconnect = true;
                  break;
              }

              if (res.ProtocolViolation)
                disconnect = true;
            }
            else
            {
              log.Debug("Connection has been closed.");
              disconnect = true;
            }
          }
          else disconnect = true;
        }
      }
      catch (Exception e)
      {
        if ((e is ObjectDisposedException) || (e is IOException)) log.Debug("Connection to client has been terminated.");
        else if (e is TaskCanceledException) log.Debug("Timeout or shutdown detected.");
        else log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
