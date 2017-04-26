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
  public class HostedIdentityRepository : IdentityRepository<HostedIdentity>
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
      byte[] invalidVersion = SemVer.Invalid.ToByteArray();
      return await context.Identities.Where(i => (i.ExpirationDate == null) && (i.Version != invalidVersion)).GroupBy(i => i.Type)
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
    /// Updates identity profile using update profile request.
    /// </summary>
    /// <param name="IdentityId">Network identifier of the identity to update.</param>
    /// <param name="UpdateProfileRequest">Update profile request.</param>
    /// <param name="NewProfileImage">New profile image hash in case it is being updated.</param>
    /// <param name="NewThumbnailImage">New thumbnail image hash in case it is being updated.</param>
    /// <param name="IdentityNotFound">If the function fails because the identity is not found, this referenced value is set to true.</param>
    /// <param name="ImagesToDelete">If the profile images are altered, old image files has to be deleted, in which case their hashes are returned in this list, which has to be initialized by the caller.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    public async Task<bool> UpdateProfileAsync(byte[] IdentityId, UpdateProfileRequest UpdateProfileRequest, byte[] NewProfileImage, byte[] NewThumbnailImage, StrongBox<bool> IdentityNotFound, List<byte[]> ImagesToDelete)
    {
      log.Trace("()");
      bool res = false;

      bool signalNeighborhoodAction = false;
      bool success = false;

      DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.HostedIdentityLock, UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
      using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
      {
        try
        {
          HostedIdentity identity = (await GetAsync(i => (i.IdentityId == IdentityId) && (i.ExpirationDate == null))).FirstOrDefault();
          if (identity != null)
          {
            bool isProfileInitialization = !identity.IsProfileInitialized();

            if (UpdateProfileRequest.SetVersion)
              identity.Version = UpdateProfileRequest.Version.ToByteArray();

            if (UpdateProfileRequest.SetName)
              identity.Name = UpdateProfileRequest.Name;

            if (UpdateProfileRequest.SetImage)
            {
              // Here we replace existing images with new ones
              // and we save the old images hashes so we can delete them later.
              if (identity.ProfileImage != null) ImagesToDelete.Add(identity.ProfileImage);
              if (identity.ThumbnailImage != null) ImagesToDelete.Add(identity.ThumbnailImage);

              identity.ProfileImage = NewProfileImage;
              identity.ThumbnailImage = NewThumbnailImage;
            }

            if (UpdateProfileRequest.SetLocation)
            {
              GpsLocation gpsLocation = new GpsLocation(UpdateProfileRequest.Latitude, UpdateProfileRequest.Longitude);
              identity.SetInitialLocation(gpsLocation);
            }

            if (UpdateProfileRequest.SetExtraData)
              identity.ExtraData = UpdateProfileRequest.ExtraData;

            unitOfWork.HostedIdentityRepository.Update(identity);


            // The profile change has to be propagated to all our followers
            // we create database actions that will be processed by dedicated thread.
            NeighborhoodActionType actionType = isProfileInitialization ? NeighborhoodActionType.AddProfile : NeighborhoodActionType.ChangeProfile;
            string extraInfo = null;
            if (actionType == NeighborhoodActionType.ChangeProfile)
            {
              SharedProfileChangeItem changeItem = new SharedProfileChangeItem()
              {
                SetVersion = UpdateProfileRequest.SetVersion,
                SetName = UpdateProfileRequest.SetName,
                SetThumbnailImage = UpdateProfileRequest.SetImage,
                SetLocation = UpdateProfileRequest.SetLocation,
                SetExtraData = UpdateProfileRequest.SetExtraData
              };
              extraInfo = changeItem.ToString();
            }
            else
            {
              extraInfo = identity.PublicKey.ToHex();
            }
            signalNeighborhoodAction = await unitOfWork.NeighborhoodActionRepository.AddIdentityProfileFollowerActionsAsync(actionType, identity.IdentityId, extraInfo);

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
  }
}
