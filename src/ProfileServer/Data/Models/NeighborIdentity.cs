using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using IopCommon;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Database representation of IoP Identity profile that is hosted in the profile server's neighborhood.
  /// </summary>
  public class NeighborIdentity : IdentityBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Models.NeighborIdentity");
  }
}
