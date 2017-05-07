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

      // Check if the update is a valid profile initialization.
      // If the profile is updated for the first time (aka is being initialized),
      // SetVersion, SetName and SetLocation must be true.
      if (!Identity.IsProfileInitialized())
      {
        log.Debug("Profile initialization detected.");

        if (!UpdateProfileRequest.SetVersion || !UpdateProfileRequest.SetName || !UpdateProfileRequest.SetLocation)
        {
          string details = null;
          if (!UpdateProfileRequest.SetVersion) details = "setVersion";
          else if (!UpdateProfileRequest.SetName) details = "setName";
          else if (!UpdateProfileRequest.SetLocation) details = "setLocation";

          log.Debug("Attempt to initialize profile without '{0}' being set.", details);
          ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, details);
        }
      }
      else
      {
        // Nothing to update?
        if (!UpdateProfileRequest.SetVersion
          && !UpdateProfileRequest.SetName
          && !UpdateProfileRequest.SetImage
          && !UpdateProfileRequest.SetLocation
          && !UpdateProfileRequest.SetExtraData)
        {
          log.Debug("Update request updates nothing.");
          ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, "set*");
        }
      }

      if (ErrorResponse == null)
      {
        string details = null;

        // Now check if the values we received are valid.
        if (UpdateProfileRequest.SetVersion)
        {
          SemVer version = new SemVer(UpdateProfileRequest.Version);

          // Currently only supported version is 1.0.0.
          if (!version.Equals(SemVer.V100))
          {
            log.Debug("Unsupported version '{0}'.", version);
            details = "version";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetName)
        {
          string name = UpdateProfileRequest.Name;

          // Name is non-empty string, max Identity.MaxProfileNameLengthBytes bytes long.
          if (string.IsNullOrEmpty(name) || (Encoding.UTF8.GetByteCount(name) > IdentityBase.MaxProfileNameLengthBytes))
          {
            log.Debug("Invalid name '{0}'.", name);
            details = "name";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetImage)
        {
          byte[] image = UpdateProfileRequest.Image.ToByteArray();

          // Profile image must be PNG or JPEG image, no bigger than Identity.MaxProfileImageLengthBytes.
          bool eraseImage = image.Length == 0;
          bool imageValid = (image.Length <= HostedIdentity.MaxProfileImageLengthBytes) && (eraseImage || ImageManager.ValidateImageFormat(image));
          if (!imageValid)
          {
            log.Debug("Invalid image.");
            details = "image";
          }
        }


        if ((details == null) && UpdateProfileRequest.SetLocation)
        {
          GpsLocation locLat = new GpsLocation(UpdateProfileRequest.Latitude, 0);
          GpsLocation locLong = new GpsLocation(0, UpdateProfileRequest.Longitude);
          if (!locLat.IsValid())
          {
            log.Debug("Latitude '{0}' is not a valid GPS latitude value.", UpdateProfileRequest.Latitude);
            details = "latitude";
          }
          else if (!locLong.IsValid())
          {
            log.Debug("Longitude '{0}' is not a valid GPS longitude value.", UpdateProfileRequest.Longitude);
            details = "longitude";
          }
        }

        if ((details == null) && UpdateProfileRequest.SetExtraData)
        {
          string extraData = UpdateProfileRequest.ExtraData;
          if (extraData == null) extraData = "";

          // Extra data is semicolon separated 'key=value' list, max IdentityBase.MaxProfileExtraDataLengthBytes bytes long.
          int byteLen = Encoding.UTF8.GetByteCount(extraData);
          if (byteLen > IdentityBase.MaxProfileExtraDataLengthBytes)
          {
            log.Debug("Extra data too large ({0} bytes, limit is {1}).", byteLen, IdentityBase.MaxProfileExtraDataLengthBytes);
            details = "extraData";
          }
        }

        if (details == null)
        {
          res = true;
        }
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
        if (!StructuralEqualityComparer<byte[]>.Default.Equals(cardId, appCardId))
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
        if (!StructuralEqualityComparer<byte[]>.Default.Equals(recipientPublicKey, Client.PublicKey))
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
        if (!StructuralEqualityComparer<byte[]>.Default.Equals(hash, cardId))
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

      string details = null;

      if (SharedProfilesCount >= IdentityBase.MaxHostedIdentities)
      {
        log.Debug("Target server already sent too many profiles.");
        details = "add";
      }

      if (details == null)
      {
        SemVer version = new SemVer(AddItem.Version);
        // Currently only supported version is 1.0.0.
        if (!version.Equals(SemVer.V100))
        {
          log.Debug("Unsupported version '{0}'.", version);
          details = "add.version";
        }
      }

      if (details == null)
      {
        // We do not verify identity duplicity here, that is being done in ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync.
        byte[] pubKey = AddItem.IdentityPublicKey.ToByteArray();
        bool pubKeyValid = (0 < pubKey.Length) && (pubKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes);
        if (pubKeyValid)
        {
          byte[] identityId = Crypto.Sha256(pubKey);
          if (!UsedProfileIdsInBatch.Contains(identityId))
          {
            UsedProfileIdsInBatch.Add(identityId);
          }
          else
          {
            log.Debug("ID '{0}' (public key '{1}') already processed in this request.", identityId.ToHex(), pubKey.ToHex());
            details = "add.identityPublicKey";
          }
        }
        else
        {
          log.Debug("Invalid public key length '{0}'.", pubKey.Length);
          details = "add.identityPublicKey";
        }
      }

      if (details == null)
      {
        int nameSize = Encoding.UTF8.GetByteCount(AddItem.Name);
        bool nameValid = !string.IsNullOrEmpty(AddItem.Name) && (nameSize <= IdentityBase.MaxProfileNameLengthBytes);
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "add.name";
        }
      }

      if (details == null)
      {
        int typeSize = Encoding.UTF8.GetByteCount(AddItem.Type);
        bool typeValid = (0 < typeSize) && (typeSize <= IdentityBase.MaxProfileTypeLengthBytes);
        if (!typeValid)
        {
          log.Debug("Invalid type size in bytes {0}.", typeSize);
          details = "add.type";
        }
      }

      if ((details == null) && AddItem.SetThumbnailImage)
      {
        byte[] thumbnailImage = AddItem.ThumbnailImage.ToByteArray();

        bool imageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && ImageManager.ValidateImageFormat(thumbnailImage);
        if (!imageValid)
        {
          log.Debug("Invalid thumbnail image.");
          details = "add.thumbnailImage";
        }
      }

      if (details == null)
      {
        GpsLocation locLat = new GpsLocation(AddItem.Latitude, 0);
        if (!locLat.IsValid())
        {
          log.Debug("Invalid latitude {0}.", AddItem.Latitude);
          details = "add.latitude";
        }
      }

      if (details == null)
      {
        GpsLocation locLon = new GpsLocation(0, AddItem.Longitude);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", AddItem.Longitude);
          details = "add.longitude";
        }
      }

      if (details == null)
      {
        int extraDataSize = Encoding.UTF8.GetByteCount(AddItem.ExtraData);
        bool extraDataValid = extraDataSize <= IdentityBase.MaxProfileExtraDataLengthBytes;
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData size in bytes {0}.", extraDataSize);
          details = "add.extraData";
        }
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

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

      string details = null;

      byte[] identityId = ChangeItem.IdentityNetworkId.ToByteArray();
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
          details = "change.identityNetworkId";
        }
      }
      else
      {
        log.Debug("Invalid identity ID length '{0}'.", identityId.Length);
        details = "change.identityNetworkId";
      }

      if (details == null)
      {
        if (!ChangeItem.SetVersion
          && !ChangeItem.SetName
          && !ChangeItem.SetThumbnailImage
          && !ChangeItem.SetLocation
          && !ChangeItem.SetExtraData)
        {
          log.Debug("Nothing is going to change.");
          details = "change.set*";
        }
      }

      if ((details == null) && ChangeItem.SetVersion)
      {
        SemVer version = new SemVer(ChangeItem.Version);
        // Currently only supported version is 1.0.0.
        if (!version.Equals(SemVer.V100))
        {
          log.Debug("Unsupported version '{0}'.", version);
          details = "change.version";
        }
      }


      if ((details == null) && ChangeItem.SetName)
      {
        int nameSize = Encoding.UTF8.GetByteCount(ChangeItem.Name);
        bool nameValid = !string.IsNullOrEmpty(ChangeItem.Name) && (nameSize <= IdentityBase.MaxProfileNameLengthBytes);
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "change.name";
        }
      }

      if ((details == null) && ChangeItem.SetThumbnailImage)
      {
        byte[] thumbnailImage = ChangeItem.ThumbnailImage.ToByteArray();

        bool deleteImage = thumbnailImage.Length == 0;
        bool imageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes)
          && (deleteImage || ImageManager.ValidateImageFormat(thumbnailImage));
        if (!imageValid)
        {
          log.Debug("Invalid thumbnail image.");
          details = "change.thumbnailImage";
        }
      }

      if ((details == null) && ChangeItem.SetLocation)
      {
        GpsLocation locLat = new GpsLocation(ChangeItem.Latitude, 0);
        if (!locLat.IsValid())
        {
          log.Debug("Invalid latitude {0}.", ChangeItem.Latitude);
          details = "change.latitude";
        }
      }

      if ((details == null) && ChangeItem.SetLocation)
      {
        GpsLocation locLon = new GpsLocation(0, ChangeItem.Longitude);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", ChangeItem.Longitude);
          details = "change.longitude";
        }
      }

      if ((details == null) && ChangeItem.SetExtraData)
      {
        int extraDataSize = Encoding.UTF8.GetByteCount(ChangeItem.ExtraData);
        bool extraDataValid = extraDataSize <= IdentityBase.MaxProfileExtraDataLengthBytes;
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData size in bytes {0}.", extraDataSize);
          details = "change.extraData";
        }
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

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
        log.Debug("Invalid identity ID length '{0}'.", identityId.Length);
        details = "delete.identityNetworkId";
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Validates incoming SharedProfileAddItem update item.
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

      string details = null;
      if (IdentityDatabase.Count >= IdentityBase.MaxHostedIdentities)
      {
        log.Debug("Target server already sent too many profiles.");
        details = "add";
      }

      if (details == null)
      {
        SemVer version = new SemVer(AddItem.Version);

        // Currently only supported version is 1.0.0.
        if (!version.Equals(SemVer.V100))
        {
          log.Debug("Unsupported version '{0}'.", version);
          details = "add.version";
        }
      }

      if (details == null)
      {
        byte[] pubKey = AddItem.IdentityPublicKey.ToByteArray();
        bool pubKeyValid = (0 < pubKey.Length) && (pubKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes);
        if (pubKeyValid)
        {
          byte[] id = Crypto.Sha256(pubKey);
          if (IdentityDatabase.ContainsKey(id))
          {
            log.Debug("Identity with public key '{0}' (ID '{1}') already exists.", pubKey.ToHex(), id.ToHex());
            details = "add.identityPublicKey";
          }
        }
        else
        {
          log.Debug("Invalid public key length '{0}'.", pubKey.Length);
          details = "add.identityPublicKey";
        }
      }

      if (details == null)
      {
        int nameSize = Encoding.UTF8.GetByteCount(AddItem.Name);
        bool nameValid = !string.IsNullOrEmpty(AddItem.Name) && (nameSize <= IdentityBase.MaxProfileNameLengthBytes);
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "add.name";
        }
      }

      if (details == null)
      {
        int typeSize = Encoding.UTF8.GetByteCount(AddItem.Type);
        bool typeValid = (0 < typeSize) && (typeSize <= IdentityBase.MaxProfileTypeLengthBytes);
        if (!typeValid)
        {
          log.Debug("Invalid type size in bytes {0}.", typeSize);
          details = "add.type";
        }
      }

      if ((details == null) && AddItem.SetThumbnailImage)
      {
        byte[] thumbnailImage = AddItem.ThumbnailImage.ToByteArray();

        bool imageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && ImageManager.ValidateImageFormat(thumbnailImage);
        if (!imageValid)
        {
          log.Debug("Invalid thumbnail image.");
          details = "add.thumbnailImage";
        }
      }

      if (details == null)
      {
        GpsLocation locLat = new GpsLocation(AddItem.Latitude, 0);
        if (!locLat.IsValid())
        {
          log.Debug("Invalid latitude {0}.", AddItem.Latitude);
          details = "add.latitude";
        }
      }

      if (details == null)
      {
        GpsLocation locLon = new GpsLocation(0, AddItem.Longitude);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", AddItem.Longitude);
          details = "add.longitude";
        }
      }

      if (details == null)
      {
        int extraDataSize = Encoding.UTF8.GetByteCount(AddItem.ExtraData);
        bool extraDataValid = extraDataSize <= IdentityBase.MaxProfileExtraDataLengthBytes;
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData size in bytes {0}.", extraDataSize);
          details = "add.extraData";
        }
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }



  }
}
