using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using ProfileServerProtocol;
using System.Net;
using System.Runtime.InteropServices;
using ProfileServer.Kernel;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using ProfileServer.Utils;
using ProfileServer.Data.Models;
using Iop.Profileserver;
using ProfileServerCrypto;
using System.Security.Authentication;
using ProfileServer.Data;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProfileServer.Network
{
  /// <summary>Different states of conversation between the client and the server.</summary>
  public enum ClientConversationStatus
  {
    /// <summary>Client has not initiated a conversation yet.</summary>
    NoConversation,

    /// <summary>There is an established conversation with the client, but no authentication has been done.</summary>
    ConversationStarted,

    /// <summary>There is an established conversation with the non-customer client and the verification process has already been completed.</summary>
    Verified,

    /// <summary>There is an established conversation with the client and the authentication process has already been completed.</summary>
    Authenticated,

    /// <summary>The conversation status of the client is ConversationStarted, Verified, or Authenticated.</summary>
    ConversationAny
  };

  /// <summary>
  /// Incoming client class represents any kind of TCP client that connects to one of the profile server's TCP servers.
  /// </summary>
  public class IncomingClient : ClientBase, IDisposable
  {   
    /// <summary>Maximum number of bytes that application service name can occupy.</summary>
    public const int MaxApplicationServiceNameLengthBytes = 32;

    /// <summary>Maximum number of application services that a client can have enabled within a session.</summary>
    public const int MaxClientApplicationServices = 50;

    /// <summary>Maximal number of unconfirmed messages that one relay client can send to the other one.</summary>
    public const int MaxUnfinishedRequests = 20;


    /// <summary>
    /// Length of the profile search cache expiration period in seconds.
    /// Minimal value defined by the protocol is 60 seconds.
    /// </summary>
    public const int ProfileSearchResultCacheExpirationTimeSeconds = 180;


    /// <summary>Role server assigned client identifier.</summary>
    public ulong Id;

    /// <summary>UTC time before next message has to come over the connection from this client or the profile server can close the connection due to inactivity.</summary>
    public DateTime NextKeepAliveTime;

    /// <summary>This is either TcpRoleServer.ClientKeepAliveIntervalSeconds for end user client device connections, 
    /// or TcpRoleServer.ServerKeepAliveIntervalSeconds for server to server or unknown connections.</summary>
    public int KeepAliveIntervalSeconds;


    // Client Context Section
    
    /// <summary>Current status of the conversation with the client.</summary>
    public ClientConversationStatus ConversationStatus;

    /// <summary>Client's public key.</summary>
    public byte[] PublicKey;

    /// <summary>Client's public key hash.</summary>
    public byte[] IdentityId;

    /// <summary>Random data used for client's authentication.</summary>
    public byte[] AuthenticationChallenge;

    /// <summary>true if the network client represents identity hosted by the profile server that is checked-in, false otherwise.</summary>
    public bool IsOurCheckedInClient;

    /// <summary>Client's application services available for the current session.</summary>
    public ApplicationServices ApplicationServices;

    /// <summary>
    /// If the client is connected to clAppService because of the application service call,
    /// this represents the relay object. Otherwise, this is null, including the situation 
    /// when this client is connected to clCustomerPort and is a callee of the relay, 
    /// but this connection is not the one being relayed.
    /// </summary>
    public RelayConnection Relay;



    /// <summary>Lock object to protect access to profile search result cache objects.</summary>
    private object profileSearchResultCacheLock = new object();

    /// <summary>Cache for profile search result queries.</summary>
    private List<IdentityNetworkProfileInformation> profileSearchResultCache;

    /// <summary>Original value of ProfileSearchResponse.includeThumbnailImages from the search query request.</summary>
    private bool profileSearchResultCacheIncludeImages;

    /// <summary>
    /// Timer for profile search result cache expiration. When the timer's routine is called, 
    /// the cache is deleted and cached results are no longer available.
    /// </summary>
    private Timer profileSearchResultCacheExpirationTimer;


    /// <summary>True if the client connection is from a follower server who initiated the neighborhood initialization process in this session.</summary>
    public bool NeighborhoodInitializationProcessInProgress;

    // \Client Context Section


    /// <summary>List of unprocessed requests that we expect to receive responses to mapped by Message.id.</summary>
    private Dictionary<uint, UnfinishedRequest> unfinishedRequests = new Dictionary<uint, UnfinishedRequest>();

    /// <summary>Lock for access to unfinishedRequests list.</summary>
    private object unfinishedRequestsLock = new object();


    /// <summary>Server to which the client is connected to.</summary>
    private TcpRoleServer server;

    /// <summary>Component responsible for processing logic behind incoming messages.</summary>
    private MessageProcessor messageProcessor;


    /// <summary>
    /// Creates the instance for a new TCP server client.
    /// </summary>
    /// <param name="Server">Role server that the client connected to.</param>
    /// <param name="TcpClient">TCP client class that holds the connection and allows communication with the client.</param>
    /// <param name="Id">Unique identifier of the client's connection.</param>
    /// <param name="UseTls">true if the client is connected to the TLS port, false otherwise.</param>
    /// <param name="KeepAliveIntervalSeconds">Number of seconds for the connection to this client to be without any message until the profile server can close it for inactivity.</param>
    public IncomingClient(TcpRoleServer Server, TcpClient TcpClient, ulong Id, bool UseTls, int KeepAliveIntervalSeconds):
      base(TcpClient, UseTls, Server.IdBase)
    {
      this.Id = Id;
      server = Server;
      string logPrefix = string.Format("[{0}<=>{1}|{2}] ", server.EndPoint, RemoteEndPoint, Id.ToHex());
      string logName = "ProfileServer.Network.IncomingClient";
      log = new PrefixLogger(logName, logPrefix);

      log.Trace("(UseTls:{0},KeepAliveIntervalSeconds:{1})", UseTls, KeepAliveIntervalSeconds);

      messageProcessor = new MessageProcessor(server, logPrefix);

      this.KeepAliveIntervalSeconds = KeepAliveIntervalSeconds;
      NextKeepAliveTime = DateTime.UtcNow.AddSeconds(this.KeepAliveIntervalSeconds);
    
      ConversationStatus = ClientConversationStatus.NoConversation;
      IsOurCheckedInClient = false;

      ApplicationServices = new ApplicationServices(logPrefix);

      log.Trace("(-)");
    }

    /// <summary>
    /// Reads messages from the client stream and processes them in a loop until the client disconnects 
    /// or until an action (such as a protocol violation) that leads to disconnecting of the client occurs.
    /// </summary>
    public async Task ReceiveMessageLoop()
    {
      log.Trace("()");

      try
      {
        if (UseTls) 
        {
          SslStream sslStream = (SslStream)Stream;
          await sslStream.AuthenticateAsServerAsync(Base.Configuration.TcpServerTlsCertificate, false, SslProtocols.Tls12, false);
        }

        RawMessageReader messageReader = new RawMessageReader(Stream);
        while (!server.ShutdownSignaling.IsShutdown)
        {
          RawMessageResult rawMessage = await messageReader.ReceiveMessageAsync(server.ShutdownSignaling.ShutdownCancellationTokenSource.Token);
          bool disconnect = rawMessage.Data == null;
          bool protocolViolation = rawMessage.ProtocolViolation;
          if (rawMessage.Data != null)
          {
            Message message = CreateMessageFromRawData(rawMessage.Data);
            if (message != null) disconnect = !await messageProcessor.ProcessMessageAsync(this, message);
            else protocolViolation = true;
          }

          if (protocolViolation)
          {
            await messageProcessor.SendProtocolViolation(this);
            break;
          }

          if (disconnect)
            break;
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      log.Trace("(-)");
    }




    /// <summary>
    /// Sends a request message to the client over the open network stream and stores the request to the list of unfinished requests.
    /// </summary>
    /// <param name="Message">Message to send.</param>
    /// <param name="Context">Caller's defined context to store information required for later response processing.</param>
    /// <returns>true if the connection to the client should remain open, false otherwise.</returns>
    public async Task<bool> SendMessageAndSaveUnfinishedRequestAsync(Message Message, object Context)
    {
      log.Trace("()");
      bool res = false;

      UnfinishedRequest unfinishedRequest = new UnfinishedRequest(Message, Context);
      if (AddUnfinishedRequest(unfinishedRequest))
      {
        res = await SendMessageInternalAsync(Message);
        if (res)
        {
          // If the message was sent successfully to the target, we close the connection only in case it was a protocol violation error response.
          if (Message.MessageTypeCase == Message.MessageTypeOneofCase.Response)
            res = Message.Response.Status != Status.ErrorProtocolViolation;
        }
        else RemoveUnfinishedRequest(unfinishedRequest.RequestMessage.Id);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Handles client disconnection. Destroys objects that are connected to this client 
    /// and frees the resources.
    /// </summary>
    public async Task HandleDisconnect()
    {
      log.Trace("()");

      await EmptyStream();

      if (Relay != null)
      {
        // This connection is on clAppService port. There might be the other peer still connected 
        // to this relay, so we have to make sure that other peer is disconnected as well.
        await Relay.HandleDisconnectedClient(this, true);
      }

      if (NeighborhoodInitializationProcessInProgress)
      {
        // This client is a follower server and when it disconnected there was an unfinished neighborhood initialization process.
        // We need to delete its traces from the database.
        await DeleteUnfinishedFollowerInitialization();
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Adds unfinished request to the list of unfinished requests.
    /// </summary>
    /// <param name="Request">Request to add to the list.</param>
    /// <returns>true if the function succeeds, false if the number of unfinished requests is over the limit.</returns>
    public bool AddUnfinishedRequest(UnfinishedRequest Request)
    {
      log.Trace("(Request.RequestMessage.Id:{0})", Request.RequestMessage.Id);

      bool res = false;
      lock (unfinishedRequestsLock)
      {
        if (unfinishedRequests.Count < MaxUnfinishedRequests)
        {
          unfinishedRequests.Add(Request.RequestMessage.Id, Request);
          res = true;
        }
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res, unfinishedRequests.Count);
      return res;
    }

    /// <summary>
    /// Removes unfinished request from the list of unfinished requests.
    /// </summary>
    /// <param name="Id">Identifier of the unfinished request message to remove from the list.</param>
    public bool RemoveUnfinishedRequest(uint Id)
    {
      log.Trace("(Id:{0})", Id);

      bool res = false;
      lock (unfinishedRequestsLock)
      {
        res = unfinishedRequests.Remove(Id);
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res, unfinishedRequests.Count);
      return res;
    }


    /// <summary>
    /// Finds an unfinished request message by its ID and removes it from the list.
    /// </summary>
    /// <param name="Id">Identifier of the message to find.</param>
    /// <returns>Unfinished request with the given ID, or null if no such request is in the list.</returns>
    public UnfinishedRequest GetAndRemoveUnfinishedRequest(uint Id)
    {
      log.Trace("(Id:{0})", Id);

      UnfinishedRequest res = null;
      lock (unfinishedRequestsLock)
      {
        if (unfinishedRequests.TryGetValue(Id, out res))
          unfinishedRequests.Remove(Id);
      }

      log.Trace("(-):{0},unfinishedRequests.Count={1}", res != null ? "UnfinishedRequest" : "null", unfinishedRequests.Count);
      return res;
    }


    /// <summary>
    /// Finds all unfinished request message and removes them from the list.
    /// </summary>
    /// <returns>Unfinished request messages of the client.</returns>
    public List<UnfinishedRequest> GetAndRemoveUnfinishedRequests()
    {
      log.Trace("()");

      List<UnfinishedRequest> res = new List<UnfinishedRequest>();
      lock (unfinishedRequestsLock)
      {
        res = unfinishedRequests.Values.ToList();
        unfinishedRequests.Clear();
      }

      log.Trace("(-):*.Count={0}", res.Count);
      return res;
    }


    /// <summary>
    /// Saves search results to the client session cache.
    /// </summary>
    /// <param name="SearchResults">Search results to save.</param>
    /// <param name="IncludeImages">Original value of ProfileSearchResponse.includeThumbnailImages from the search query request.</param>
    public void SaveProfileSearchResults(List<IdentityNetworkProfileInformation> SearchResults, bool IncludeImages)
    {
      log.Trace("(SearchResults.GetHashCode():{0},IncludeImages:{1})", SearchResults.GetHashCode(), IncludeImages);

      lock (profileSearchResultCacheLock)
      {
        if (profileSearchResultCacheExpirationTimer != null)
          profileSearchResultCacheExpirationTimer.Dispose();

        profileSearchResultCache = SearchResults;
        profileSearchResultCacheIncludeImages = IncludeImages;

        profileSearchResultCacheExpirationTimer = new Timer(ProfileSearchResultCacheTimerCallback, profileSearchResultCache, ProfileSearchResultCacheExpirationTimeSeconds * 1000, Timeout.Infinite);
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Gets information about the search result cache.
    /// </summary>
    /// <param name="Count">If the result cache is not empty, this is filled with the number of items in the cache.</param>
    /// <param name="IncludeImages">If the result cache is not empty, this is filled with the original value of ProfileSearchResponse.includeThumbnailImages from the search query request.</param>
    /// <returns>true if the result cache is not empty, false otherwise.</returns>
    public bool GetProfileSearchResultsInfo(out int Count, out bool IncludeImages)
    {
      log.Trace("()");

      bool res = false;
      Count = 0;
      IncludeImages = false;

      lock (profileSearchResultCacheLock)
      {
        if (profileSearchResultCache != null)
        {
          Count = profileSearchResultCache.Count;
          IncludeImages = profileSearchResultCacheIncludeImages;
          res = true;
        }
      }

      if (res) log.Trace("(-):{0},Count={1},IncludeImages={2}", res, Count, IncludeImages);
      else log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Loads search results from the client session cache.
    /// </summary>
    /// <param name="Index">Index of the first item to retrieve.</param>
    /// <param name="Count">Number of items to retrieve.</param>
    /// <returns>A copy of search results loaded from the cache or null if the required item range is not available.</returns>
    public List<IdentityNetworkProfileInformation> GetProfileSearchResults(int Index, int Count)
    {
      log.Trace("()");

      List<IdentityNetworkProfileInformation> res = null;
      lock (profileSearchResultCacheLock)
      {
        if ((profileSearchResultCache != null) && (Index + Count <= profileSearchResultCache.Count))
          res = new List<IdentityNetworkProfileInformation>(profileSearchResultCache.GetRange(Index, Count));
      }

      log.Trace("(-):*.Count={0}", res != null ? res.Count.ToString() : "N/A");
      return res;
    }

    /// <summary>
    /// Callback routine that is called once the profileSearchResultCacheExpirationTimer expires to delete cached search results.
    /// </summary>
    /// <param name="state">Search results object that was set during the timer initialization.
    /// this has to match the current search results, otherwise it means the results have been replaced already.</param>
    private void ProfileSearchResultCacheTimerCallback(object State)
    {
      List<IdentityNetworkProfileInformation> searchResults = (List<IdentityNetworkProfileInformation>)State;
      log.Trace("(State.GetHashCode():{0})", searchResults.GetHashCode());

      lock (profileSearchResultCacheLock)
      {
        if (profileSearchResultCache == searchResults)
        {
          if (profileSearchResultCacheExpirationTimer != null)
            profileSearchResultCacheExpirationTimer.Dispose();

          profileSearchResultCache = null;

          log.Debug("Search result cache has been cleaned.");
        }
        else log.Debug("Current search result cache is not the same as the cache this timer was about to delete.");
      }

      log.Trace("(-)");
    }


    /// <summary>
    /// Deletes a follower entry from the database when the follower disconnected before the neighborhood initialization process completed.
    /// </summary>
    /// <returns>true if the function succeeds, false othewise.</returns>
    private async Task<bool> DeleteUnfinishedFollowerInitialization()
    {
      log.Trace("()");

      bool res = false;
      byte[] followerId = IdentityId;
      bool success = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.FollowerLock, UnitOfWork.NeighborhoodActionLock };
        using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
        {
          try
          {
            bool saveDb = false;

            // Delete the follower itself.
            Follower existingFollower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == followerId)).FirstOrDefault();
            if (existingFollower != null)
            {
              unitOfWork.FollowerRepository.Delete(existingFollower);
              log.Debug("Follower ID '{0}' will be removed from the database.", followerId.ToHex());
              saveDb = true;
            }
            else log.Error("Follower ID '{0}' not found.", followerId.ToHex());

            // Delete all its neighborhood actions.
            List<NeighborhoodAction> actions = (await unitOfWork.NeighborhoodActionRepository.GetAsync(a => a.ServerId == followerId)).ToList();
            foreach (NeighborhoodAction action in actions)
            {
              if (action.IsProfileAction())
              {
                log.Debug("Action ID {0}, type {1}, serverId '{2}' will be removed from the database.", action.Id, action.Type, followerId.ToHex());
                unitOfWork.NeighborhoodActionRepository.Delete(action);
                saveDb = true;
              }
            }


            if (saveDb)
            { 
              await unitOfWork.SaveThrowAsync();
              transaction.Commit();
            }

            success = true;
            res = true;
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }

          if (!success)
          {
            log.Warn("Rolling back transaction.");
            unitOfWork.SafeTransactionRollback(transaction);
          }

          unitOfWork.ReleaseLock(lockObjects);
        }
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>Signals whether the instance has been disposed already or not.</summary>
    private bool disposed = false;

    /// <summary>Prevents race condition from multiple threads trying to dispose the same client instance at the same time.</summary>
    private object disposingLock = new object();

    /// <summary>
    /// Disposes the instance of the class if it has not been disposed yet and <paramref name="Disposing"/> is set.
    /// </summary>
    /// <param name="Disposing">Indicates whether the method was invoked from the IDisposable.Dispose implementation or from the finalizer.</param>
    protected override void Dispose(bool Disposing)
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
        base.Dispose(Disposing);

        lock (profileSearchResultCacheLock)
        {
          if (profileSearchResultCacheExpirationTimer != null)
            profileSearchResultCacheExpirationTimer.Dispose();

          profileSearchResultCacheExpirationTimer = null;
          profileSearchResultCache = null;
        }

        // Relay is not disposed here as it is being destroyed using HandleDisconnect method
        // which is called by the RoleServer in ClientHandlerAsync method.
      }
    }
  }
}
