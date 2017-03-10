using IopCommon;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProfileServer.Network
{
  public class RelayList : IDisposable
  {
    private static Logger log = new Logger("ProfileServer.Network.RelayList");

    /// <summary>Lock object for synchronized access to relay list structures.</summary>
    private object lockObject = new object();

    /// <summary>
    /// List of active relay connections mapped by the caller/callee's tokens or relay ID.
    /// </summary>
    private Dictionary<Guid, RelayConnection> relaysByGuid = new Dictionary<Guid, RelayConnection>(StructuralEqualityComparer<Guid>.Default);


    /// <summary>
    /// Creates a new network relay between a caller identity and one of the profile server's customer identities that is online.
    /// </summary>
    /// <param name="Caller">Initiator of the call.</param>
    /// <param name="Callee">Profile server's customer client to be called.</param>
    /// <param name="ServiceName">Name of the application service to use.</param>
    /// <param name="RequestMessage">CallIdentityApplicationServiceRequest message that the caller send in order to initiate the call.</param>
    /// <returns>New relay connection object if the function succeeds, or null otherwise.</returns>
    public RelayConnection CreateNetworkRelay(IncomingClient Caller, IncomingClient Callee, string ServiceName, PsProtocolMessage RequestMessage)
    {
      log.Trace("(Caller.Id:{0},Callee.Id:{1},ServiceName:'{2}')", Caller.Id.ToHex(), Callee.Id.ToHex(), ServiceName);

      RelayConnection res = null;

      RelayConnection relay = new RelayConnection(Caller, Callee, ServiceName, RequestMessage);
      lock (lockObject)
      {
        relaysByGuid.Add(relay.GetId(), relay);
        relaysByGuid.Add(relay.GetCallerToken(), relay);
        relaysByGuid.Add(relay.GetCalleeToken(), relay);
      }

      log.Debug("Relay ID '{0}' added to the relay list.", relay.GetId());
      log.Debug("Caller token '{0}' added to the relay list.", relay.GetCallerToken());
      log.Debug("Callee token '{0}' added to the relay list.", relay.GetCalleeToken());

      res = relay;

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Destroys relay connection and all references to it.
    /// </summary>
    /// <param name="Relay">Relay connection to destroy.</param>
    public async Task DestroyNetworkRelay(RelayConnection Relay)
    {
      log.Trace("(Relay.id:'{0}')", Relay.GetId());

      bool destroyed = await Relay.TestAndSetDestroyed();
      if (!destroyed)
      {
        bool relayIdRemoved = false;
        bool callerTokenRemoved = false;
        bool calleeTokenRemoved = false;
        lock (lockObject)
        {
          relayIdRemoved = relaysByGuid.Remove(Relay.GetId());
          callerTokenRemoved = relaysByGuid.Remove(Relay.GetCallerToken());
          calleeTokenRemoved = relaysByGuid.Remove(Relay.GetCalleeToken());
        }

        if (!relayIdRemoved) log.Error("Relay ID '{0}' not found in relay list.", Relay.GetId());
        if (!callerTokenRemoved) log.Error("Caller token '{0}' not found in relay list.", Relay.GetCallerToken());
        if (!calleeTokenRemoved) log.Error("Callee token '{0}' not found in relay list.", Relay.GetCalleeToken());

        Relay.Dispose();
      }
      else log.Trace("Relay ID '{0}' has been destroyed already.", Relay.GetId());

      log.Trace("(-)");
    }


    /// <summary>
    /// Obtains relay using its ID, caller's token, or callee's token.
    /// </summary>
    /// <param name="Guid">Relay ID, caller's token, or callee's token</param>
    /// <returns>Relay that corresponds to the given GUID, or null if no such relay exists.</returns>
    public RelayConnection GetRelayByGuid(Guid Guid)
    {
      log.Trace("(Guid:'{0}')", Guid);

      RelayConnection res = null;
      lock (lockObject)
      {
        relaysByGuid.TryGetValue(Guid, out res);
      }

      if (res != null) log.Trace("(-):*.Id='{0}'", res.GetId());
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class.
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected virtual void Dispose(bool Disposing)
    {
      bool disposedAlready = false;
      lock (disposingLock)
      {
        disposedAlready = disposed;
        disposed = true;
      }
      if (disposedAlready) return;

      if (Disposing)
      {
        lock (lockObject)
        {
          List<RelayConnection> relays = relaysByGuid.Values.ToList();
          foreach (RelayConnection relay in relays)
            DestroyNetworkRelay(relay).Wait();
        }
      }
    }
  }
}
