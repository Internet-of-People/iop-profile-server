using HomeNet.Utils;
using HomeNetProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Data.Models
{
  /// <summary>
  /// Database representation of IoP Identity profile that is hosted by the node.
  /// </summary>
  public class HomeIdentity : BaseIdentity
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("HomeNet.Data.Models.HomeIdentity");
  }
}
