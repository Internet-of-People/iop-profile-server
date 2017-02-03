using Google.Protobuf;
using Iop.Profileserver;
using Newtonsoft.Json;
using ProfileServer.Kernel;
using ProfileServer.Utils;
using ProfileServerCrypto;
using ProfileServerProtocol;
using ProfileServerProtocol.Multiformats;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ProfileServer.Network.CAN
{
  /// <summary>
  /// Integration of API provided by CAN server.
  /// </summary>
  public class CanApi : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.ContentAddressNetwork.CanApi");

    /// <summary>URL of the CAN API gateway.</summary>
    private static string apiUrl;


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        apiUrl = string.Format("http://{0}:{1}/api/v0/", Base.Configuration.CanEndPoint.Address, Base.Configuration.CanEndPoint.Port);

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
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      log.Info("(-)");
    }


    /// <summary>
    /// Sends HTTP POST request to CAN server.
    /// </summary>
    /// <param name="Action">Specifies the API function to call.</param>
    /// <param name="Params">List of parameters and their values.</param>
    /// <param name="FileToUploadParamName">Name of the file parameter, or null if no file is being uploaded.</param>
    /// <param name="FileToUploadName">Name of the file being uploaded, or null if no file is being uploaded.</param>
    /// <param name="FileToUploadData">Binary data of the file being uploaded, or null if no file is being uploaded.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    private async Task<CanApiResult> SendRequest(string Action, NameValueCollection Params, string FileToUploadParamName = null, string FileToUploadName = null, byte[] FileToUploadData = null)
    {
      log.Trace("(Action:'{0}',FileToUploadParamName:'{1}')", Action, FileToUploadParamName);

      CanApiResult res = new CanApiResult();

      string query = "";
      foreach (string key in Params)
        query += string.Format("{0}{1}={2}", query.Length > 0 ? "&" : "", WebUtility.HtmlEncode(key), WebUtility.HtmlEncode(Params[key]));

      string url = string.Format("{0}{1}{2}{3}", apiUrl, Action, query.Length > 0 ? "?" : "", query);
      log.Debug("CAN API URL is '{0}'.", url);

      try
      {
        using (HttpClient client = new HttpClient())
        {
          client.Timeout = TimeSpan.FromSeconds(8);

          byte[] boundaryBytes = new byte[16];
          Crypto.Rng.GetBytes(boundaryBytes);
          string boundary = string.Format("------------------------{0}", boundaryBytes.ToHex().ToLowerInvariant());

          using (MultipartFormDataContent content = new MultipartFormDataContent(boundary))
          {
            if (FileToUploadParamName != null)
            {
              ByteArrayContent fileContent = new ByteArrayContent(FileToUploadData);
              fileContent.Headers.Add("Content-Type", "application/octet-stream");
              fileContent.Headers.Add("Content-Disposition", string.Format("form-data; name=\"{0}\"; filename = \"{1}\"", FileToUploadParamName, FileToUploadName));
              content.Add(fileContent, FileToUploadParamName, FileToUploadName);
            }

            using (HttpResponseMessage message = await client.PostAsync(url, content, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
            {
              res.Success = message.IsSuccessStatusCode;
              byte[] data = await message.Content.ReadAsByteArrayAsync();
              string dataStr = null;
              try
              {
                dataStr = Encoding.UTF8.GetString(data);
              }
              catch
              {
              }

              if (res.Success)
              {
                res.Data = data;
                res.DataStr = dataStr;
              }
              else
              {
                try
                {
                  dataStr = Encoding.UTF8.GetString(data);
                  CanErrorResponse cer = JsonConvert.DeserializeObject<CanErrorResponse>(dataStr);
                  res.Message = cer.Message;
                }
                catch
                {
                  res.Message = dataStr != null ? dataStr : "Invalid response.";
                }
                res.IsCanError = true;
              }
            }
          }
        }
      }
      catch (Exception e)
      {
        if (e is OperationCanceledException)
        {
          log.Debug("Shutdown detected.");
          res.IsCanError = false;
          res.Message = "Shutdown";
        }
        else log.Warn("Exception occurred: {0}", e.Message);
      }

      if (res.Success) log.Trace("(-):*.Success={0},*.Data:\n{1}", res.Success, res.DataStr != null ? res.DataStr.SubstrMax() : "n/a");
      else log.Trace("(-):*.Success={0},*.IsCanError={1},*.Message:\n{2}", res.Success, res.IsCanError, res.Message != null ? res.Message.SubstrMax(512) : "");
      return res;
    }



    /// <summary>
    /// Uploads CAN object to CAN server.
    /// </summary>
    /// <param name="ObjectData">CAN object to upload.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    public async Task<CanUploadResult> CanUploadObject(byte[] ObjectData)
    {
      log.Trace("(ObjectData.Length:{0})", ObjectData.Length);

      CanApiResult apiResult = await SendRequest("add", new NameValueCollection(), "file", "object", ObjectData);
      CanUploadResult res = CanUploadResult.FromApiResult(apiResult);

      if (res.Success) log.Trace("(-):*.Success={0},*.Hash='{1}'", res.Success, res.Hash.ToBase58());
      else log.Trace("(-):*.Success={0},*.Message='{1}'", res.Success, res.Message);
      return res;
    }


    /// <summary>
    /// Deletes CAN object from CAN server.
    /// </summary>
    /// <param name="ObjectPath">CAN path to the object.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    public async Task<CanDeleteResult> CanDeleteObject(string ObjectPath)
    {
      log.Trace("(ObjectPath:'{0}')", ObjectPath);

      NameValueCollection args = new NameValueCollection();
      args.Add("arg", ObjectPath);
      CanApiResult apiResult = await SendRequest("pin/rm", args);
      CanDeleteResult res = CanDeleteResult.FromApiResult(apiResult);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Refreshes profile server's IPNS record in CAN.
    /// </summary>
    /// <param name="IpnsRecord">IPNS record to refresh.</param>
    /// <param name="PublicKey">Public key of the IPNS record owner.</param>
    /// <returns>Structure describing whether the function succeeded and response provided by CAN server.</returns>
    public async Task<CanRefreshIpnsResult> RefreshIpnsRecord(CanIpnsEntry IpnsRecord, byte[] PublicKey)
    {
      log.Trace("(PublicKey:'{0}')", PublicKey.ToHex());

      string ipnsRecordEncoded = IpnsRecord.ToByteArray().ToBase64UrlPad(true);

      CanCryptoKey cryptoKey = new CanCryptoKey()
      {
        Type = CanCryptoKey.Types.KeyType.Ed25519,
        Data = ProtocolHelper.ByteArrayToByteString(PublicKey)
      };
      string keyEncoded = Base58Encoding.Encoder.Encode(cryptoKey.ToByteArray());
      log.Debug("Encoding public key: {0}", keyEncoded);

      NameValueCollection args = new NameValueCollection();
      args.Add("arg", ipnsRecordEncoded);
      args.Add("key", keyEncoded);

      CanApiResult apiResult = await SendRequest("name/upload", args);
      CanRefreshIpnsResult res = CanRefreshIpnsResult.FromApiResult(apiResult);

      if (res.Success)
      {
        res.IsCanError = false;

        // Check that the ID, path and sequence number match what we expect.
        string canId = CanApi.PublicKeyToId(PublicKey).ToBase58();
        if (res.Details.Peer == canId)
        {
          string path = Encoding.UTF8.GetString(IpnsRecord.Value.ToByteArray());          
          if (res.Details.NewPath == path)
          {
            if (res.Details.NewSeq == IpnsRecord.Sequence)
            {
              // All OK.
            }
            else
            {
              log.Warn("CAN sequence is {0}, received {1}.", IpnsRecord.Sequence, res.Details.NewSeq);
              res.Success = false;
              res.Message = "CAN path in response does not match expected value.";
            }
          }
          else
          {
            log.Warn("CAN path is '{0}', received '{1}'.", path, res.Details.NewPath);
            res.Success = false;
            res.Message = "CAN path in response does not match expected value.";
          }
        }
        else
        {
          log.Warn("CAN ID is '{0}', received '{1}'.", canId, res.Details.Peer);
          res.Success = false;
          res.Message = "CAN ID in response does not match expected value.";
        }
      }

      if (res.Success) log.Trace("(-):*.Success={0}", res.Success);
      else log.Trace("(-):*.Success={0},*.IsCanError={1},*.Message='{2}'", res.Success, res.IsCanError, res.Message);
      return res;
    }


    /// <summary>
    /// Creates IPFS path to the object of a given hash.
    /// </summary>
    /// <param name="Hash">Hash of the object.</param>
    /// <returns>IPFS path to the object of the given hash.</returns>
    public static string CreateIpfsPathFromHash(byte[] Hash)
    {
      return "/ipfs/" + Base58Encoding.Encoder.EncodeRaw(Hash);
    }


    /// <summary>
    /// Converts public key to CAN ID format.
    /// </summary>
    /// <param name="PublicKey">Ed25519 public key.</param>
    /// <returns>CAN ID that corresponds to the the public.</returns>
    public static byte[] PublicKeyToId(byte[] PublicKey)
    {
      CanCryptoKey key = new CanCryptoKey()
      {
        Type = CanCryptoKey.Types.KeyType.Ed25519,
        Data = ProtocolHelper.ByteArrayToByteString(PublicKey)
      };

      byte[] hash = Crypto.Sha256(key.ToByteArray());

      byte[] res = new byte[2 + hash.Length];
      res[0] = 0x12; // SHA256 hash prefix
      res[1] = (byte)hash.Length;
      Array.Copy(hash, 0, res, 2, hash.Length);
      return res;
    }
  }


  /// <summary>
  /// Result of CanUploadObject function.
  /// </summary>
  public class CanUploadResult : CanApiResult
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.ContentAddressNetwork.CanUploadResult");

    /// <summary>
    /// Structure of the JSON response of CAN '/api/v0/add' call.
    /// </summary>
    public class CanUploadObjectResponse
    {
      /// <summary>Name of the uploaded object.</summary>
      public string Name;

      /// <summary>CAN hash of the uploaded object</summary>
      public string Hash;
    }

    /// <summary>If Success is true, hash of the uploaded object.</summary>
    public byte[] Hash;

    /// <summary>
    /// Creates upload result from generic API result.
    /// </summary>
    /// <param name="ApiResult">Existing instance to copy.</param>
    public CanUploadResult(CanApiResult ApiResult) :
      base(ApiResult)
    {
    }

    /// <summary>
    /// Creates a new object based on a result from CAN API including validation checks.
    /// </summary>
    /// <param name="ApiResult">CAN API result object to copy values from.</param>
    /// <returns>Structure describing result of CAN upload operation.</returns>
    public static CanUploadResult FromApiResult(CanApiResult ApiResult)
    {
      log.Trace("()");

      CanUploadResult res = new CanUploadResult(ApiResult);
      if (res.Success)
      {
        bool error = false;
        try
        {
          CanUploadObjectResponse response = JsonConvert.DeserializeObject<CanUploadObjectResponse>(res.DataStr);
          if (!string.IsNullOrEmpty(response.Hash))
          {
            res.Hash = Base58Encoding.Encoder.DecodeRaw(response.Hash);
            if (res.Hash == null)
            {
              log.Error("Unable to decode hash '{0}'.", response.Hash);
              error = true;
            }
          }
          else
          {
            log.Error("Empty hash in CAN response.");
            error = true;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
          error = true;
        }

        if (error)
        {
          res.Success = false;
          res.Message = "Invalid CAN response.";
          res.IsCanError = false;
        }
      }

      log.Trace("(-)");
      return res;
    }
  }


  /// <summary>
  /// Result of CanDeleteObject function.
  /// </summary>
  public class CanDeleteResult : CanApiResult
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.ContentAddressNetwork.CanDeleteResult");

    /// <summary>
    /// Structure of the JSON response of CAN '/api/v0/pin/rm' call.
    /// </summary>
    public class CanDeleteObjectResponse
    {
      /// <summary>List of removed pins.</summary>
      public string[] Pins;
    }


    /// <summary>
    /// Creates delete result from generic API result.
    /// </summary>
    /// <param name="ApiResult">Existing instance to copy.</param>
    public CanDeleteResult(CanApiResult ApiResult) :
      base(ApiResult)
    {
    }

    /// <summary>List of removed pins.</summary>
    public string[] Pins;

    /// <summary>
    /// Creates a new object based on a result from CAN API including validation checks.
    /// </summary>
    /// <param name="ApiResult">CAN API result object to copy values from.</param>
    /// <returns>Structure describing result of CAN upload operation.</returns>
    public static CanDeleteResult FromApiResult(CanApiResult ApiResult)
    {
      log.Trace("()");

      CanDeleteResult res = new CanDeleteResult(ApiResult);
      if (res.Success)
      {
        bool error = false;
        try
        {
          CanDeleteObjectResponse response = JsonConvert.DeserializeObject<CanDeleteObjectResponse>(res.DataStr);
          res.Pins = response.Pins;

          // If the object was deleted previously, we might have empty Pins in response.
          // We are thus OK if we receive success response and no more validation is done.
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
          error = true;
        }

        if (error)
        {
          res.Success = false;
          res.Message = "Invalid CAN response.";
          res.IsCanError = false;
        }
      }
      else if (res.Message.ToLowerInvariant() == "not pinned")
      {
        res.Success = true;
        res.Pins = null;
      }

      log.Trace("(-)");
      return res;
    }
  }


  /// <summary>
  /// Result of CanDeleteObject function.
  /// </summary>
  public class CanRefreshIpnsResult : CanApiResult
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.ContentAddressNetwork.CanRefreshIpnsResult");

    /// <summary>
    /// Structure of the JSON response of CAN '/api/v0/name/upload' call.
    /// </summary>
    public class CanRefreshIpnsResponse
    {
      /// <summary>CAN ID of the IPNS record owner.</summary>
      public string Peer;

      /// <summary>Old sequence number.</summary>
      public ulong OldSeq;

      /// <summary>New sequence number.</summary>
      public ulong NewSeq;

      /// <summary>Path to which the new IPNS record resolves.</summary>
      public string NewPath;
    }

    /// <summary>Details returned by the CAN server.</summary>
    public CanRefreshIpnsResponse Details;

    /// <summary>
    /// Creates delete result from generic API result.
    /// </summary>
    /// <param name="ApiResult">Existing instance to copy.</param>
    public CanRefreshIpnsResult(CanApiResult ApiResult) :
      base(ApiResult)
    {
    }
    /// <summary>
    /// Creates a new object based on a result from CAN API including validation checks.
    /// </summary>
    /// <param name="ApiResult">CAN API result object to copy values from.</param>
    /// <returns>Structure describing result of CAN IPNS refresh operation.</returns>
    public static CanRefreshIpnsResult FromApiResult(CanApiResult ApiResult)
    {
      log.Trace("()");

      CanRefreshIpnsResult res = new CanRefreshIpnsResult(ApiResult);
      if (res.Success)
      {
        bool error = false;
        try
        {
          CanRefreshIpnsResponse response = JsonConvert.DeserializeObject<CanRefreshIpnsResponse>(res.DataStr);
          res.Details = response;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
          error = true;
        }

        if (error)
        {
          res.Success = false;
          res.Message = "Invalid CAN response.";
          res.IsCanError = false;
        }
      }

      log.Trace("(-)");
      return res;
    }
  }



  /// <summary>
  /// Result of CAN API call.
  /// </summary>
  public class CanApiResult
  {
    /// <summary>true if the function succeeds, false otherwise.</summary>
    public bool Success;

    /// <summary>If Success is true, this contains response data.</summary>
    public byte[] Data;

    /// <summary>String representation of Data, or null if Data does not hold a string.</summary>
    public string DataStr;

    /// <summary>
    /// If Success is false and IsCanError is true, this is an error message from CAN server.
    /// If Success is false and IsCanError is false, this is an error message from our code.
    /// </summary>
    public string Message;

    /// <summary>If Success is false, this is true if the error was reported by CAN server, and this is false if the error comes from our code.</summary>
    public bool IsCanError;

    /// <summary>
    /// Creates a default instance of the object.
    /// </summary>
    public CanApiResult()
    {
      Success = false;
      Data = null;
      DataStr = null;
      Message = "Internal error.";
      IsCanError = false;
    }

    /// <summary>
    /// Creates an instance of the object as a copy of another existing instance.
    /// </summary>
    /// <param name="ApiResult">Existing instance to copy.</param>
    public CanApiResult(CanApiResult ApiResult)
    {
      Success = ApiResult.Success;
      Data = ApiResult.Data;
      DataStr = ApiResult.DataStr;
      Message = ApiResult.Message;
      IsCanError = ApiResult.IsCanError;
    }
  }

  /// <summary>
  /// Structure of the CAN error JSON response.
  /// </summary>
  public class CanErrorResponse
  {
    /// <summary>Error message.</summary>
    public string Message;

    /// <summary>Error code.</summary>
    public int Code;
  }
}
