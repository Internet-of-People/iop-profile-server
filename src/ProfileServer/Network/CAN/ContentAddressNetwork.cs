using System;
using ProfileServer.Kernel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Threading;
using ProfileServer.Utils;
using System.Threading.Tasks;
using System.IO;
using ProfileServerProtocol;
using Google.Protobuf;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using ProfileServerCrypto;
using Iop.Profileserver;
using System.Globalization;
using System.Text;
using ProfileServerProtocol.Multiformats;
using System.Net.Http;
using System.Linq;

namespace ProfileServer.Network.CAN
{
  /// <summary>
  /// Location based network (LBN) is a part of IoP that the profile server relies on.
  /// When the node starts, this component connects to LBN and obtains information about the node's neighborhood.
  /// Then it keep receiving updates from LBN about changes in the neighborhood structure.
  /// The profile server needs to share its database of hosted identities with its neighbors and it also accepts 
  /// requests to share foreign profiles and consider them during its own search queries.
  /// </summary>
  public class ContentAddressNetwork : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.ContentAddressNetwork");

    /// <summary>Validity of profile server's IPNS record in milliseconds. </summary>
    private const int IpnsRecordExpirationTimeSeconds = 24 * 60 * 60;

    /// <summary>IPNS record refresh frequency in milliseconds.</summary>
#warning put back
    //private const int IpnsRecordRefreshIntervalMs = 7 * 60 * 60 * 1000;
    private const int IpnsRecordRefreshIntervalMs = 17 * 1000;

    /// <summary>Time format of IPNS record.</summary>
    private const string Rfc3339DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffK";

    /// <summary>Timer that invokes IPNS record refreshment.</summary>
    private static Timer ipnsRecordRefreshTimer;


    /// <summary>Profile server's IPNS record.</summary>
    private CanIpnsEntry canIpnsRecord;



    /// <summary>Profile server's contact information object in CAN.</summary>
    private CanProfileServerContact canContactInformation;

    /// <summary>CAN hash of CanContactInformation object.</summary>
    private byte[] canContactInformationHash;


    /// <summary>Event that is set when initThread is not running.</summary>
    private ManualResetEvent initThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that initializes CAN objects during the profile server's startup.</summary>
    private Thread initThread;

    /// <summary>Access to CAN API.</summary>
    private CanApi canApi;


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        canApi = (CanApi)Base.ComponentDictionary["Network.ContentAddressNetwork.CanApi"];

        // Construct profile server's contact information CAN object.
        canContactInformation = new CanProfileServerContact()
        {
          PublicKey = ProtocolHelper.ByteArrayToByteString(Base.Configuration.Keys.PublicKey),
          IpAddress = ProtocolHelper.ByteArrayToByteString(Base.Configuration.ServerInterface.GetAddressBytes()),
          PrimaryPort = (uint)Base.Configuration.ServerRoles.GetRolePort(ServerRole.Primary)
        };


        initThread = new Thread(new ThreadStart(InitThread));
        initThread.Start();

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

        if ((initThread != null) && !initThreadFinished.WaitOne(10000))
          log.Error("Init thread did not terminated in 10 seconds.");

        if (ipnsRecordRefreshTimer != null) ipnsRecordRefreshTimer.Dispose();
        ipnsRecordRefreshTimer = null;
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      if ((initThread != null) && !initThreadFinished.WaitOne(10000))
        log.Error("Init thread did not terminated in 10 seconds.");

      if (ipnsRecordRefreshTimer != null) ipnsRecordRefreshTimer.Dispose();
      ipnsRecordRefreshTimer = null;

      log.Info("(-)");
    }


    /// <summary>
    /// Callback routine of ipnsRecordRefreshTimer. Simply invokes IPNS record refresh procedure.
    /// </summary>
    /// <param name="State">Not used.</param>
    private async void IpnsRecordRefreshTimerCallback(object State)
    {
      log.Trace("()");

      Base.Configuration.CanIpnsLastSequenceNumber++;
      canIpnsRecord = CreateIpnsRecord(canContactInformationHash, Base.Configuration.CanIpnsLastSequenceNumber);
      CanRefreshIpnsResult cres = await canApi.RefreshIpnsRecord(canIpnsRecord, Base.Configuration.Keys.PublicKey);
      if (cres.Success)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          await unitOfWork.AcquireLockAsync(UnitOfWork.SettingsLock);

          try
          {
            Setting setting = new Setting("CanIpnsLastSequenceNumber", Base.Configuration.CanIpnsLastSequenceNumber.ToString());
            await unitOfWork.SettingsRepository.AddOrUpdate(setting);
            await unitOfWork.SaveThrowAsync();
            log.Debug("CanIpnsLastSequenceNumber updated in database to new value {0}.", setting.Value);
          }
          catch (Exception e)
          {
            log.Error("Unable to update CanIpnsLastSequenceNumber in the database to new value {0}, exception: {1}", Base.Configuration.CanIpnsLastSequenceNumber, e.ToString());
          }

          unitOfWork.ReleaseLock(UnitOfWork.SettingsLock);
        }
      }
      else log.Error("Failed to refresh profile server's IPNS record.");

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread that is initializes CAN objects during the profile server startup.
    /// </summary>
    private async void InitThread()
    {
      log.Info("()");

      initThreadFinished.Reset();

      if (Base.Configuration.CanProfileServerContactInformationHash != null) log.Debug("Old CAN object hash is '{0}', object {1} change.", Base.Configuration.CanProfileServerContactInformationHash.ToBase58(), Base.Configuration.CanProfileServerContactInformationChanged ? "DID" : "did NOT");
      else log.Debug("No CAN object found.");

      bool deleteOldObject = Base.Configuration.CanProfileServerContactInformationChanged && (Base.Configuration.CanProfileServerContactInformationHash != null);
      byte[] canObject = canContactInformation.ToByteArray();
      log.Trace("CAN object: {0}", canObject.ToHex());

      while (!ShutdownSignaling.IsShutdown)
      {
        // First delete old CAN object if there is any.
        bool error = false;
        if (deleteOldObject)
        {
          string objectPath = CanApi.CreateIpfsPathFromHash(Base.Configuration.CanProfileServerContactInformationHash);
          CanDeleteResult cres = await canApi.CanDeleteObject(objectPath);
          if (cres.Success)
          {
            log.Info("Old CAN object hash '{0}' deleted.", Base.Configuration.CanProfileServerContactInformationHash.ToBase58());
          }
          else
          {
            log.Warn("Failed to delete old CAN object hash '{0}', error message '{1}', will retry.", Base.Configuration.CanProfileServerContactInformationHash.ToBase58(), cres.Message);
            error = true;
          }
        }
        else log.Trace("No old object to delete.");

        if (!error)
        {
          if (Base.Configuration.CanProfileServerContactInformationChanged)
          {
            // Now upload the new object.
            CanUploadResult cres = await canApi.CanUploadObject(canObject);
            if (cres.Success)
            {
              canContactInformationHash = cres.Hash;
              log.Info("New CAN object hash '{0}' added.", canContactInformationHash.ToBase58());
              break;
            }

            log.Warn("Unable to add new object to CAN, error message: '{0}'", cres.Message);
          }
          else
          {
            canContactInformationHash = Base.Configuration.CanProfileServerContactInformationHash;
            log.Info("CAN object unchanged since last time, hash is '{0}'.", canContactInformationHash.ToBase58());
            break;
          }
        }

        // Retry in 10 seconds.
        try
        {
          await Task.Delay(10000, ShutdownSignaling.ShutdownCancellationTokenSource.Token);
        }
        catch
        {
          // Catch cancellation exception.
        }
      }


      if (canContactInformationHash != null)
      {
        if (Base.Configuration.CanProfileServerContactInformationChanged)
        {
          // Save the new data to the database.
          if (!await SaveProfileServerContactInformation())
            log.Error("Failed to save new profile server contact information values to database.");
        }

        // Finally, start IPNS record refreshing timer.
        ipnsRecordRefreshTimer = new Timer(IpnsRecordRefreshTimerCallback, null, 10 * 1000, IpnsRecordRefreshIntervalMs);
      }


      initThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Saves values related to the profile server contact information to the database.
    /// </summary>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SaveProfileServerContactInformation()
    {
      log.Trace("()");

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.SettingsLock;
        await unitOfWork.AcquireLockAsync(lockObject);

        try
        {
          string addr = Base.Configuration.ServerInterface.ToString();
          string port = Base.Configuration.ServerRoles.GetRolePort(ServerRole.Primary).ToString();
          string hash = canContactInformationHash.ToBase58();
          log.Debug("Saving contact information values to database: {0}:{1}, '{2}'", addr, port, hash);

          Setting primaryPort = new Setting("PrimaryPort", port);
          Setting networkInterface = new Setting("NetworkInterface", addr);
          Setting canProfileServerContactInformationHash = new Setting("CanProfileServerContactInformationHash", hash);

          await unitOfWork.SettingsRepository.AddOrUpdate(networkInterface);
          await unitOfWork.SettingsRepository.AddOrUpdate(primaryPort);
          await unitOfWork.SettingsRepository.AddOrUpdate(canProfileServerContactInformationHash);

          await unitOfWork.SaveThrowAsync();
          res = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Creates CAN IPNS record object to point to a CAN object of the given hash with the given sequence number.
    /// </summary>
    /// <param name="Hash">Hash of the CAN object to point IPNS record to.</param>
    /// <param name="SequenceNumber">Sequence number of the IPNS record.</param>
    public CanIpnsEntry CreateIpnsRecord(byte[] Hash, UInt64 SequenceNumber)
    {
      log.Trace("(Hash:'{0}',SequenceNumber:{1})", Hash.ToBase58(), SequenceNumber);

      string validityString = DateTime.UtcNow.AddMonths(1).ToString(Rfc3339DateTimeFormat, DateTimeFormatInfo.InvariantInfo);
      byte[] validityBytes = Encoding.UTF8.GetBytes(validityString);

      UInt64 ttlNanoSec = (UInt64)(TimeSpan.FromMinutes(10).TotalMilliseconds) * (UInt64)1000000;

      string valueString = CanApi.CreateIpfsPathFromHash(Hash);
      byte[] valueBytes = Encoding.UTF8.GetBytes(valueString);

      CanIpnsEntry res = new CanIpnsEntry()
      {
        Sequence = SequenceNumber,
        ValidityType = CanIpnsEntry.Types.ValidityType.Eol,
        Ttl = ttlNanoSec,
        Validity = ProtocolHelper.ByteArrayToByteString(validityBytes),
        Value = ProtocolHelper.ByteArrayToByteString(valueBytes)
      };

      res.Signature = ProtocolHelper.ByteArrayToByteString(CreateIpnsRecordSignature(res));

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Calculates a signature of IPNS record.
    /// </summary>
    /// <param name="Record">IPNS record to calculate signature for.</param>
    /// <returns>Signature of the IPNS record.</returns>
    public byte[] CreateIpnsRecordSignature(CanIpnsEntry Record)
    {
      string validityTypeString = Record.ValidityType.ToString().ToUpperInvariant();
      byte[] validityTypeBytes = Encoding.UTF8.GetBytes(validityTypeString);
      byte[] dataToSign = new byte[Record.Value.Length + Record.Validity.Length + validityTypeBytes.Length];

      int offset = 0;
      Array.Copy(Record.Value.ToByteArray(), 0, dataToSign, offset, Record.Value.Length);
      offset += Record.Value.Length;

      Array.Copy(Record.Validity.ToByteArray(), 0, dataToSign, offset, Record.Validity.Length);
      offset += Record.Validity.Length;

      Array.Copy(validityTypeBytes, 0, dataToSign, offset, validityTypeBytes.Length);
      offset += validityTypeBytes.Length;

      byte[] res = Ed25519.Sign(dataToSign, Base.Configuration.Keys.ExpandedPrivateKey);
      
      return res;
    }
  }
}
