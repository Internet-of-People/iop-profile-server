using IopCommon;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IopServerCore.Network
{
  /// <summary>
  /// Represents a single item in IncomingClientList collections.
  /// </summary>
  public class PeerListItem<TMessage>
  {
    /// <summary>Network client object.</summary>
    public IncomingClientBase<TMessage> Client;  
  }

  /// <summary>
  /// Implements structures for managment of a server's network peers and authenticated clients.
  /// </summary>
  public class IncomingClientList<TMessage>
  {
    private static Logger log = new Logger("IopServerCore.Network.IncomingClientList");

    /// <summary>Lock object for synchronized access to client list structures.</summary>
    private object lockObject = new object();

    /// <summary>Server assigned client identifier for internal client maintanence purposes.</summary>
    private long clientLastId = 0;

    /// <summary>
    /// List of network peers mapped by their internal ID. All network peers are in this list.
    /// A network peer is any connected entity to the server's role server.
    /// </summary>
    private Dictionary<ulong, PeerListItem<TMessage>> peersByInternalId = new Dictionary<ulong, PeerListItem<TMessage>>();

    /// <summary>
    /// List of network peers mapped by their Identity ID. Only peers with known Identity ID are in this list.
    /// </summary>
    private Dictionary<byte[], List<PeerListItem<TMessage>>> peersByIdentityId = new Dictionary<byte[], List<PeerListItem<TMessage>>>(StructuralEqualityComparer<byte[]>.Default);

    /// <summary>
    /// List of clients mapped by their Identity ID. The clients in this list are authenticated and their connections to the server are exclusive, 
    /// which means that if another connection is established and the same client is authenticated over the second connection, the first connection must be closed.
    /// </summary>
    private Dictionary<byte[], PeerListItem<TMessage>> authenticatedClientsByIdentityId = new Dictionary<byte[], PeerListItem<TMessage>>(StructuralEqualityComparer<byte[]>.Default);

    public int Count => peersByIdentityId.Count;

    /// <summary>
    /// Creates a copy of list of all network clients (peers) that are connected to the server.
    /// </summary>
    /// <returns>List of network clients.</returns>
    public List<IncomingClientBase<TMessage>> GetNetworkClientList()
    {
      log.Trace("()");

      var res = new List<IncomingClientBase<TMessage>>();

      lock (lockObject)
      {
        foreach (var peer in peersByInternalId.Values)
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
    public void AddNetworkPeer(IncomingClientBase<TMessage> Client)
    {
      log.Trace("()");

      var peer = new PeerListItem<TMessage>();
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
    public bool AddNetworkPeerWithIdentity(IncomingClientBase<TMessage> Client)
    {
      log.Trace("(Client.Id:{0})", Client.Id.ToHex());

      bool res = false;

      PeerListItem<TMessage> peer = null;
      byte[] identityId = Client.IdentityId;

      lock (lockObject)
      {
        // First we find the peer in the list of all peers.
        if (peersByInternalId.TryGetValue(Client.Id, out peer))
        {
          // Then we either have this identity in peersByIdentityId list, 
          // in which case we add another "instance" to the list,
          // or we create a new list for this peer.
          List<PeerListItem<TMessage>> list = null;
          bool listExists = peersByIdentityId.TryGetValue(identityId, out list);

          if (!listExists) list = new List<PeerListItem<TMessage>>();

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
    /// Adds an authenticated online client to the authenticatedClientsByIdentityId.
    /// </summary>
    /// <param name="Client">Online authenticated server's client to add.</param>
    /// <returns>true if the function succeeds, false otherwise. The function may fail only 
    /// if there is an asynchrony in internal peer lists, which should never happen.</returns>
    public async Task<bool> AddAuthenticatedOnlineClient(IncomingClientBase<TMessage> Client)
    {
      log.Trace("(Client.Id:{0})", Client.Id.ToHex());

      bool res = false;

      PeerListItem<TMessage> peer = null;
      PeerListItem<TMessage> clientToCheckOut = null;
      byte[] identityId = Client.IdentityId;

      lock (lockObject)
      {
        // First we find the peer in the list of all peers.
        if (peersByInternalId.ContainsKey(Client.Id))
        {
          peer = peersByInternalId[Client.Id];

          // Then we either have this identity checked-in using different network client,
          // in which case we want to disconnect that old identity's connection and replace it with the new one.
          authenticatedClientsByIdentityId.TryGetValue(identityId, out clientToCheckOut);
          authenticatedClientsByIdentityId[identityId] = peer;

          if (clientToCheckOut != null)
            clientToCheckOut.Client.IsAuthenticatedOnlineClient = false;

          Client.IsAuthenticatedOnlineClient = true;

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
    public void RemoveNetworkPeer(IncomingClientBase<TMessage> Client)
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
          List<PeerListItem<TMessage>> list;
          if (peersByIdentityId.TryGetValue(identityId, out list))
          {
            for (int i = 0; i < list.Count; i++)
            {
              PeerListItem<TMessage> peerListItem = list[i];
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
          // If a checked-in client was replaced by a new connection, its IsAuthenticatedOnlineClient field was set to false.
          if (Client.IsAuthenticatedOnlineClient)
          {
            if (authenticatedClientsByIdentityId.ContainsKey(identityId)) authenticatedClientsByIdentityId.Remove(identityId);
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
    /// Finds an authenticated exclusively connected client by its identity ID.
    /// </summary>
    /// <param name="IdentityId">Identifier of the identity to search for.</param>
    /// <returns>Client object of the requested online identity, or null if the identity is not online.</returns>
    public IncomingClientBase<TMessage> GetAuthenticatedOnlineClient(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());

      IncomingClientBase<TMessage> res = null;
      PeerListItem<TMessage> peer;
      lock (lockObject)
      {
        if (authenticatedClientsByIdentityId.TryGetValue(IdentityId, out peer))
          res = peer.Client;
      }

      if (res != null) log.Trace("(-):{0}", res.Id.ToHex());
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Checks whether a certain identity is online and authenticated.
    /// </summary>
    /// <param name="IdentityId">Identifier of the identity to check for.</param>
    /// <returns>true if the identity is online, false otherwise.</returns>
    public bool IsIdentityOnlineAuthenticated(byte[] IdentityId)
    {
      log.Trace("(IdentityId:'{0}')", IdentityId.ToHex());

      bool res = GetAuthenticatedOnlineClient(IdentityId) != null;

      return res;
    }
  }
}
