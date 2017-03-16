using System;
using ProfileServer.Kernel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using IopProtocol;
using Google.Protobuf;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using IopCrypto;
using Iop.Profileserver;
using System.Globalization;
using System.Text;
using IopCommon.Multiformats;
using System.Net.Http;
using System.Linq;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;

namespace ProfileServer.Network.CAN
{
  /// <summary>
  /// Location based network (LOC) is a part of IoP that the profile server relies on.
  /// When the profile server starts, this component connects to LOC and obtains information about the profile 
  /// server's neighborhood. Then it keeps receiving updates from LOC about changes in the neighborhood structure.
  /// The profile server needs to share its database of hosted identities with its neighbors and it also accepts 
  /// requests to share foreign profiles and consider them during its own search queries.
  /// </summary>
  public class ContentAddressNetwork : Component
  {
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Network.ContentAddressNetwork";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer." + ComponentName);

    /// <summary>Validity of profile server's IPNS record in seconds. </summary>
    private const int IpnsRecordExpirationTimeSeconds = 24 * 60 * 60;

    /// <summary>Time format of IPNS record.</summary>
    private const string Rfc3339DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffK";

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

    /// <summary>Last sequence number used for IPNS record.</summary>
    private UInt64 canIpnsLastSequenceNumber;



    /// <summary>
    /// Initializes the component.
    /// </summary>
    public ContentAddressNetwork():
      base(ComponentName)
    {
    }


    public override bool Init()
    {
      log.Info("()");

      bool res = false;

      try
      {
        canIpnsLastSequenceNumber = Config.Configuration.CanIpnsLastSequenceNumber;
        canApi = (CanApi)Base.ComponentDictionary[CanApi.ComponentName];

        // Construct profile server's contact information CAN object.
        canContactInformation = new CanProfileServerContact()
        {
          PublicKey = ProtocolHelper.ByteArrayToByteString(Config.Configuration.Keys.PublicKey),
          IpAddress = ProtocolHelper.ByteArrayToByteString(Config.Configuration.ExternalServerAddress.GetAddressBytes()),
          PrimaryPort = (uint)Config.Configuration.ServerRoles.GetRolePort((uint)ServerRole.Primary)
        };


        initThread = new Thread(new ThreadStart(InitThread));
        initThread.Start();

        RegisterCronJobs();

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

        if ((initThread != null) && !initThreadFinished.WaitOne(25000))
          log.Error("Init thread did not terminated in 25 seconds.");
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      if ((initThread != null) && !initThreadFinished.WaitOne(25000))
        log.Error("Init thread did not terminate in 25 seconds.");

      log.Info("(-)");
    }



    /// <summary>
    /// Registers component's cron jobs.
    /// </summary>
    public void RegisterCronJobs()
    {
      log.Trace("()");

      List<CronJob> cronJobDefinitions = new List<CronJob>()
      {
        // Refreshes profile server's IPNS record.
        { new CronJob() { Name = "ipnsRecordRefresh", StartDelay = 2 * 60 * 60 * 1000, Interval = 7 * 60 * 60 * 1000, HandlerAsync = CronJobHandlerIpnsRecordRefreshAsync  } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }



    /// <summary>
    /// Handler for "ipnsRecordRefresh" cron job.
    /// </summary>
    public async void CronJobHandlerIpnsRecordRefreshAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await IpnsRecordRefreshAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Refreshes porofile server's IPNS record.
    /// </summary>
    public async Task IpnsRecordRefreshAsync()
    {
      log.Trace("()");

      if (canContactInformationHash == null)
      {
        log.Debug("No CAN contact information hash, can't refresh IPNS record, will try later.");
        log.Trace("(-)");
        return;
      }

      canIpnsLastSequenceNumber++;
      canIpnsRecord = CreateIpnsRecord(canContactInformationHash, canIpnsLastSequenceNumber);
      CanRefreshIpnsResult cres = await canApi.RefreshIpnsRecord(canIpnsRecord, Config.Configuration.Keys.PublicKey);
      if (cres.Success)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          await unitOfWork.AcquireLockAsync(UnitOfWork.SettingsLock);

          try
          {
            Setting setting = new Setting("CanIpnsLastSequenceNumber", canIpnsLastSequenceNumber.ToString());
            await unitOfWork.SettingsRepository.AddOrUpdate(setting);
            await unitOfWork.SaveThrowAsync();
            log.Debug("CanIpnsLastSequenceNumber updated in database to new value {0}.", setting.Value);
          }
          catch (Exception e)
          {
            log.Error("Unable to update CanIpnsLastSequenceNumber in the database to new value {0}, exception: {1}", canIpnsLastSequenceNumber, e.ToString());
          }

          unitOfWork.ReleaseLock(UnitOfWork.SettingsLock);
        }
      }
      else if (cres.Message != "Shutdown") log.Error("Failed to refresh profile server's IPNS record.");

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread that is initializes CAN objects during the profile server startup.
    /// </summary>
    private async void InitThread()
    {
      log.Info("()");

      initThreadFinished.Reset();

      if (Config.Configuration.CanProfileServerContactInformationHash != null) log.Debug("Old CAN object hash is '{0}', object {1} change.", Config.Configuration.CanProfileServerContactInformationHash.ToBase58(), Config.Configuration.CanProfileServerContactInformationChanged ? "DID" : "did NOT");
      else log.Debug("No CAN object found.");

      bool deleteOldObject = Config.Configuration.CanProfileServerContactInformationChanged && (Config.Configuration.CanProfileServerContactInformationHash != null);
      byte[] canObject = canContactInformation.ToByteArray();
      log.Trace("CAN object: {0}", canObject.ToHex());

      while (!ShutdownSignaling.IsShutdown)
      {
        // First delete old CAN object if there is any.
        bool error = false;
        if (deleteOldObject)
        {
          string objectPath = CanApi.CreateIpfsPathFromHash(Config.Configuration.CanProfileServerContactInformationHash);
          CanDeleteResult cres = await canApi.CanDeleteObject(objectPath);
          if (cres.Success)
          {
            log.Info("Old CAN object hash '{0}' deleted.", Config.Configuration.CanProfileServerContactInformationHash.ToBase58());
          }
          else
          {
            log.Warn("Failed to delete old CAN object hash '{0}', error message '{1}', will retry.", Config.Configuration.CanProfileServerContactInformationHash.ToBase58(), cres.Message);
            error = true;
          }
        }
        else log.Trace("No old object to delete.");

        if (ShutdownSignaling.IsShutdown) break;
        if (!error)
        {
          if (Config.Configuration.CanProfileServerContactInformationChanged)
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
            canContactInformationHash = Config.Configuration.CanProfileServerContactInformationHash;
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
        if (Config.Configuration.CanProfileServerContactInformationChanged)
        {
          // Save the new data to the database.
          if (!await SaveProfileServerContactInformation())
            log.Error("Failed to save new profile server contact information values to database.");
        }

        // Finally, start IPNS record refreshing timer.
        await IpnsRecordRefreshAsync();
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
          string addr = Config.Configuration.ExternalServerAddress.ToString();
          string port = Config.Configuration.ServerRoles.GetRolePort((uint)ServerRole.Primary).ToString();
          string hash = canContactInformationHash.ToBase58();
          log.Debug("Saving contact information values to database: {0}:{1}, '{2}'", addr, port, hash);

          Setting primaryPort = new Setting("PrimaryPort", port);
          Setting externalServerAddress = new Setting("ExternalServerAddress", addr);
          Setting canProfileServerContactInformationHash = new Setting("CanProfileServerContactInformationHash", hash);

          await unitOfWork.SettingsRepository.AddOrUpdate(externalServerAddress);
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

      UInt64 ttlNanoSec = (UInt64)(TimeSpan.FromSeconds(IpnsRecordExpirationTimeSeconds).TotalMilliseconds) * (UInt64)1000000;

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

      byte[] res = Ed25519.Sign(dataToSign, Config.Configuration.Keys.ExpandedPrivateKey);
      
      return res;
    }
  }
}
