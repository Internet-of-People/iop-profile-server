using ProfileServer.Data.Models;
using Iop.Profileserver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using IopProtocol;
using Microsoft.EntityFrameworkCore.Storage;
using IopCommon;
using IopServerCore.Data;
using System.Runtime.CompilerServices;
using IopServerCore.Kernel;

namespace ProfileServer.Data.Repositories
{
  /// <summary>
  /// Repository for locally hosted identities.
  /// </summary>
  public class HostedIdentityRepository : IdentityRepositoryBase<HostedIdentity>
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Repositories.HostedIdentityRepository");

    /// <summary>
    /// Creates instance of the repository.
    /// </summary>
    /// <param name="Context">Database context.</param>
    /// <param name="UnitOfWork">Instance of unit of work that owns the repository.</param>
    public HostedIdentityRepository(Context Context, UnitOfWork UnitOfWork)
      : base(Context, UnitOfWork)
    {
    }

    /// <summary>
    /// Obtains hosted identities type statistics.
    /// </summary>
    /// <returns>List of statistics of hosted profile types.</returns>
    public async Task<List<ProfileStatsItem>> GetProfileStatsAsync()
    {
      return await context.Identities.Where(i => (i.Initialized == true) && (i.Cancelled == false)).GroupBy(i => i.Type)
        .Select(g => new ProfileStatsItem { IdentityType = g.Key, Count = (uint)g.Count() }).ToListAsync();
    }

    /// <summary>
    /// Obtains identity profile by its ID.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the identity profile to get.</param>
    /// <returns>Identity profile or null if the function fails.</returns>
    public async Task<HostedIdentity> GetHostedIdentityByIdAsync(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());
      HostedIdentity res = null;

      try
      {
        res = (await GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):{0}", res != null ? "HostedIdentity" : "null");
      return res;
    }



    /// <summary>
    /// Obtains CanObjectHash from specific identity's profile.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the identity to get the data from.</param>
    /// <returns>CanObjectHash of the given identity or null if the function fails.</returns>
    public async Task<byte[]> GetCanObjectHashAsync(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());
      byte[] res = null;

      try
      {
        HostedIdentity identity = (await GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
        if (identity != null) res = identity.CanObjectHash;
        else log.Error("Identity ID '{0}' not found.", IdentityId.ToHex());
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-):'{0}'", res != null ? res.ToBase58() : "");
      return res;
    }


    /// <summary>
    /// Sets a new value to CanObjectHash of an identity's profile.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the identity to set the value to.</param>
    /// <param name="NewValue">Value to set identity's CanObjectHash to.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> SetCanObjectHashAsync(byte[] IdentityId, byte[] NewValue)
    {
      log.Trace("(IdentityId:'{0}',NewValue:'{1}')", IdentityId.ToHex(), NewValue != null ? NewValue.ToBase58() : "");

      bool res = false;

      DatabaseLock lockObject = UnitOfWork.HostedIdentityLock;
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObject))
      {
        try
        {
          HostedIdentity identity = (await GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
          if (identity != null)
          {
            identity.CanObjectHash = NewValue;
            Update(identity);
            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            res = true;
          }
          else log.Error("Identity ID '{0}' not found.", IdentityId.ToHex());
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!res)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }
      }

      unitOfWork.ReleaseLock(lockObject);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Updates identity profile and creates neighborhood action to propagate the change unless the client did not want the propagation.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the identity to update.</param>
    /// <param name="SignedProfile">Signed profile information with updated values.</param>
    /// <param name="ProfileImageChanged">True if profile image is about to change.</param>
    /// <param name="ThumbnailImageChanged">True if thumbnail image is about to change.</param>
    /// <param name="NoPropagation">True if the client does not want this change of profile to be propagated to the neighborhood.</param>
    /// <param name="IdentityNotFound">If the function fails because the identity is not found, this referenced value is set to true.</param>
    /// <param name="ImagesToDelete">If the function succeeds and the profile images are altered, old image files has to be deleted, in which case their hashes 
    /// are returned in this list, which has to be initialized by the caller.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> UpdateProfileAndPropagateAsync(byte[] IdentityId, SignedProfileInformation SignedProfile, bool ProfileImageChanged, bool ThumbnailImageChanged, bool NoPropagation, StrongBox<bool> IdentityNotFound, List<byte[]> ImagesToDelete)
    {
      log.Trace("()");
      bool res = false;

      bool signalNeighborhoodAction = false;
      bool success = false;

      List<byte[]> imagesToDelete = new List<byte[]>();

      ProfileInformation profile = SignedProfile.Profile;
      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.HostedIdentityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          HostedIdentity identity = (await GetAsync(i => (i.IdentityId == IdentityId) && (i.Cancelled == false))).FirstOrDefault();
          if (identity != null)
          {
            bool isProfileInitialization = !identity.Initialized;

            identity.Initialized = true;
            identity.Version = profile.Version.ToByteArray();
            identity.Name = profile.Name;

            GpsLocation location = new GpsLocation(profile.Latitude, profile.Longitude);
            identity.SetInitialLocation(location);
            identity.ExtraData = profile.ExtraData;
            identity.Signature = SignedProfile.Signature.ToByteArray();

            if (ProfileImageChanged)
            {
              if (identity.ProfileImage != null) imagesToDelete.Add(identity.ProfileImage);
              identity.ProfileImage = profile.ProfileImageHash.Length != 0 ? profile.ProfileImageHash.ToByteArray() : null;
            }

            if (ThumbnailImageChanged)
            {
              if (identity.ThumbnailImage != null) imagesToDelete.Add(identity.ThumbnailImage);
              identity.ThumbnailImage = profile.ThumbnailImageHash.Length != 0 ? profile.ThumbnailImageHash.ToByteArray() : null;
            }


            Update(identity);


            if (!NoPropagation)
            {
              // The profile change has to be propagated to all our followers
              // we create database actions that will be processed by dedicated thread.
              NeighborhoodActionType actionType = isProfileInitialization ? NeighborhoodActionType.AddProfile : NeighborhoodActionType.ChangeProfile;
              string extraInfo = identity.PublicKey.ToHex();
              signalNeighborhoodAction = await unitOfWork.NeighborhoodActionRepository.AddIdentityProfileFollowerActionsAsync(actionType, identity.IdentityId, extraInfo);
            }

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            success = true;
          }
          else IdentityNotFound.Value = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (!success)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObjects);
      }

      if (success)
      {
        // Only when the function succeeds the old images can be deleted.
        ImagesToDelete.AddRange(imagesToDelete);

        // Send signal to neighborhood action processor to process the new series of actions.
        if (signalNeighborhoodAction)
        {
          Network.NeighborhoodActionProcessor neighborhoodActionProcessor = (Network.NeighborhoodActionProcessor)Base.ComponentDictionary[Network.NeighborhoodActionProcessor.ComponentName];
          neighborhoodActionProcessor.Signal();
        }

        res = true;
      }


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Cancels the hosting of the profile in the database.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the hosted identity to cancel.</param>
    /// <param name="CancelHostingAgreementRequest">Cancellation request from the client/</param>
    /// <param name="NotFound">If the function fails because the identity does not exist, this value is set to true.</param>
    /// <param name="IdentityNotFound">If the function fails because the identity is not found, this referenced value is set to true.
    /// are returned in this list, which has to be initialized by the caller.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> CancelProfileAndPropagateAsync(byte[] IdentityId, CancelHostingAgreementRequest CancelHostingAgreementRequest, StrongBox<bool> IdentityNotFound, List<byte[]> ImagesToDelete)
    {
      log.Trace("()");
      bool res = false;

      bool signalNeighborhoodAction = false;
      bool success = false;
      bool redirected = false;

      List<byte[]> imagesToDelete = new List<byte[]>();

      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.HostedIdentityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          HostedIdentity identity = (await GetAsync(i => (i.IdentityId == IdentityId) && (i.Cancelled == false))).FirstOrDefault();
          if (identity != null)
          {
            // We are going to delete the images, so we have to make sure, the identity in database does not reference it anymore.
            if (identity.ProfileImage != null) imagesToDelete.Add(identity.ProfileImage);
            if (identity.ThumbnailImage != null) imagesToDelete.Add(identity.ThumbnailImage);

            identity.ProfileImage = null;
            identity.ThumbnailImage = null;
            identity.Cancelled = true;

            if (CancelHostingAgreementRequest.RedirectToNewProfileServer)
            {
              // The customer cancelled the contract, but left a redirect, which we will maintain for 14 days.
              identity.ExpirationDate = DateTime.UtcNow.AddDays(14);
              identity.HostingServerId = CancelHostingAgreementRequest.NewProfileServerNetworkId.ToByteArray();
              redirected = true;
            }
            else
            {
              // The customer cancelled the contract, no redirect is being maintained, we can delete the record at any time.
              identity.ExpirationDate = DateTime.UtcNow;
            }

            Update(identity);

            // The profile change has to be propagated to all our followers
            // we create database actions that will be processed by dedicated thread.
            signalNeighborhoodAction = await unitOfWork.NeighborhoodActionRepository.AddIdentityProfileFollowerActionsAsync(NeighborhoodActionType.RemoveProfile, identity.IdentityId);

            await unitOfWork.SaveThrowAsync();
            transaction.Commit();
            success = true;
          }
          else IdentityNotFound.Value = true;
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());

        }

        if (!success)
        {
          log.Warn("Rolling back transaction.");
          unitOfWork.SafeTransactionRollback(transaction);
        }

        unitOfWork.ReleaseLock(lockObjects);

        if (success)
        {
          if (redirected) log.Debug("Identity '{0}' hosting agreement cancelled and redirection set to profile server ID '{1}'.", IdentityId.ToHex(), CancelHostingAgreementRequest.NewProfileServerNetworkId.ToByteArray().ToHex());
          else log.Debug("Identity '{0}' hosting agreement cancelled and no redirection set.", IdentityId.ToHex());

          res = true;
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Finds and deletes expired identities.
    /// </summary>
    public async Task DeleteExpiredIdentitiesAsync()
    {
      log.Trace("()");

      DateTime now = DateTime.UtcNow;
      List<byte[]> imagesToDelete = new List<byte[]>();

      DatabaseLock lockObject = UnitOfWork.HostedIdentityLock;
      await unitOfWork.AcquireLockAsync(lockObject);
      try
      {
        List<HostedIdentity> expiredIdentities = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.ExpirationDate < now, null, true)).ToList();
        if (expiredIdentities.Count > 0)
        {
          log.Debug("There are {0} expired hosted identities.", expiredIdentities.Count);
          foreach (HostedIdentity identity in expiredIdentities)
          {
            if (identity.ProfileImage != null) imagesToDelete.Add(identity.ProfileImage);
            if (identity.ThumbnailImage != null) imagesToDelete.Add(identity.ThumbnailImage);

            unitOfWork.HostedIdentityRepository.Delete(identity);
            log.Debug("Identity ID '{0}' expired and will be deleted.", identity.IdentityId.ToHex());
          }

          await unitOfWork.SaveThrowAsync();
          log.Debug("{0} expired hosted identities were deleted.", expiredIdentities.Count);
        }
        else log.Debug("No expired hosted identities found.");
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      unitOfWork.ReleaseLock(lockObject);


      if (imagesToDelete.Count > 0)
      {
        ImageManager imageManager = (ImageManager)Base.ComponentDictionary[ImageManager.ComponentName];

        foreach (byte[] hash in imagesToDelete)
          imageManager.RemoveImageReference(hash);
      }


      log.Trace("(-)");
    }
  }
}
