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
    private static Logger _log = new Logger("ProfileServer.Network.RelayList");

    /// <summary>Lock object for synchronized access to relay list structures.</summary>
    private object _lock = new object();

    /// <summary>
    /// List of active relay connections mapped by the caller/callee's tokens or relay ID.
    /// </summary>
    private Dictionary<Guid, RelayConnection> _relayMap = new Dictionary<Guid, RelayConnection>(StructuralEqualityComparer<Guid>.Default);


    /// <summary>
    /// Creates a new network relay between a caller identity and one of the profile server's customer identities that is online.
    /// </summary>
    /// <param name="caller">Initiator of the call.</param>
    /// <param name="callee">Profile server's customer client to be called.</param>
    /// <param name="serviceName">Name of the application service to use.</param>
    /// <param name="request">CallIdentityApplicationServiceRequest message that the caller send in order to initiate the call.</param>
    /// <returns>New relay connection object if the function succeeds, or null otherwise.</returns>
    public RelayConnection CreateNetworkRelay(IncomingClient caller, IncomingClient callee, string serviceName, IProtocolMessage<Iop.Profileserver.Message> request)
    {
      _log.Trace("(Caller.Id:{0},Callee.Id:{1},ServiceName:'{2}')", caller.Id.ToHex(), callee.Id.ToHex(), serviceName);

      RelayConnection res = null;

      RelayConnection relay = new RelayConnection(this, caller, callee, serviceName, request);
      lock (_lock)
      {
        _relayMap.Add(relay.Id, relay);
        _relayMap.Add(relay.CallerToken, relay);
        _relayMap.Add(relay.CalleeToken, relay);
      }

      _log.Debug("Relay ID '{0}' added to the relay list.", relay.Id);
      _log.Debug("Caller token '{0}' added to the relay list.", relay.CallerToken);
      _log.Debug("Callee token '{0}' added to the relay list.", relay.CalleeToken);

      res = relay;

      _log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Destroys relay connection and all references to it.
    /// </summary>
    /// <param name="relay">Relay connection to destroy.</param>
    public async Task DestroyNetworkRelay(RelayConnection relay)
    {
      _log.Trace("(Relay.id:'{0}')", relay.Id);

      bool destroyed = await relay.TestAndSetDestroyed();
      if (!destroyed)
      {
        bool relayIdRemoved = false;
        bool callerTokenRemoved = false;
        bool calleeTokenRemoved = false;
        lock (_lock)
        {
          relayIdRemoved = _relayMap.Remove(relay.Id);
          callerTokenRemoved = _relayMap.Remove(relay.CallerToken);
          calleeTokenRemoved = _relayMap.Remove(relay.CalleeToken);
        }

        if (!relayIdRemoved) _log.Error("Relay ID '{0}' not found in relay list.", relay.Id);
        if (!callerTokenRemoved) _log.Error("Caller token '{0}' not found in relay list.", relay.CallerToken);
        if (!calleeTokenRemoved) _log.Error("Callee token '{0}' not found in relay list.", relay.CalleeToken);

        relay.Dispose();
      }
      else _log.Trace("Relay ID '{0}' has been destroyed already.", relay.Id);

      _log.Trace("(-)");
    }


    /// <summary>
    /// Obtains relay using its ID, caller's token, or callee's token.
    /// </summary>
    /// <param name="token">Relay ID, caller's token, or callee's token</param>
    /// <returns>Relay that corresponds to the given GUID, or null if no such relay exists.</returns>
    public RelayConnection GetRelayByGuid(Guid token)
    {
      _log.Trace("(Guid:'{0}')", token);

      RelayConnection res = null;
      lock (_lock)
      {
        _relayMap.TryGetValue(token, out res);
      }

      if (res != null) _log.Trace("(-):*.Id='{0}'", res.Id);
      else _log.Trace("(-):null");
      return res;
    }


    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool _disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object _disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class.
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="_disposing"/> is set.
    /// </summary>
    /// <param name="_disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected virtual void Dispose(bool _disposing)
    {
      bool disposedAlready = false;
      lock (_disposingLock)
      {
        disposedAlready = _disposed;
        _disposed = true;
      }
      if (disposedAlready) return;

      if (_disposing)
      {
        lock (_lock)
        {
          List<RelayConnection> relays = _relayMap.Values.ToList();
          foreach (RelayConnection relay in relays)
            DestroyNetworkRelay(relay).Wait();
        }
      }
    }
  }
}
