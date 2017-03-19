using ProfileServer.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Network
{
  /// <summary>
  /// Context information related to the neighborhood initialization process.
  /// </summary>
  public class NeighborhoodInitializationProcessContext
  {
    /// <summary>Snapshot of all hosted identities at the moment the client sent request to start the neighborhood initialization process.</summary>
    public List<HostedIdentity> HostedIdentities;

    /// <summary>Number of items from HostedIdentities that has been processed already.</summary>
    public int IdentitiesDone;
  }
}
