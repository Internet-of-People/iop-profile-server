using HomeNet.Utils;
using HomeNetCrypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HomeNet.Network
{
  /// <summary>
  /// Represents a single item in ClientList collections.
  /// </summary>
  public class PeerListItem
  {
    /// <summary>Network client object.</summary>
    public Client Client;  
  }

  /// <summary>
  /// Implements structures for managment of a server's network peers and clients.
  /// </summary>
  public class ClientList
  {
    private PrefixLogger log;

    /// <summary>Lock object for synchronized access to client list.</summary>
    private object listLock = new object();

    /// <summary>Server assigned client identifier for internal client maintanence purposes.</summary>
    private ulong clientLastId = 0;

    /// <summary>
    /// List of network peers by their internal ID. All network peers are in this list.
    /// A network peer is any connected entity to the node's role server.
    /// </summary>
    private Dictionary<ulong, PeerListItem> peersByInternalId = new Dictionary<ulong, PeerListItem>();

    /// <summary>
    /// List of network peers by their Identity ID. Only peers with known Identity ID are in this list.
    /// </summary>
    private Dictionary<byte[], List<PeerListItem>> peersByIdentityId = new Dictionary<byte[], List<PeerListItem>>();

    /// <summary>
    /// List of online clients by their Identity ID. Only node's clients are in this list.
    /// A client is an identity for which the node acts as a home node.
    /// </summary>
    private Dictionary<byte[], PeerListItem> clientsByIdentityId = new Dictionary<byte[], PeerListItem>();
    
    /// <summary>Creates </summary>
    /// <param name="IdBase">Base number of internal identifiers of clients. First client's ID is going to be IdBase + 1.</param>
    public ClientList(ulong IdBase, string LogPrefix)
    {
      string logName = "HomeNet.Network.ClientList";
      this.log = new PrefixLogger(logName, LogPrefix);

      log.Trace("(IdBase:0x{0:X16})", IdBase);

      clientLastId = IdBase + 1;

      log.Trace("(-)");
    }


    /// <summary>
    /// Creates a copy of list of all network clients (peers) that are connected to the server.
    /// </summary>
    /// <returns>List of network clients.</returns>
    public List<Client> GetNetworkClientList()
    {
      log.Trace("()");

      List<Client> res = new List<Client>();

      lock (listLock)
      {
        foreach (PeerListItem peer in peersByInternalId.Values)
          res.Add(peer.Client);
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }



    /// <summary>
    /// Assigns ID to a new network client and safely adds it to the peersByInternalId list.
    /// </summary>
    /// <param name="Client">Network client to add.</param>
    public void AddNetworkPeer(Client Client)
    {
      log.Trace("()", Client.Id);

      PeerListItem peer = new PeerListItem();
      peer.Client = Client;

      lock (listLock)
      {
        Client.Id = clientLastId;
        clientLastId++;
        peersByInternalId.Add(Client.Id, peer);
      }
      log.Trace("Client.Id is 0x{0:X16}.", Client.Id);

      log.Trace("(-)");
    }

    /// <summary>
    /// Adds a network client with identity to the peersByIdentityList.
    /// </summary>
    /// <param name="Client">Network client to add.</param>
    /// <returns>true if the function succeeds, false otherwise. The function may fail only 
    /// if there is an asynchrony in internal peer lists, which should never happen.</returns>
    public bool AddNetworkPeerWithIdentity(Client Client)
    {
      log.Trace("(Client.Id:0x{0:X16})", Client.Id);

      bool res = false;

      PeerListItem peer = null;
      byte[] identityId = Client.IdentityId;

      lock (listLock)
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
        log.Error("peersByInternalId does not contain peer with internal ID 0x{0:X16}.", Client.Id);

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Adds a checked-in client to the clientsByIdentityList.
    /// </summary>
    /// <param name="Client">Checked-in node's client to add.</param>
    /// <returns>true if the function succeeds, false otherwise. The function may fail only 
    /// if there is an asynchrony in internal peer lists, which should never happen.</returns>
    public bool AddCheckedInClient(Client Client)
    {
      log.Trace("(Client.Id:0x{0:X16})", Client.Id);

      bool res = false;

      PeerListItem peer = null;
      PeerListItem clientToCheckOut = null;
      byte[] identityId = Client.IdentityId;

      lock (listLock)
      {
        // First we find the peer in the list of all peers.
        if (peersByInternalId.ContainsKey(Client.Id))
        {
          peer = peersByInternalId[Client.Id];

          // Then we either have this identity checked-in using different network client,
          // in which case we want to disconnect that old identity's connection and replace it with the new one.
          clientsByIdentityId.TryGetValue(identityId, out clientToCheckOut);
          clientsByIdentityId[identityId] = peer;

          res = true;
        }
      }

      if (res && (clientToCheckOut != null))
      {
        log.Info("Identity ID '{0}' has been checked-in already via network peer internal ID 0x{1:X16} and will now be disconnected.", clientToCheckOut.Client.Id);
        clientToCheckOut.Client.Dispose();
      }

      if (!res)
        log.Error("peersByInternalId does not contain peer with internal ID 0x{0:X16}.", Client.Id);

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Safely removes network client (peer) from all lists.
    /// </summary>
    /// <param name="Client">Network client to remove.</param>
    public void RemoveNetworkPeer(Client Client)
    {
      log.Trace("()");

      ulong internalId = Client.Id;
      byte[] identityId = Client.IdentityId;

      bool peerByInternalIdRemoveError = false;
      bool peerByIdentityIdRemoveError = false;
      bool clientByIdentityIdRemoveError = false;
      lock (listLock)
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
          if (clientsByIdentityId.ContainsKey(identityId)) clientsByIdentityId.Remove(identityId);
          else clientByIdentityIdRemoveError = Client.IsOurCheckedInClient;
        }
      }

      if (peerByInternalIdRemoveError)
        log.Error("Peer internal ID 0x{0:X16} not found in peersByInternalId list.", internalId);

      if (peerByIdentityIdRemoveError)
        log.Error("Peer Identity ID '{0}' not found in peersByIdentityId list.", identityId);

      if (clientByIdentityIdRemoveError)
        log.Error("Checked-in client Identity ID '{0}' not found in clientsByIdentityId list.", identityId);

      log.Trace("(-)");
    }

  }
}
