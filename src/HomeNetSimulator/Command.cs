using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNetSimulator
{
  /// <summary>
  /// All types of commands that are supported. Note that Unknown represents an invalid command.
  /// </summary>
  public enum CommandType { Unknown, ProfileServer, StartServer, Neighborhood, Neighbor, Identity, TestQuery, Delay }

  /// <summary>
  /// Base class for all types for commands.
  /// </summary>
  public class Command
  {
    /// <summary>Type of the command.</summary>
    public CommandType Type;
  }


  /// <summary>
  /// ProfileServer command creates one or more profile servers with associated LBN server.
  /// </summary>
  public class CommandProfileServer : Command
  {
    /// <summary>Name of the group of the servers.</summary>
    public string GroupName;

    /// <summary>Number of instances to create.</summary>
    public int Count;
    
    /// <summary>TCP port number from which TCP ports of each profile server and associated LBN servers are to be calculated.</summary>
    public int BasePort;

    /// <summary>GPS latitude in the decimal form of the target area centre.</summary>
    public decimal Latitude;

    /// <summary>GPS longitude in the decimal form of the target area centre.</summary>
    public decimal Longitude;

    /// <summary>Radius in metres that together with Latitude and Longitude specify the target area.</summary>
    public int Radius;
  }
}
