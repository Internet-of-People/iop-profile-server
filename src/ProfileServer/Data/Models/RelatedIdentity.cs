using ProfileServer.Utils;
using ProfileServerProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Hosted identities can announce relations to other identities using relationship cards.
  /// This class represents such a relation in the database.
  /// </summary>
  public class RelatedIdentity
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Data.Models.RelatedIdentity");

    /// <summary>
    /// Maximum number of relations that an identity is allowed to have. This is protocol limit. 
    /// The actual value is defined in the configuration file, but it has to be lower than this maximal number.
    /// </summary>
    public const int MaxIdentityRelations = 2000;

    /// <summary>Length in bytes of relationship card identifiers.</summary>
    public const int CardIdentifierLength = 32;

    /// <summary>Maximum number of bytes that relationship card type can occupy.</summary>
    public const int MaxCardTypeLengthBytes = 64;

    /// <summary>Maximum number of bytes that card application identifier can occupy.</summary>
    public const int MaxApplicationIdLengthBytes = 32;

    /// <summary>Maximum number of bytes that a signature can occupy.</summary>
    public const int MaxSignatureLengthBytes = 100;



    /// <summary>Identifier of the hosted identity.</summary>
    /// <remarks>This is part of the key and index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [MaxLength(IdentityBase.IdentifierLength)]
    public byte[] IdentityId { get; set; }

    /// <summary>Identifier of the related identity.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [MaxLength(IdentityBase.IdentifierLength)]
    public byte[] RelatedToIdentityId { get; set; }

    /// <summary>Identifier of the card application.</summary>
    /// <remarks>This is part of the key and index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [MaxLength(MaxApplicationIdLengthBytes)]
    public byte[] ApplicationId { get; set; }

    /// <summary>Identifier of the relationship card.</summary>
    [MaxLength(CardIdentifierLength)]
    public byte[] CardId { get; set; }

    /// <summary>Version of the relationship card.</summary>
    [MaxLength(3)]
    public byte[] CardVersion { get; set; }


    /// <summary>Type of the relationship card.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(MaxCardTypeLengthBytes)]
    public string Type { get; set; }

    /// <summary>Time from which the card is valid.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    public DateTime ValidFrom { get; set; }

    /// <summary>Time after which the card is not valid.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    public DateTime ValidTo { get; set; }

    /// <summary>Identifier of the home node or empty array if the identity is hosted by this node.</summary>
    [MaxLength(IdentityBase.MaxPublicKeyLengthBytes)]
    public byte[] IssuerPublicKey { get; set; }

    /// <summary>Identifier of the home node or empty array if the identity is hosted by this node.</summary>
    [MaxLength(IdentityBase.MaxPublicKeyLengthBytes)]
    public byte[] RecipientPublicKey { get; set; }

    /// <summary>Signature of CardId value using private key of the issuer of the card.</summary>
    [MaxLength(MaxSignatureLengthBytes)]
    public byte[] IssuerSignature { get; set; }

    /// <summary>Signature of Protobuf CardApplicationInformation message using private key of the recipient of the card.</summary>
    [MaxLength(MaxSignatureLengthBytes)]
    public byte[] RecipientSignature { get; set; }
  }
}
