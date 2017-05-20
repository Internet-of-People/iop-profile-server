using IopCommon;
using IopProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ProfileServer.Data.Models
{
  /// <summary>
  /// Database representation of so called Follower server, which is a remote profile server, for which the profile server acts as a neighbor. 
  /// Follower is a server that asked the profile server to share its profile database with it and the profile server is sending 
  /// updates of the database to the follower.
  /// <para>
  /// The opposite direction relation is represented by <see cref="Neighbor"/> class.
  /// </para>
  /// </summary>
  public class Follower: RemoteServerBase
  {
    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer.Data.Models.Follower");
  }
}
