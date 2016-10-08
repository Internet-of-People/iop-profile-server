using HomeNet.Utils;
using HomeNetCrypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Network
{
  /// <summary>
  /// Implements customer client's application services.
  /// The list of application services is valid only for the currently opened session.
  /// When the client disconnects from the home node, it has to add all application services again.
  /// </summary>
  public class ApplicationServices
  {
    private PrefixLogger log;

    /// <summary>Lock object for synchronized access to application services.</summary>
    private object listLock = new object();

    /// <summary>Server assigned client identifier for internal client maintanence purposes.</summary>
    private HashSet<string> serviceNames = new HashSet<string>();

    /// <summary>Initializes the class logger.</summary>
    public ApplicationServices(string LogPrefix)
    {
      string logName = "HomeNet.Network.ApplicationServices";
      log = new PrefixLogger(logName, LogPrefix);
      log.Trace("()");

      log.Trace("(-)");
    }

    /// <summary>
    /// Safely adds a service name to the list of supported services within the current session.
    /// </summary>
    /// <param name="ServiceName">Name of the application service to add.</param>
    /// <returns>true if the function succeeds, false if the number of client's application services exceeded the limit.</returns>
    /// <remarks>If the function fails, the set of enabled services is not changed.</remarks>
    public bool AddLimit(IEnumerable<string> ServiceNames)
    {
      log.Trace("(ServiceName:'{0}')", string.Join(",", ServiceNames));

      bool res = false;
      lock (listLock)
      {
        HashSet<string> newSet = new HashSet<string>(serviceNames);
        foreach (string serviceName in ServiceNames)
          newSet.Add(serviceName);

        if (newSet.Count < Client.MaxClientApplicationServices)
        {
          serviceNames = newSet;
          res = true;
        }
      }

      log.Trace("List now contains {0} application services.", serviceNames.Count);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Safely removes a service name from the list of supported services within the current session.
    /// </summary>
    /// <param name="ServiceName">Name of the application service to remove.</param>
    /// <returns>true if the function succeeds, false if the given service name was not found in the list.</returns>
    public bool Remove(string ServiceName)
    {
      log.Trace("(ServiceName:'{0}')", ServiceName);

      bool res = false;
      lock (listLock)
      {
        res = serviceNames.Remove(ServiceName);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Obtains a copy of the list of all application services.
    /// </summary>
    /// <returns>List of all services.</returns>
    public HashSet<string> Get()
    {
      log.Trace("()");

      HashSet<string> res = null;
      lock (listLock)
      {
        res = new HashSet<string>(serviceNames);
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }

  }
}
