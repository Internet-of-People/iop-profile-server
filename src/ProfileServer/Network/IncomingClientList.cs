using ProfileServer.Utils;
using ProfileServerCrypto;
using Iop.Profileserver;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServer.Network
{
  /// <summary>
  /// Represents a single item in IncomingClientList collections.
  /// </summary>
  public class PeerListItem
  {
    /// <summary>Network client object.</summary>
    public IncomingClient Client;  
  }

  /// <summary>
  /// Implements structures for managment of a server's network peers and clients.
  /// This includes context information of client to client calls over the node server relay.
  /// </summary>
  public class IncomingClientList
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.IncomingClientList");

    /// <summary>Lock object for synchronized access to client list structures.</summary>
    private object lockObject = new object();

    /// <summary>Server assigned client identifier for internal client maintanence purposes.</summary>
    private long clientLastId = 0;

    /// <summary>
    /// List of network peers mapped by their internal ID. All network peers are in this list.
    /// A network peer is any connected entity to the node's role server.
    /// </summary>
    private Dictionary<ulong, PeerListItem> peersByInternalId = new Dictionary<ulong, PeerListItem>();

    /// <summary>
    /// List of network peers mapped by their Identity ID. Only peers with known Identity ID are in this list.
    /// </summary>
    private Dictionary<byte[], List<PeerListItem>> peersByIdentityId = new Dictionary<byte[], List<PeerListItem>>(StructuralEqualityComparer<byte[]>.Default);

    /// <summary>
    /// List of online (checked-in) clients mapped by their Identity ID. Only node's clients are in this list.
    /// A client is an identity hosted by this server.
    /// </summary>
    private Dictionary<byte[], PeerListItem> clientsByIdentityId = new Dictionary<byte[], PeerListItem>(StructuralEqualityComparer<byte[]>.Default);


    /// <summary>
    /// List of active relay connections mapped by the caller/callee's tokens or relay ID.
    /// </summary>
    private Dictionary<Guid, RelayConnection> relaysByGuid = new Dictionary<Guid, RelayConnection>(StructuralEqualityComparer<Guid>.Default);



    /// <summary>
    /// Creates a copy of list of all network clients (peers) that are connected to the server.
    /// </summary>
    /// <returns>List of network clients.</returns>
    public List<IncomingClient> GetNetworkClientList()
    {
      log.Trace("()");

      List<IncomingClient> res = new List<IncomingClient>();

      lock (lockObject)
      {
        foreach (PeerListItem peer in peersByInternalId.Values)
          res.Add(peer.Client);
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Gets ID for a new client.
    /// </summary>
    /// <returns>New client's network ID.</returns>
    public ulong GetNewClientId()
    {
      log.Trace("()");
      ulong res = 0;

      long newId = Interlocked.Increment(ref clientLastId);
      res = (ulong)newId;

      log.Trace("(-):{0}", res.ToHex());
      return res;
    }


    /// <summary>
    /// Assigns ID to a new network client and safely adds it to the peersByInternalId list.
    /// </summary>
    /// <param name="Client">Network client to add.</param>
    public void AddNetworkPeer(IncomingClient Client)
    {
      log.Trace("()");

      PeerListItem peer = new PeerListItem();
      peer.Client = Client;

      lock (lockObject)
      {
        peersByInternalId.Add(Client.Id, peer);
      }
      log.Trace("Client.Id is {0}.", Client.Id.ToHex());

      log.Trace("(-)");
    }


    /// <summary>
    /// Adds a network client with identity to the peersByIdentityList.
    /// </summary>
    /// <param name="Client">Network client to add.</param>
    /// <returns>true if the function succeeds, false otherwise. The function may fail only 
    /// if there is an asynchrony in internal peer lists, which should never happen.</returns>
    public bool AddNetworkPeerWithIdentity(IncomingClient Client)
    {
      log.Trace("(Client.Id:{0})", Client.Id.ToHex());

      bool res = false;

      PeerListItem peer = null;
      byte[] identityId = Client.IdentityId;

      lock (lockObject)
      {
        // First we find the peer in the list of all peers.
        if (peersByInternalId.TryGetValue(Client.Id, out peer))
        {
          // Then we either have this identity in peersByIdentityId list, 
          // in which case we add another "instance" to the list,
          // or we create a new list for this peer.
          List<PeerListItem> list = null;
          bool listExists = peersByIdentityId.TryGetValue(identityId, out list);

          if (!listExists) list = new List<PeerListItem>();

          list.Add(peer);

          if (!listExists) peersByIdentityId.Add(identityId, list);
          res = true;
        }
      }

      if (!res)
        log.Error("peersByInternalId does not contain peer with internal ID {0}.", Client.Id.ToHex());

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Adds a checked-in client to the clientsByIdentityList.
    /// </summary>
    /// <param name="Client">Checked-in node's client to add.</param>
    /// <returns>true if the function succeeds, false otherwise. The function may fail only 
    /// if there is an asynchrony in internal peer lists, which should never happen.</returns>
    public async Task<bool> AddCheckedInClient(IncomingClient Client)
    {
      log.Trace("(Client.Id:{0})", Client.Id.ToHex());

      bool res = false;

      PeerListItem peer = null;
      PeerListItem clientToCheckOut = null;
      byte[] identityId = Client.IdentityId;

      lock (lockObject)
      {
        // First we find the peer in the list of all peers.
        if (peersByInternalId.ContainsKey(Client.Id))
        {
          peer = peersByInternalId[Client.Id];

          // Then we either have this identity checked-in using different network client,
          // in which case we want to disconnect that old identity's connection and replace it with the new one.
          clientsByIdentityId.TryGetValue(identityId, out clientToCheckOut);
          clientsByIdentityId[identityId] = peer;

          if (clientToCheckOut != null)
            clientToCheckOut.Client.IsOurCheckedInClient = false;

          Client.IsOurCheckedInClient = true;

          res = true;
        }
      }

      if (res && (clientToCheckOut != null))
      {
        log.Info("Identity ID '{0}' has been checked-in already via network peer internal ID {1} and will now be disconnected.", identityId.ToHex(), clientToCheckOut.Client.Id.ToHex());
        await clientToCheckOut.Client.CloseConnectionAsync();
      }

      if (!res)
        log.Error("peersByInternalId does not contain peer with internal ID {0}.", Client.Id.ToHex());

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Safely removes network client (peer) from all lists.
    /// </summary>
    /// <param name="Client">Network client to remove.</param>
    public void RemoveNetworkPeer(IncomingClient Client)
    {
      log.Trace("(Client.Id:{0})", Client.Id.ToHex());

      ulong internalId = Client.Id;
      byte[] identityId = Client.IdentityId;

      bool peerByInternalIdRemoveError = false;
      bool peerByIdentityIdRemoveError = false;
      bool clientByIdentityIdRemoveError = false;
      lock (lockObject)
      {
        // All peers should be in peersByInternalId list.
        if (peersByInternalId.ContainsKey(internalId)) peersByInternalId.Remove(internalId);
        else peerByInternalIdRemoveError = true;

        if (identityId != null)
        {
          // All peers with known Identity ID should be in peersByIdentityId list.
          peerByIdentityIdRemoveError = true;
          List<PeerListItem> list;
          if (peersByIdentityId.TryGetValue(identityId, out list))
          {
            for (int i = 0; i < list.Count; i++)
            {
              PeerListItem peerListItem = list[i];
              if (peerListItem.Client.Id == internalId)
              {
                list.RemoveAt(i);
                peerByIdentityIdRemoveError = false;
                break;
              }
            }

            // If the list is empty, delete it.
            if (list.Count == 0)
              peersByIdentityId.Remove(identityId);
          }

          // Only checked-in clients are in clientsByIdentityId list.
          // If a checked-in client was replaced by a new connection, its IsOurCheckedInClient field was set to false.
          if (Client.IsOurCheckedInClient)
          {
            if (clientsByIdentityId.ContainsKey(identityId)) clientsByIdentityId.Remove(identityId);
            else clientByIdentityIdRemoveError = true;
          }
        }
      }

      if (peerByInternalIdRemoveError)
        log.Error("Peer internal ID {0} not found in peersByInternalId list.", internalId.ToHex());

      if (peerByIdentityIdRemoveError)
        log.Error("Peer Identity ID '{0}' not found in peersByIdentityId list.", identityId.ToHex());

      if (clientByIdentityIdRemoveError)
        log.Error("Checked-in client Identity ID '{0}' not found in clientsByIdentityId list.", identityId.ToHex());

      log.Trace("(-)");
    }


    /// <summary>
    /// Finds a checked-in client by its identity ID.
    /// </summary>
    /// <param name="IdentityId">Identifier of the identity to search for.</param>
    /// <returns>Client object of the requested online identity, or null if the identity is not online.</returns>
    public IncomingClient GetCheckedInClient(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());

      IncomingClient res = null;
      PeerListItem peer;
      lock (lockObject)
      {
        if (clientsByIdentityId.TryGetValue(IdentityId, out peer))
          res = peer.Client;
      }

      if (res != null) log.Trace("(-):{0}", res.Id.ToHex());
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Checks whether a certain identity is online.
    /// </summary>
    /// <param name="IdentityId">Identifier of the identity to check for.</param>
    /// <returns>true if the identity is online, false otherwise.</returns>
    public bool IsIdentityOnline(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());

      bool res = GetCheckedInClient(IdentityId) != null;

      return res;
    }


    /// <summary>
    /// Creates a new network relay between a caller identity and one of the node's customer identities that is online.
    /// </summary>
    /// <param name="Caller">Initiator of the call.</param>
    /// <param name="Callee">Node's customer client to be called.</param>
    /// <param name="ServiceName">Name of the application service to use.</param>
    /// <param name="RequestMessage">CallIdentityApplicationServiceRequest message that the caller send in order to initiate the call.</param>
    /// <returns>New relay connection object if the function succeeds, or null otherwise.</returns>
    public RelayConnection CreateNetworkRelay(IncomingClient Caller, IncomingClient Callee, string ServiceName, Message RequestMessage)
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
  }
}
