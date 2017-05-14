using Google.Protobuf;
using Iop.Profileserver;
using IopCommon;
using IopCrypto;
using IopProtocol;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProfileServer.Network
{
  /// <summary>
  /// Implements functions to validate user inputs.
  /// </summary>
  public static class InputValidators
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Network.InputValidators");


    /// <summary>
    /// Checks whether a profile information is valid.
    /// </summary>
    /// <param name="Profile">Profile information to check.</param>
    /// <param name="IdentityPublicKey">Public key of the profile's identity.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorPrefix">Prefix to add to the validation error details.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the signed profile information is valid, false otherwise.</returns>
    public static bool ValidateProfileInformation(ProfileInformation Profile, byte[] IdentityPublicKey, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, string ErrorPrefix, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;


      SemVer version = new SemVer(Profile.Version);
      // Currently only supported version is 1.0.0.
      if (!version.Equals(SemVer.V100))
      {
        log.Debug("Unsupported version '{0}'.", version);
        details = "version";
      }
      

      if (details == null)
      {
        byte[] pubKey = Profile.PublicKey.ToByteArray();
        bool pubKeyValid = (0 < pubKey.Length) && (pubKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes) && ByteArrayComparer.Equals(IdentityPublicKey, pubKey);
        if (!pubKeyValid)
        {
          log.Debug("Invalid public key '{0}' does not match identity public key '{1}'.", pubKey.ToHex(), IdentityPublicKey.ToHex());
          details = "publicKey";
        }
      }

      if (details == null)
      {
        int typeSize = Encoding.UTF8.GetByteCount(Profile.Type);
        bool typeValid = (0 < typeSize) && (typeSize <= IdentityBase.MaxProfileTypeLengthBytes);
        if (!typeValid)
        {
          log.Debug("Invalid type size in bytes {0}.", typeSize);
          details = "type";
        }
      }

      if (details == null)
      {
        int nameSize = Encoding.UTF8.GetByteCount(Profile.Name);
        bool nameValid = (0 < nameSize) && (nameSize <= IdentityBase.MaxProfileNameLengthBytes);
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "name";
        }
      }

      if (details == null)
      {
        GpsLocation locLat = new GpsLocation(Profile.Latitude, 0);
        if (!locLat.IsValid())
        {
          log.Debug("Invalid latitude {0}.", Profile.Latitude);
          details = "latitude";
        }
      }

      if (details == null)
      {
        GpsLocation locLon = new GpsLocation(0, Profile.Longitude);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", Profile.Longitude);
          details = "longitude";
        }
      }

      if (details == null)
      {
        int extraDataSize = Encoding.UTF8.GetByteCount(Profile.ExtraData);
        bool extraDataValid = extraDataSize <= IdentityBase.MaxProfileExtraDataLengthBytes;
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData size in bytes {0}.", extraDataSize);
          details = "extraData";
        }
      }

      if (details == null)
      {
        bool profileImageHashValid = (Profile.ProfileImageHash.Length == 0) || (Profile.ProfileImageHash.Length == ProtocolHelper.HashLengthBytes);
        if (!profileImageHashValid)
        {
          log.Debug("Invalid profile image hash size {0} bytes.", Profile.ProfileImageHash.Length);
          details = "profileImageHash";
        }
      }

      if (details == null)
      {
        bool thumbnailImageHashValid = (Profile.ThumbnailImageHash.Length == 0) || (Profile.ThumbnailImageHash.Length == ProtocolHelper.HashLengthBytes);
        if (!thumbnailImageHashValid)
        {
          log.Debug("Invalid thumbnail image hash size {0} bytes.", Profile.ThumbnailImageHash.Length);
          details = "thumbnailImageHash";
        }
      }


      if (details == null) res = true;
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, ErrorPrefix + details);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether a signed profile information is valid.
    /// </summary>
    /// <param name="SignedProfile">Signed profile information to chec.</param>
    /// <param name="IdentityPublicKey">Public key of the profile's identity.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorPrefix">Prefix to add to the validation error details.</param>
    /// <param name="InvalidSignatureToDetails">If set to true, invalid signature error will be reported as invalid value in signature field.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the signed profile information is valid and signed correctly by the given identity, false otherwise.</returns>
    public static bool ValidateSignedProfileInformation(SignedProfileInformation SignedProfile, byte[] IdentityPublicKey, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, string ErrorPrefix, bool InvalidSignatureToDetails, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("()");
      ErrorResponse = null;

      bool res = false;
      if (ValidateProfileInformation(SignedProfile.Profile, IdentityPublicKey, MessageBuilder, RequestMessage, ErrorPrefix + "profile.", out ErrorResponse))
      {
        // IdentityBase.InternalInvalidProfileType is a special internal type of profile that we use to prevent problems in the network.
        // This is not an elegant solution.
        // See NeighborhoodActionProcessor.NeighborhoodProfileUpdateAsync case NeighborhoodActionType.AddProfile for more information.
        if (SignedProfile.Profile.Type != IdentityBase.InternalInvalidProfileType)
        {
          byte[] signature = SignedProfile.Signature.ToByteArray();
          byte[] data = SignedProfile.Profile.ToByteArray();

          if (Ed25519.Verify(signature, data, IdentityPublicKey)) res = true;
          else ErrorResponse = InvalidSignatureToDetails ? MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, ErrorPrefix + "signature") : MessageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Checks whether the update profile request is valid.
    /// </summary>
    /// <param name="Identity">Identity on which the update operation is about to be performed.</param>
    /// <param name="UpdateProfileRequest">Update profile request part of the client's request message.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the profile update request can be applied, false otherwise.</returns>
    public static bool ValidateUpdateProfileRequest(HostedIdentity Identity, UpdateProfileRequest UpdateProfileRequest, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("(Identity.IdentityId:'{0}')", Identity.IdentityId.ToHex());

      bool res = false;
      ErrorResponse = null;

      SignedProfileInformation signedProfile = new SignedProfileInformation()
      {
        Profile = UpdateProfileRequest.Profile,
        Signature = RequestMessage.Request.ConversationRequest.Signature
      };

      if (ValidateSignedProfileInformation(signedProfile, Identity.PublicKey, MessageBuilder, RequestMessage, "", false, out ErrorResponse))
      {
        string details = null;

        // Check if the update is a valid profile initialization.
        // If the profile is updated for the first time (aka is being initialized),
        // NoPropagation must be false.
        if (!Identity.IsProfileInitialized() && UpdateProfileRequest.NoPropagation)
        {
          log.Debug("Attempt to initialize profile with NoPropagation set to false.");
          details = "noPropagation";
        }

        if (details == null)
        {
          // Profile type is unchangable after registration, unless it is special internal type.
          bool identityTypeValid = (Identity.Type == UpdateProfileRequest.Profile.Type) || (Identity.Type == IdentityBase.InternalInvalidProfileType);
          if (!identityTypeValid)
          {
            log.Debug("Attempt to change profile type.");
            details = "profile.type";
          }
        }

        if (details == null)
        {
          byte[] profileImage = UpdateProfileRequest.ProfileImage.ToByteArray();
          byte[] profileImageHash = UpdateProfileRequest.Profile.ProfileImageHash.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxProfileImageLengthBytes.
          bool eraseImage = profileImageHash.Length == 0;
          bool profileImageValid = (profileImage.Length <= HostedIdentity.MaxProfileImageLengthBytes) && (eraseImage || ImageManager.ValidateImageWithHash(profileImage, profileImageHash));
          if (!profileImageValid)
          {
            log.Debug("Invalid profile image.");
            details = "profileImage";
          }
        }

        if (details == null)
        {
          byte[] thumbnailImage = UpdateProfileRequest.ThumbnailImage.ToByteArray();
          byte[] thumbnailImageHash = UpdateProfileRequest.Profile.ThumbnailImageHash.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxThumbnailImageLengthBytes.
          bool eraseImage = thumbnailImageHash.Length == 0;
          bool thumbnailImageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && (eraseImage || ImageManager.ValidateImageWithHash(thumbnailImage, thumbnailImageHash));
          if (!thumbnailImageValid)
          {
            log.Debug("Invalid thumbnail image.");
            details = "thumbnailImage";
          }
        }


        if (details == null) res = true;
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether the profile search request is valid.
    /// </summary>
    /// <param name="ProfileSearchRequest">Profile search request part of the client's request message.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the profile search request is valid, false otherwise.</returns>
    public static bool ValidateProfileSearchRequest(ProfileSearchRequest ProfileSearchRequest, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      bool includeImages = ProfileSearchRequest.IncludeThumbnailImages;
      int responseResultLimit = includeImages ? PsMessageProcessor.ProfileSearchMaxResponseRecordsWithImage : PsMessageProcessor.ProfileSearchMaxResponseRecordsWithoutImage;
      int totalResultLimit = includeImages ? PsMessageProcessor.ProfileSearchMaxTotalRecordsWithImage : PsMessageProcessor.ProfileSearchMaxTotalRecordsWithoutImage;

      bool maxResponseRecordCountValid = (1 <= ProfileSearchRequest.MaxResponseRecordCount)
        && (ProfileSearchRequest.MaxResponseRecordCount <= responseResultLimit)
        && (ProfileSearchRequest.MaxResponseRecordCount <= ProfileSearchRequest.MaxTotalRecordCount);
      if (!maxResponseRecordCountValid)
      {
        log.Debug("Invalid maxResponseRecordCount value '{0}'.", ProfileSearchRequest.MaxResponseRecordCount);
        details = "maxResponseRecordCount";
      }

      if (details == null)
      {
        bool maxTotalRecordCountValid = (1 <= ProfileSearchRequest.MaxTotalRecordCount) && (ProfileSearchRequest.MaxTotalRecordCount <= totalResultLimit);
        if (!maxTotalRecordCountValid)
        {
          log.Debug("Invalid maxTotalRecordCount value '{0}'.", ProfileSearchRequest.MaxTotalRecordCount);
          details = "maxTotalRecordCount";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Type != null))
      {
        bool typeValid = Encoding.UTF8.GetByteCount(ProfileSearchRequest.Type) <= PsMessageBuilder.MaxProfileSearchTypeLengthBytes;
        if (!typeValid)
        {
          log.Debug("Invalid type value length '{0}'.", ProfileSearchRequest.Type.Length);
          details = "type";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Name != null))
      {
        bool nameValid = Encoding.UTF8.GetByteCount(ProfileSearchRequest.Name) <= PsMessageBuilder.MaxProfileSearchNameLengthBytes;
        if (!nameValid)
        {
          log.Debug("Invalid name value length '{0}'.", ProfileSearchRequest.Name.Length);
          details = "name";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Latitude != GpsLocation.NoLocationLocationType))
      {
        GpsLocation locLat = new GpsLocation(ProfileSearchRequest.Latitude, 0);
        GpsLocation locLong = new GpsLocation(0, ProfileSearchRequest.Longitude);
        if (!locLat.IsValid())
        {
          log.Debug("Latitude '{0}' is not a valid GPS latitude value.", ProfileSearchRequest.Latitude);
          details = "latitude";
        }
        else if (!locLong.IsValid())
        {
          log.Debug("Longitude '{0}' is not a valid GPS longitude value.", ProfileSearchRequest.Longitude);
          details = "longitude";
        }
      }

      if ((details == null) && (ProfileSearchRequest.Latitude != GpsLocation.NoLocationLocationType))
      {
        bool radiusValid = ProfileSearchRequest.Radius > 0;
        if (!radiusValid)
        {
          log.Debug("Invalid radius value '{0}'.", ProfileSearchRequest.Radius);
          details = "radius";
        }
      }

      if ((details == null) && (ProfileSearchRequest.ExtraData != null))
      {
        bool validLength = (Encoding.UTF8.GetByteCount(ProfileSearchRequest.ExtraData) <= PsMessageBuilder.MaxProfileSearchExtraDataLengthBytes);
        bool extraDataValid = RegexTypeValidator.ValidateRegex(ProfileSearchRequest.ExtraData);
        if (!validLength || !extraDataValid)
        {
          log.Debug("Invalid extraData regular expression filter.");
          details = "extraData";
        }
      }

      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether AddRelatedIdentityRequest request is valid.
    /// </summary>
    /// <param name="Client">Client that sent the request.</param>
    /// <param name="AddRelatedIdentityRequest">Client's request message to validate.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the profile update request can be applied, false otherwise.</returns>
    public static bool ValidateAddRelatedIdentityRequest(IncomingClient Client, AddRelatedIdentityRequest AddRelatedIdentityRequest, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("()");

      bool res = false;
      ErrorResponse = null;
      string details = null;

      CardApplicationInformation cardApplication = AddRelatedIdentityRequest.CardApplication;
      SignedRelationshipCard signedCard = AddRelatedIdentityRequest.SignedCard;
      RelationshipCard card = signedCard.Card;

      byte[] applicationId = cardApplication.ApplicationId.ToByteArray();
      byte[] cardId = card.CardId.ToByteArray();

      if ((applicationId.Length == 0) || (applicationId.Length > RelatedIdentity.CardIdentifierLength))
      {
        log.Debug("Card application ID is invalid.");
        details = "cardApplication.applicationId";
      }

      if (details == null)
      {
        byte[] appCardId = cardApplication.CardId.ToByteArray();
        if (!ByteArrayComparer.Equals(cardId, appCardId))
        {
          log.Debug("Card IDs in application card and relationship card do not match.");
          details = "cardApplication.cardId";
        }
      }

      if (details == null)
      {
        if (card.ValidFrom > card.ValidTo)
        {
          log.Debug("Card validFrom field is greater than validTo field.");
          details = "signedCard.card.validFrom";
        }
        else
        {
          DateTime? cardValidFrom = ProtocolHelper.UnixTimestampMsToDateTime(card.ValidFrom);
          DateTime? cardValidTo = ProtocolHelper.UnixTimestampMsToDateTime(card.ValidTo);
          if (cardValidFrom == null)
          {
            log.Debug("Card validFrom value '{0}' is not a valid timestamp.", card.ValidFrom);
            details = "signedCard.card.validFrom";
          }
          else if (cardValidTo == null)
          {
            log.Debug("Card validTo value '{0}' is not a valid timestamp.", card.ValidTo);
            details = "signedCard.card.validTo";
          }
        }
      }

      if (details == null)
      {
        byte[] issuerPublicKey = card.IssuerPublicKey.ToByteArray();
        bool pubKeyValid = (0 < issuerPublicKey.Length) && (issuerPublicKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes);
        if (!pubKeyValid)
        {
          log.Debug("Issuer public key has invalid length {0} bytes.", issuerPublicKey.Length);
          details = "signedCard.card.issuerPublicKey";
        }
      }

      if (details == null)
      {
        byte[] recipientPublicKey = card.RecipientPublicKey.ToByteArray();
        if (!ByteArrayComparer.Equals(recipientPublicKey, Client.PublicKey))
        {
          log.Debug("Caller is not recipient of the card.");
          details = "signedCard.card.recipientPublicKey";
        }
      }

      if (details == null)
      {
        if (!Client.MessageBuilder.VerifySignedConversationRequestBodyPart(RequestMessage, cardApplication.ToByteArray(), Client.PublicKey))
        {
          log.Debug("Caller is not recipient of the card.");
          ErrorResponse = Client.MessageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
          details = "";
        }
      }

      if (details == null)
      {
        SemVer cardVersion = new SemVer(card.Version);
        if (!cardVersion.Equals(SemVer.V100))
        {
          log.Debug("Card version is invalid or not supported.");
          details = "signedCard.card.version";
        }
      }

      if (details == null)
      {
        if (Encoding.UTF8.GetByteCount(card.Type) > PsMessageBuilder.MaxRelationshipCardTypeLengthBytes)
        {
          log.Debug("Card type is too long.");
          details = "signedCard.card.type";
        }
      }

      if (details == null)
      {
        RelationshipCard emptyIdCard = new RelationshipCard()
        {
          CardId = ProtocolHelper.ByteArrayToByteString(new byte[RelatedIdentity.CardIdentifierLength]),
          Version = card.Version,
          IssuerPublicKey = card.IssuerPublicKey,
          RecipientPublicKey = card.RecipientPublicKey,
          Type = card.Type,
          ValidFrom = card.ValidFrom,
          ValidTo = card.ValidTo
        };

        byte[] hash = Crypto.Sha256(emptyIdCard.ToByteArray());
        if (!ByteArrayComparer.Equals(hash, cardId))
        {
          log.Debug("Card ID '{0}' does not match its hash '{1}'.", cardId.ToHex(64), hash.ToHex());
          details = "signedCard.card.cardId";
        }
      }

      if (details == null)
      {
        byte[] issuerSignature = signedCard.IssuerSignature.ToByteArray();
        byte[] issuerPublicKey = card.IssuerPublicKey.ToByteArray();
        if (!Ed25519.Verify(issuerSignature, cardId, issuerPublicKey))
        {
          log.Debug("Issuer signature is invalid.");
          details = "signedCard.issuerSignature";
        }
      }

      if (details == null)
      {
        res = true;
      }
      else
      {
        if (ErrorResponse == null)
          ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedProfileUpdateItem update item.
    /// </summary>
    /// <param name="UpdateItem">Update item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="SharedProfilesCount">Number of profiles the neighbor already shares with the profile server.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedProfileUpdateItem(SharedProfileUpdateItem UpdateItem, int Index, int SharedProfilesCount, HashSet<byte[]> UsedProfileIdsInBatch, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0},SharedProfilesCount:{1})", Index, SharedProfilesCount);

      bool res = false;
      ErrorResponse = null;

      switch (UpdateItem.ActionTypeCase)
      {
        case SharedProfileUpdateItem.ActionTypeOneofCase.Add:
          res = ValidateSharedProfileAddItem(UpdateItem.Add, Index, SharedProfilesCount, UsedProfileIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedProfileUpdateItem.ActionTypeOneofCase.Change:
          res = ValidateSharedProfileChangeItem(UpdateItem.Change, Index, UsedProfileIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedProfileUpdateItem.ActionTypeOneofCase.Delete:
          res = ValidateSharedProfileDeleteItem(UpdateItem.Delete, Index, UsedProfileIdsInBatch, MessageBuilder, RequestMessage, out ErrorResponse);
          break;

        case SharedProfileUpdateItem.ActionTypeOneofCase.Refresh:
          res = true;
          break;

        default:
          ErrorResponse = MessageBuilder.CreateErrorProtocolViolationResponse(RequestMessage);
          res = false;
          break;
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedProfileAddItem update item.
    /// </summary>
    /// <param name="AddItem">Add item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="SharedProfilesCount">Number of profiles the neighbor already shares with the profile server.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedProfileAddItem(SharedProfileAddItem AddItem, int Index, int SharedProfilesCount, HashSet<byte[]> UsedProfileIdsInBatch, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0},SharedProfilesCount:{1})", Index, SharedProfilesCount);

      bool res = false;
      ErrorResponse = null;

      byte[] identityPubKey = AddItem.SignedProfile.Profile.PublicKey.ToByteArray();
      if (ValidateSignedProfileInformation(AddItem.SignedProfile, identityPubKey, MessageBuilder, RequestMessage, Index.ToString() + ".add.signedProfile.", true, out ErrorResponse))
      {
        string details = null;

        if (SharedProfilesCount >= IdentityBase.MaxHostedIdentities)
        {
          log.Debug("Target server already sent too many profiles.");
          details = "add";
        }

        if (details == null)
        {
          // We do not verify identity duplicity here, that is being done in PsMessageProcessor.ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
          byte[] identityId = Crypto.Sha256(identityPubKey);
          if (!UsedProfileIdsInBatch.Contains(identityId))
          {
            UsedProfileIdsInBatch.Add(identityId);
          }
          else
          {
            log.Debug("ID '{0}' (public key '{1}') already processed in this request.", identityId.ToHex(), identityPubKey.ToHex());
            details = "add.signedProfile.profile.publicKey";
          }
        }

        if (details == null)
        {
          byte[] thumbnailImage = AddItem.ThumbnailImage.ToByteArray();
          byte[] thumbnailImageHash = AddItem.SignedProfile.Profile.ThumbnailImageHash.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxThumbnailImageLengthBytes.
          bool noImage = thumbnailImageHash.Length == 0;
          bool thumbnailImageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && (noImage || ImageManager.ValidateImageWithHash(thumbnailImage, thumbnailImageHash));
          if (!thumbnailImageValid)
          {
            log.Debug("Invalid thumbnail image.");
            details = "add.thumbnailImage";
          }
        }


        if (details == null) res = true; 
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedProfileChangeItem update item.
    /// </summary>
    /// <param name="ChangeItem">Change item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedProfileChangeItem(SharedProfileChangeItem ChangeItem, int Index, HashSet<byte[]> UsedProfileIdsInBatch, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      byte[] identityPubKey = ChangeItem.SignedProfile.Profile.PublicKey.ToByteArray();
      if (ValidateSignedProfileInformation(ChangeItem.SignedProfile, identityPubKey, MessageBuilder, RequestMessage, Index.ToString() + ".change.signedProfile.", true, out ErrorResponse))
      {
        string details = null;

        // We do not verify identity duplicity here, that is being done in PsMessageProcessor.ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
        byte[] identityId = Crypto.Sha256(identityPubKey);
        if (!UsedProfileIdsInBatch.Contains(identityId))
        {
          UsedProfileIdsInBatch.Add(identityId);
        }
        else
        {
          log.Debug("ID '{0}' (public key '{1}') already processed in this request.", identityId.ToHex(), identityPubKey.ToHex());
          details = "change.signedProfile.profile.publicKey";
        }

        if (details == null)
        {
          byte[] thumbnailImage = ChangeItem.ThumbnailImage.ToByteArray();
          byte[] thumbnailImageHash = ChangeItem.SignedProfile.Profile.ThumbnailImageHash.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxThumbnailImageLengthBytes.
          bool noImage = thumbnailImageHash.Length == 0;
          bool thumbnailImageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && (noImage || ImageManager.ValidateImageWithHash(thumbnailImage, thumbnailImageHash));
          if (!thumbnailImageValid)
          {
            log.Debug("Invalid thumbnail image.");
            details = "change.thumbnailImage";
          }
        }


        if (details == null) res = true;
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// Validates incoming SharedProfileDeleteItem update item.
    /// </summary>
    /// <param name="DeleteItem">Delete item to validate.</param>
    /// <param name="Index">Item index in the update message.</param>
    /// <param name="UsedProfileIdsInBatch">List of profile network IDs of already validated items of this batch.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateSharedProfileDeleteItem(SharedProfileDeleteItem DeleteItem, int Index, HashSet<byte[]> UsedProfileIdsInBatch, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      string details = null;

      byte[] identityId = DeleteItem.IdentityNetworkId.ToByteArray();
      // We do not verify identity existence here, that is being done in ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
      bool identityIdValid = identityId.Length == ProtocolHelper.NetworkIdentifierLength;
      if (identityIdValid)
      {
        if (!UsedProfileIdsInBatch.Contains(identityId))
        {
          UsedProfileIdsInBatch.Add(identityId);
        }
        else
        {
          log.Debug("ID '{0}' already processed in this request.", identityId.ToHex());
          details = "delete.identityNetworkId";
        }
      }
      else
      {
        log.Debug("Invalid identity ID length {0}.", identityId.Length);
        details = "delete.identityNetworkId";
      }


      if (details == null) res = true;
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedProfileAddItem update received as a part of neighborhood initialization process.
    /// </summary>
    /// <param name="AddItem">Update item to validate.</param>
    /// <param name="Index">Index of the update item in the message.</param>
    /// <param name="IdentityDatabase">In-memory temporary database of identities hosted on the neighbor server that were already received and processed.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    public static bool ValidateInMemorySharedProfileAddItem(SharedProfileAddItem AddItem, int Index, Dictionary<byte[], NeighborIdentity> IdentityDatabase, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);

      bool res = false;
      ErrorResponse = null;

      byte[] identityPubKey = AddItem.SignedProfile.Profile.PublicKey.ToByteArray();
      if (ValidateSignedProfileInformation(AddItem.SignedProfile, identityPubKey, MessageBuilder, RequestMessage, Index.ToString() + ".add.signedProfile.", true, out ErrorResponse))
      {
        string details = null;
        if (IdentityDatabase.Count >= IdentityBase.MaxHostedIdentities)
        {
          log.Debug("Target server already sent too many profiles.");
          details = "add";
        }

        if (details == null)
        {
          // We do not verify identity duplicity here, that is being done in PsMessageProcessor.ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
          byte[] identityId = Crypto.Sha256(identityPubKey);
          if (IdentityDatabase.ContainsKey(identityId))
          {
            log.Debug("Identity with public key '{0}' (ID '{1}') already exists.", identityPubKey.ToHex(), identityId.ToHex());
            details = "add.signedProfile.profile.publicKey";
          }
        }

        if (details == null)
        {
          byte[] thumbnailImage = AddItem.ThumbnailImage.ToByteArray();
          byte[] thumbnailImageHash = AddItem.SignedProfile.Profile.ThumbnailImageHash.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxThumbnailImageLengthBytes.
          bool noImage = thumbnailImageHash.Length == 0;
          bool thumbnailImageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && (noImage || ImageManager.ValidateImageWithHash(thumbnailImage, thumbnailImageHash));
          if (!thumbnailImageValid)
          {
            log.Debug("Invalid thumbnail image.");
            details = "add.thumbnailImage";
          }
        }


        if (details == null) res = true;
        else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Checks whether the contract received from user with hosting registration request is valid.
    /// <para>This function does not verify that the hosting plan exists.</para>
    /// </summary>
    /// <param name="IdentityPublicKey">Public key of the identity that wants to register hosting.</param>
    /// <param name="Contract">Description of the contract.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message from client.</param>
    /// <param name="ErrorResponse">If the function fails, this is filled with error response message that is ready to be sent to the client.</param>
    /// <returns>true if the profile update request can be applied, false otherwise.</returns>
    public static bool ValidateRegisterHostingRequest(byte[] IdentityPublicKey, HostingPlanContract Contract, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
    {
      log.Trace("(IdentityPublicKey:'{0}')", IdentityPublicKey.ToHex());

      bool res = false;
      ErrorResponse = null;

      string details = null;

      if (!MessageBuilder.VerifySignedConversationRequestBodyPart(RequestMessage, Contract.ToByteArray(), IdentityPublicKey))
      {
        log.Debug("Contract signature is invalid.");
        ErrorResponse = MessageBuilder.CreateErrorInvalidSignatureResponse(RequestMessage);
        details = "";
      }


      if (details == null)
      {
        byte[] contractPubKey = Contract.IdentityPublicKey.ToByteArray();
        bool publicKeyValid = ByteArrayComparer.Equals(contractPubKey, IdentityPublicKey);
        if (!publicKeyValid)
        {
          log.Debug("Contract public key '{0}' does not match client's public key '{1}'.", contractPubKey.ToHex(), IdentityPublicKey.ToHex());
          details = "contract.identityPublicKey";
        }
      }

      if (details == null)
      {
        DateTime? startTime = ProtocolHelper.UnixTimestampMsToDateTime(Contract.StartTime);
        bool startTimeValid = (startTime != null) && ((startTime.Value - DateTime.UtcNow).TotalMinutes >= -60);

        if (!startTimeValid)
        {
          if (startTime == null) log.Debug("Invalid contract start time timestamp {0}.", Contract.StartTime);
          else log.Debug("Contract start time {0} is more than 1 hour in the past.", startTime.Value.ToString("yyyy-MM-dd HH:mm:ss"));
          details = "contract.startTime";
        }
      }

      if (details == null)
      {
        int typeSize = Encoding.UTF8.GetByteCount(Contract.IdentityType);
        bool typeValid = (0 < typeSize) && (typeSize <= IdentityBase.MaxProfileTypeLengthBytes);
        if (!typeValid)
        {
          log.Debug("Invalid contract identity type size in bytes {0}.", typeSize);
          details = "contract.identityType";
        }
      }

      if (details == null)
      {
        res = true;
      }
      else
      {
        if (ErrorResponse == null)
          ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
      }

      log.Trace("(-):{0}", res);
      return res;
    }
  }
}
