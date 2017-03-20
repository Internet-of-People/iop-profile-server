using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IopCommon;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Types of actions between neighbor profile servers.
  /// </summary>
  public enum NeighborhoodActionType
  {
    /// <summary>
    /// LOC server informed the profile server about a new server in its neighborhood.
    /// The profile server contacts the neighbor and ask it to share its profile database.
    /// </summary>
    AddNeighbor = 1,

    /// <summary>
    /// The Cron component found out that a neighbor expired.
    /// The profile server removes the profiles hosted on the neighbor server from its database.
    /// Then it creates StopNeighborhoodUpdates action.
    /// </summary>
    RemoveNeighbor = 2,

    /// <summary>
    /// Profile server removed a neighbor and wants to ask the neighbor profile server 
    /// to stop sending profile updates.
    /// </summary>
    StopNeighborhoodUpdates = 3,

    /// <summary>
    /// New identity registered and initialized its profile on the profile server.
    /// The profile server has to inform its followers about the change.
    /// </summary>
    AddProfile = 10,

    /// <summary>
    /// The profile server wants to refresh profiles on the follower server in order to prevent their expiration.
    /// </summary>
    RefreshProfiles = 11,

    /// <summary>
    /// Existing identity changed its profile on the profile server.
    /// The profile server has to inform its followers about the change.
    /// </summary>
    ChangeProfile = 12,

    /// <summary>
    /// Existing identity cancelled its hosting agreement with the profile server.
    /// The profile server has to inform its followers about the change.
    /// </summary>
    RemoveProfile = 13,

    /// <summary>
    /// Purpose of this action is to prevent other profile actions to be sent as updates to followers 
    /// before the neighborhood initialization process is finished.
    /// </summary>
    InitializationProcessInProgress = 14
  }


  /// <summary>
  /// Neighborhood actions are actions within the profile server's neighborhood that the profile server is intended to do as soon as possible.
  /// For example, if a hosted profile is changed, the change should be propagated to the profile server followers. When this happens, the profile 
  /// server creates a neighborhood action for each follower that will take care of this change propagation.
  /// <para>
  /// Each change has its target server. All changes to a single target has to be processed in the correct order, otherwise the integrity
  /// of information might be corrupted.
  /// </para>
  /// </summary>
  public class NeighborhoodAction
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Models.NeighborhoodAction");

    /// <summary>Unique identifier of the action for ordering purposes.</summary>
    /// <remarks>This is index and key - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    public int Id { get; set; }

    /// <summary>Network identifier of the neighbor/follower profile server.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] ServerId { get; set; }

    /// <summary>When was the action created.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public DateTime Timestamp { get; set; }

    /// <summary>Time before which the action can not be executed, or null if it can be executed at any time.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    public DateTime? ExecuteAfter { get; set; }

    /// <summary>Type of the action.</summary>
    /// <remarks>This is index - see ProfileServer.Data.Context.OnModelCreating.</remarks>
    [Required]
    public NeighborhoodActionType Type { get; set; }

    /// <summary>Network identifier of the identity which profile is related to the action.</summary>
    /// <remarks>
    /// This is index - see ProfileServer.Data.Context.OnModelCreating.
    /// This property is optional - see ProfileServer.Data.Context.OnModelCreating.
    /// </remarks>    /// 
    [MaxLength(ProtocolHelper.NetworkIdentifierLength)]
    public byte[] TargetIdentityId { get; set; }

    /// <summary>Description of the action as a JSON encoded string.</summary>
    public string AdditionalData { get; set; }

    /// <summary>
    /// Returns true if the action is one of the profile actions, which means its target server 
    /// is the profile server's follower.
    /// </summary>
    /// <returns>true if the action is one of the profile actions, false otherwise.</returns>
    public bool IsProfileAction()
    {
      return Type >= NeighborhoodActionType.AddProfile;
    }
  }
}
