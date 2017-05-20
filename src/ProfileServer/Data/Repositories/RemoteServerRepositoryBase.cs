using ProfileServer.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;
using System.Net;
using System.Runtime.CompilerServices;
using Iop.Profileserver;
using ProfileServer.Network;
using IopServerCore.Kernel;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Generic repository for remote servers, which is the base for NeighborReposity for neighbor servers
  /// and FollowerRepository for follower servers of this profile server.
  /// </summary>
  public abstract class RemoteServerRepositoryBase<T> : GenericRepository<T> where T : RemoteServerBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Repositories.RemoteServerRepositoryBase");

    /// <summary>Obtains the primary database lock of this repository table.</summary>
    /// <returns>Primary database lock of this repository table.</returns>
    public abstract DatabaseLock GetTableLock();

    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public RemoteServerRepositoryBase(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }



    /// <summary>
    /// Sets srNeighborPort of a server to null.
    /// </summary>
    /// <param name="NetworkId">Identifier of the remote server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> ResetSrNeighborPortAsync(byte[] NetworkId)
    {
      log.Trace("(NetworkId:'{0}')", NetworkId.ToHex());

      bool res = false;
      bool dbSuccess = false;
      DatabaseLock lockObject = GetTableLock();
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObject))
      {
        try
        {
          T remoteServer = (await GetAsync(f => f.NetworkId == NetworkId)).FirstOrDefault();
          if (remoteServer != null)
          {
            remoteServer.SrNeighborPort = null;
            Update(remoteServer);

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            res = true;
          }
          else log.Error("Unable to find {0} ID '{1}'.", remoteServer is Neighbor ? "neighbor" : "follower", NetworkId.ToHex());

          dbSuccess = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!dbSuccess)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Updates LastRefreshTime of a remote server.
    /// </summary>
    /// <param name="NetworkId">Identifier of the remote server to update.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> UpdateLastRefreshTimeAsync(byte[] NetworkId)
    {
      log.Trace("(NetworkId:'{0}')", NetworkId.ToHex());

      bool res = false;

      DatabaseLock lockObject = GetTableLock();
      await unitOfWork.AcquireLockAsync(lockObject);
      try
      {
        T remoteServer = (await GetAsync(n => n.NetworkId == NetworkId)).FirstOrDefault();
        if (remoteServer != null)
        {
          remoteServer.LastRefreshTime = DateTime.UtcNow;
          Update(remoteServer);
          await unitOfWork.SaveThrowAsync();
          res = true;
        }
        else
        {
          // Between the check couple of lines above and here, the requesting server stop being our neighbor/follower
          // we can ignore it now and proceed as this does no harm.
          log.Error("Remote server ID '{0}' is no longer our {1}.", NetworkId.ToHex(), remoteServer is Neighbor ? "neighbor" : "follower");
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred while trying to update LastRefreshTime of server ID '{0}': {1}", NetworkId.ToHex(), e.ToString());
      }

      unitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Obtains IP address and srNeighbor port from the server's network identifier.
    /// </summary>
    /// <param name="NetworkId">Network identifer of the server.</param>
    /// <param name="NotFound">If the function fails, this is set to true if the reason for failure was that the remote server was not found.</param>
    /// <param name="IgnoreDbPortValue">If set to true, the function will ignore SrNeighborPort value of the server even if it is set in the database 
    /// and will contact the server on its primary port and then update SrNeighborPort in the database, if it successfully gets its value.</param>
    /// <returns>End point description or null if the function fails.</returns>
    public async Task<IPEndPoint> GetServerContactAsync(byte[] NetworkId, StrongBox<bool> NotFound, bool IgnoreDbPortValue = false)
    {
      log.Trace("(NetworkId:'{0}',IgnoreDbPortValue:{1})", NetworkId.ToHex(), IgnoreDbPortValue);

      IPEndPoint res = null;
      DatabaseLock lockObject = null;
      bool unlock = false;
      try
      {
        T remoteServer = (await GetAsync(n => n.NetworkId == NetworkId)).FirstOrDefault();
        if (remoteServer != null)
        {
          log.Trace("{0} server found in the database.", remoteServer is Neighbor ? "Neighbor" : "Follower");
          IPAddress addr = new IPAddress(remoteServer.IpAddress);
          if (!IgnoreDbPortValue && (remoteServer.SrNeighborPort != null))
          {
            res = new IPEndPoint(addr, remoteServer.SrNeighborPort.Value);
          }
          else
          {
            NeighborhoodActionProcessor neighborhoodActionProcessor = (NeighborhoodActionProcessor)Base.ComponentDictionary[NeighborhoodActionProcessor.ComponentName];

            // We do not know srNeighbor port of this server yet (or we ignore it), we have to connect to its primary port and get that information.
            int srNeighborPort = await neighborhoodActionProcessor.GetServerRolePortFromPrimaryPort(addr, remoteServer.PrimaryPort, ServerRoleType.SrNeighbor);
            if (srNeighborPort != 0)
            {
              lockObject = GetTableLock();
              await unitOfWork.AcquireLockAsync(lockObject);
              unlock = true;

              remoteServer.SrNeighborPort = srNeighborPort;
              Update(remoteServer);
              if (!await unitOfWork.SaveAsync())
                log.Error("Unable to save new srNeighbor port information {0} of {1} ID '{2}' to the database.", srNeighborPort, remoteServer is Neighbor ? "neighbor" : "follower", NetworkId.ToHex());

              res = new IPEndPoint(addr, srNeighborPort);
            }
            else log.Debug("Unable to obtain srNeighbor port from primary port of {0} ID '{1}'.", remoteServer is Neighbor ? "neighbor" : "follower", NetworkId.ToHex());
          }
        }
        else
        {
          log.Error("Unable to find {0} ID '{1}' in the database.", remoteServer is Neighbor ? "neighbor" : "follower", NetworkId.ToHex());
          NotFound.Value = true;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (unlock) unitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res != null ? res.ToString() : "null");
      return res;
    }
  }
}
