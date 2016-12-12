using Iop.Profileserver;
using Microsoft.EntityFrameworkCore.Storage;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using ProfileServer.Kernel;
using ProfileServer.Utils;
using ProfileServerCrypto;
using ProfileServerProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileServer.Network
{
  /// <summary>
  /// This component executes planned actions from NeighborhoodAction database table, which forms a queue of actions 
  /// related to neighbors and followers. Actions that go in the same directions using the same target server 
  /// must be executed in serial manner. Actions that work with different target servers or that goes in opposite 
  /// directions can be executed in parallel.
  /// <para>
  /// A timer is installed that causes the action queue to be checked every so often (see CheckActionListTimerInterval).
  /// In combination to the timer, other components do call Signal method to enforce an immediate check of the queue.
  /// </para>
  /// </summary>
  public class NeighborhoodActionProcessor : Component
  {
    private static NLog.Logger log = NLog.LogManager.GetLogger("ProfileServer.Network.NeighborhoodActionProcessor");

    /// <summary>Interval for role servers inactive client connection checks.</summary>
    private const int CheckActionListTimerInterval = 60000;


    /// <summary>Timer that invokes checks of the list of neighborhood actions.</summary>
    private static Timer checkActionListTimer;

    /// <summary>Event that triggers checking the list of neighborhood actions.</summary>
    private static AutoResetEvent checkActionListEvent = new AutoResetEvent(false);


    /// <summary>Event that is set when actionListHandlerThread is not running.</summary>
    private ManualResetEvent actionListHandlerThreadFinished = new ManualResetEvent(true);

    /// <summary>Thread that is responsible for processing neighborhood actions.</summary>
    private Thread actionListHandlerThread;

    /// <summary>Profile serever's primary interface port.</summary>
    private uint primaryPort;

    /// <summary>Profile server neighbors interface port.</summary>
    private uint srNeighborPort;


    public override bool Init()
    {
      log.Info("()");

      bool res = false;
      try
      {
        Server serverComponent = (Server)Base.ComponentDictionary["Network.Server"];
        List<TcpRoleServer> roleServers = serverComponent.GetRoleServers();
        foreach (TcpRoleServer roleServer in roleServers)
        {
          if (roleServer.Roles.HasFlag(ServerRole.Primary))
            primaryPort = (uint)roleServer.EndPoint.Port;

          if (roleServer.Roles.HasFlag(ServerRole.ServerNeighbor))
            srNeighborPort = (uint)roleServer.EndPoint.Port;
        }

        checkActionListTimer = new Timer(CheckActionListTimerCallback, null, CheckActionListTimerInterval, CheckActionListTimerInterval);

        actionListHandlerThread = new Thread(new ThreadStart(ActionListHandlerThread));
        actionListHandlerThread.Start();

        res = true;
        Initialized = true;
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (!res)
      {
        ShutdownSignaling.SignalShutdown();

        if ((actionListHandlerThread != null) && !actionListHandlerThreadFinished.WaitOne(10000))
          log.Error("Action list handler thread did not terminated in 10 seconds.");

        if (checkActionListTimer != null) checkActionListTimer.Dispose();
        checkActionListTimer = null;
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      if (checkActionListTimer != null) checkActionListTimer.Dispose();
      checkActionListTimer = null;

      if ((actionListHandlerThread != null) && !actionListHandlerThreadFinished.WaitOne(10000))
        log.Error("Action list handler thread did not terminated in 10 seconds.");

      log.Info("(-)");
    }


    /// <summary>
    /// Signals check action list event.
    /// </summary>
    public void Signal()
    {
      log.Trace("()");

      checkActionListEvent.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Callback routine of checkActionListTimer.
    /// We simply set an event to be handled by action list handler thread, not to occupy the timer for a long time.
    /// </summary>
    /// <param name="State">Not used.</param>
    private void CheckActionListTimerCallback(object State)
    {
      log.Trace("()");

      checkActionListEvent.Set();

      log.Trace("(-)");
    }


    /// <summary>
    /// Thread that is responsible for maintenance tasks invoked by event timers.
    /// </summary>
    private void ActionListHandlerThread()
    {
      log.Info("()");

      actionListHandlerThreadFinished.Reset();

      while (!ShutdownSignaling.IsShutdown)
      {
        log.Info("Waiting for event.");

        WaitHandle[] handles = new WaitHandle[] { ShutdownSignaling.ShutdownEvent, checkActionListEvent };

        int index = WaitHandle.WaitAny(handles);
        if (handles[index] == ShutdownSignaling.ShutdownEvent)
        {
          log.Info("Shutdown detected.");
          break;
        }

        if (handles[index] == checkActionListEvent)
        {
          log.Trace("checkActionListEvent activated.");
          CheckActionList();
        }
      }

      actionListHandlerThreadFinished.Set();

      log.Info("(-)");
    }


    /// <summary>
    /// Loads list of actions from the database and executes actions that can be executed.
    /// </summary>
    private void CheckActionList()
    {
      log.Trace("()");

      try
      {
        while (!ShutdownSignaling.IsShutdown)
        {
          NeighborhoodAction action = LoadNextAction();
          if (action != null)
          {
            ProcessActionAsync(action);
          }
          else
          {
            log.Trace("No neighborhood action to process.");
            break;
          }
        }
      }
      catch (Exception e)
      {
        log.Error("Exception occurred (and rethrowing): {0}", e.ToString());
        Thread.Sleep(5000);
        throw e;
      }

      log.Trace("(-)");
    }

    /// <summary>
    /// Processes a neighborhood action and either removes it from the database or schedules its retry.
    /// </summary>
    /// <param name="Action">Action to process.</param>
    private async void ProcessActionAsync(NeighborhoodAction Action)
    {
      LogDiagnosticContext.Start();

      log.Trace("(Action.Id:{0})", Action.Id);

      bool removeAction = await ExecuteActionAsync(Action);
      if (removeAction)
      {
        // If the action was processed successfully, we remove it from the database.
        // Otherwise its ExecuteAfter flag is set to the future and the action will 
        // attempt to be processed again later.
        // We signal the event to have the action queue checked, because this action 
        // that just finished might have blocked another action in the queue.
        if (await RemoveActionAsync(Action.Id)) Signal();
        else log.Error("Failed to remove action ID {0} from the database.", Action.Id);
      }
      else log.Warn("Processing of action ID {0} failed.", Action.Id);

      log.Trace("(-)");

      LogDiagnosticContext.Stop();
    }

    /// <summary>
    /// Loads next neighborhood action to process from the database.
    /// <para>
    /// Next action to process is an action that is not blocked by ExecuteAfter condition and has the lowest ID.
    /// An action is blocked by ExecuteAfter condition if its ExecuteAfter is greater than current time, 
    /// or if any action with lower ID and the same neighbor/follower ID has ExecuteAfter greater than current time.
    /// </para>
    /// </summary>
    /// <returns>Next action to process or null if there is no action to process now.</returns>
    private NeighborhoodAction LoadNextAction()
    {
      log.Trace("()");

      NeighborhoodAction res = null;

      // Network IDs of servers which profile actions can't be executed because a blocking action with future ExecuteAfter exists.
      HashSet<byte[]> profileActionsLockedIds = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);
      
      // Network IDs of servers which server actions can't be executed because a blocking action with future ExecuteAfter exists.
      HashSet<byte[]> serverActionsLockedIds = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DateTime now = DateTime.UtcNow;
        DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
        unitOfWork.AcquireLock(lockObject);
        try
        {
          NeighborhoodAction actionToProcess = null;
          List<NeighborhoodAction> actions = unitOfWork.NeighborhoodActionRepository.Get(null, q => q.OrderBy(a => a.Id)).ToList();
          foreach (NeighborhoodAction action in actions)
          {
            bool isProfileAction = action.IsProfileAction();

            bool isLocked = (isProfileAction && profileActionsLockedIds.Contains(action.ServerId))
              || (!isProfileAction && serverActionsLockedIds.Contains(action.ServerId));

            log.Trace("Action type is {0}, isProfileAction is {1}, isLocked is {2}, execute after time is {3}.", action.Type, isProfileAction, isLocked, action.ExecuteAfter != null ? action.ExecuteAfter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null");

            if (!isLocked)
            {
              if ((action.ExecuteAfter == null) || (action.ExecuteAfter <= now))
              {
                actionToProcess = action;
                break;
              }
              else
              {
                log.Trace("Action ID {0} can't be executed because its ExecuteAfter ({1}) is in the future.", action.Id, action.ExecuteAfter.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                if (isProfileAction) profileActionsLockedIds.Add(action.ServerId);
                else serverActionsLockedIds.Add(action.ServerId);
              }
            }
            else log.Trace("Action ID {0} can't be executed because it is locked by other action on the server ID '{1}'.", action.Id, action.ServerId.ToHex());
          }

          if (actionToProcess != null)
          {
            // If there is action to process, we set its ExecuteAfter value to the future, so that it is not selected next time 
            // this function is executed again before the action is processed.
            //
            // We MUST make sure, the action is processed before ExecuteAfter, otherwise it will be picked up again for processing!
            actionToProcess.ExecuteAfter = now.AddSeconds(600);
            unitOfWork.NeighborhoodActionRepository.Update(actionToProcess);
            unitOfWork.SaveThrow();

            res = actionToProcess;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      if (res != null) log.Trace("(-):*.Id={0}", res.Id);
      else log.Trace("(-):null");
      return res;
    }


    /// <summary>
    /// Removes an action from the database.
    /// </summary>
    /// <param name="ActionToUpdate">Identifier of the action to remove.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> RemoveActionAsync(int ActionId)
    {
      log.Trace("()");

      bool res = false;

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          NeighborhoodAction action = (await unitOfWork.NeighborhoodActionRepository.GetAsync(a => a.Id == ActionId)).FirstOrDefault();
          if (action != null)
          {
            unitOfWork.NeighborhoodActionRepository.Delete(action);
            log.Trace("Action ID {0} will be removed from database.", ActionId);

            if (await unitOfWork.SaveAsync())
              res = true;
          }
          else log.Warn("Unable to find action ID {0} in the database.", ActionId);
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }

    /// <summary>
    /// Executes a neighborhood action.
    /// </summary>
    /// <param name="Action">Action to execute.</param>
    private async Task<bool> ExecuteActionAsync(NeighborhoodAction Action)
    {
      log.Trace("(Action.Id:{0},Action.Type:{1})", Action.Id, Action.Type);

      bool res = false;

      switch (Action.Type)
      {
        case NeighborhoodActionType.AddNeighbor:
          res = await NeighborhoodInitializationProcess(Action.ServerId, Action.ExecuteAfter.Value);
          break;

        case NeighborhoodActionType.RemoveNeighbor:
          res = await NeighborhoodRemoveNeighbor(Action.ServerId);
          break;


        case NeighborhoodActionType.AddProfile:
        case NeighborhoodActionType.ChangeProfile:
        case NeighborhoodActionType.RemoveProfile:
        case NeighborhoodActionType.RefreshProfiles:
          res = await NeighborhoodProfileUpdate(Action.ServerId, Action.TargetIdentityId, Action.Type, Action.AdditionalData);
          break;

        case NeighborhoodActionType.InitializationProcessInProgress:
          // If InitializationProcessInProgress action can be executed, it means the follower finished the initialization process.
          // It is now safe to remove the action and proceed with the queue.
          res = true;
          break;

        default:
          log.Error("Invalid action type {0}.", Action.Type);
          break;
      }

      if (res) log.Debug("Action ID {0} processed successfully.", Action.Id);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Connects to a neighbor profile server and performs a neighborhood initialization process,
    /// which means that it asks the neighbor to share its profile server database.
    /// </summary>
    /// <param name="NeighborId">Network identifer of the neighbor server.</param>
    /// <param name="MustFinishBefore">Time before which the initialization process must be completed.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> NeighborhoodInitializationProcess(byte[] NeighborId, DateTime MustFinishBefore)
    {
      log.Trace("(NeighborId:'{0}',MustFinishBefore:{1})", NeighborId.ToHex(), MustFinishBefore.ToString("yyyy-MM-dd HH:mm:ss"));

      bool res = false;

      // We MUST finish before MustFinishBefore time, so to be sure, we will terminate the process 
      // if we find ourselves running 90 seconds before that time. We have 60 seconds read timeout 
      // on the stream, so in the worst case, we should have 30 seconds reserve.
      DateTime processStart = DateTime.UtcNow;
      DateTime safeDeadline = MustFinishBefore.AddSeconds(-90);
      log.Trace("Setting up safe deadline to {0}.", safeDeadline.ToString("yyyy-MM-dd HH:mm:ss"));

      IPEndPoint endPoint = await GetNeighborServerContact(NeighborId);
      if (endPoint != null)
      {
        using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
        {
          Dictionary<byte[], NeighborIdentity> identityDatabase = new Dictionary<byte[], NeighborIdentity>(StructuralEqualityComparer<byte[]>.Default);
          client.Context = identityDatabase;

          if (await client.ConnectAndVerifyIdentityAsync())
          {
            if (client.MatchServerId(NeighborId))
            {
              if (await client.StartNeighborhoodInitializationAsync(primaryPort, srNeighborPort))
              {
                bool error = false;
                bool done = false;
                while (!done && !error)
                {
                  if (DateTime.UtcNow > safeDeadline)
                  {
                    log.Warn("Intialization process took too long, the safe deadline {0} has been reached.", safeDeadline.ToString("yyyy-MM-dd HH:mm:ss"));
                    error = true;
                    break;
                  }

                  Message requestMessage = await client.ReceiveMessageAsync();
                  if (requestMessage != null)
                  {
                    Message responseMessage = client.MessageBuilder.CreateErrorProtocolViolationResponse(requestMessage);

                    if (requestMessage.MessageTypeCase == Message.MessageTypeOneofCase.Request)
                    {
                      Request request = requestMessage.Request;
                      if (request.ConversationTypeCase == Request.ConversationTypeOneofCase.ConversationRequest)
                      {
                        ConversationRequest conversationRequest = request.ConversationRequest;
                        log.Trace("Conversation request type '{0}' received.", conversationRequest.RequestTypeCase);
                        switch (conversationRequest.RequestTypeCase)
                        {
                          case ConversationRequest.RequestTypeOneofCase.NeighborhoodSharedProfileUpdate:
                            responseMessage = await ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync(client, requestMessage);
                            break;

                          case ConversationRequest.RequestTypeOneofCase.FinishNeighborhoodInitialization:
                            responseMessage = await ProcessMessageFinishNeighborhoodInitializationRequestAsync(client, requestMessage);
                            done = true;
                            break;

                          default:
                            log.Error("Invalid conversation request type '{0}' received.", conversationRequest.RequestTypeCase);
                            error = true;
                            break;
                        }
                      }
                      else
                      {
                        log.Warn("Invalid conversation type '{0}' received.", request.ConversationTypeCase);
                        error = true;
                      }
                    }
                    else
                    {
                      log.Warn("Invalid message type '{0}' received, expected Request.", requestMessage.MessageTypeCase);
                      error = true;
                    }

                    // Send response to neighbor.
                    if (!await client.SendMessageAsync(responseMessage))
                    {
                      log.Warn("Unable to send response to neighbor.");
                      error = true;
                      break;
                    }
                  }
                  else
                  {
                    log.Warn("Connection has been terminated, initialization process has not been completed.");
                    error = true;
                  }
                }

                res = !error;
              }
              else 
              {
                if (client.LastResponseStatus == Status.ErrorBusy)
                {
                  // Target server is busy at the moment and does not want to talk to us about neighborhood initialization.
                  log.Debug("Neighbor ID '{0}' is busy now, let's try later.", NeighborId.ToHex());
                }
                else log.Warn("Starting the intialization process failed with neighbor ID '{0}'.", NeighborId.ToHex());
              }
            }
            else log.Warn("Server identity differs from expected ID '{0}'.", NeighborId.ToHex());
          }
          else log.Warn("Unable to initiate conversation with neighbor ID '{0}' on address {1}.", NeighborId.ToHex(), endPoint);
        }
      }
      else log.Error("Unable to find neighbor ID '{0}' IP and port information.", NeighborId.ToHex());

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Obtains IP address and srNeighbor port from neighbor server's network identifier.
    /// </summary>
    /// <param name="NeighborId">Network identifer of the neighbor server.</param>
    /// <returns>End point description or null if the function fails.</returns>
    private async Task<IPEndPoint> GetNeighborServerContact(byte[] NeighborId)
    {
      log.Trace("(NeighborId:'{0}')", NeighborId.ToHex());

      IPEndPoint res = null;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = null;
        bool unlock = false;
        try
        {
          Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.Id == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            IPAddress addr = IPAddress.Parse(neighbor.IpAddress);
            if (neighbor.SrNeighborPort != null)
            {
              res = new IPEndPoint(addr, neighbor.SrNeighborPort.Value);
            }
            else
            {
              // We do not know srNeighbor port of this neighbor yet, we have to connect to its primary port and get that information.
              int srNeighborPort = await GetServerRolePortFromPrimaryPort(addr, neighbor.PrimaryPort, ServerRoleType.SrNeighbor);
              if (srNeighborPort != 0)
              {
                lockObject = UnitOfWork.NeighborLock;
                await unitOfWork.AcquireLockAsync(lockObject);
                unlock = true;

                neighbor.SrNeighborPort = srNeighborPort;
                if (!await unitOfWork.SaveAsync())
                  log.Error("Unable to save new srNeighbor port information {0} of neighbor ID '{1}' to the database.", srNeighborPort, NeighborId.ToHex());

                res = new IPEndPoint(addr, srNeighborPort);
              }
              else log.Error("Unable to obtain srNeighbor port from primary port of neighbor ID '{0}'.", NeighborId.ToHex());
            }
          }
          else log.Error("Unable to find neighbor ID '{0}' in the database.", NeighborId.ToHex());
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (unlock) unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res != null ? res.ToString() : "null");
      return res;
    }


    /// <summary>
    /// Obtains IP address and srNeighbor port from follower server's network identifier.
    /// </summary>
    /// <param name="FollowerId">Network identifer of the follower server.</param>
    /// <returns>End point description or null if the function fails.</returns>
    private async Task<IPEndPoint> GetFollowerServerContact(byte[] FollowerId)
    {
      log.Trace("(FollowerId:'{0}')", FollowerId.ToHex());

      IPEndPoint res = null;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = null;
        bool unlock = false;
        try
        {
          Follower follower = (await unitOfWork.FollowerRepository.GetAsync(f => f.Id == FollowerId)).FirstOrDefault();
          if (follower != null)
          {
            IPAddress addr = IPAddress.Parse(follower.IpAddress);
            if (follower.SrNeighborPort != null)
            {
              res = new IPEndPoint(addr, follower.SrNeighborPort.Value);
            }
            else
            {
              // We do not know srNeighbor port of this follower yet, we have to connect to its primary port and get that information.
              int srNeighborPort = await GetServerRolePortFromPrimaryPort(addr, follower.PrimaryPort, ServerRoleType.SrNeighbor);
              if (srNeighborPort != 0)
              {
                lockObject = UnitOfWork.FollowerLock;
                await unitOfWork.AcquireLockAsync(lockObject);
                unlock = true;

                follower.SrNeighborPort = srNeighborPort;
                if (!await unitOfWork.SaveAsync())
                  log.Error("Unable to save new srNeighbor port information {0} of follower ID '{1}' to the database.", srNeighborPort, FollowerId.ToHex());

                res = new IPEndPoint(addr, srNeighborPort);
              }
              else log.Error("Unable to obtain srNeighbor port from primary port of follower ID '{0}'.", FollowerId.ToHex());
            }
          }
          else log.Error("Unable to find follower ID '{0}' in the database.", FollowerId.ToHex());
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }

        if (unlock) unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res != null ? res.ToString() : "null");
      return res;
    }


    /// <summary>
    /// Connects to a profile server's primary port and attempts to get information on which port is a certain role served. 
    /// </summary>
    /// <param name="IpAddress">IP address of the profile server.</param>
    /// <param name="PrimaryPort">Primary interface port of the profile server.</param>
    /// <param name="Role">Role which port is being searched.</param>
    /// <returns>Port on which the given role is being served, or 0 if the port is not known.</returns>
    public async Task<int> GetServerRolePortFromPrimaryPort(IPAddress IpAddress, int PrimaryPort, ServerRoleType Role)
    {
      log.Trace("(IpAddress:{0},PrimaryPort:{1},Role:{2})", IpAddress, PrimaryPort, Role);

      int res = 0;
      using (OutgoingClient client = new OutgoingClient(new IPEndPoint(IpAddress, PrimaryPort), false, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
      {
        if (await client.ConnectAsync())
        {
          ListRolesResponse listRoles = await client.SendListRolesRequest();
          if (listRoles != null)
          {
            foreach (Iop.Profileserver.ServerRole serverRole in listRoles.Roles)
            {
              if (serverRole.Role == Role)
              {
                res = (int)serverRole.Port;
                break;
              }
            }
          }
          else log.Debug("Unable to get list of server roles from server {0}.", client.RemoteEndPoint);
        }
        else log.Debug("Unable to connect to {0}.", client.RemoteEndPoint);
      }

      log.Trace("(-):{0}", res);
      return res;
    }



    /// <summary>
    /// A former neighbor server was removed from a neighborhood. We delete it and all its shared profiles from our database.
    /// Neighbor can be removed either by message from LBN server, or due to its expiration.
    /// </summary>
    /// <param name="NeighborId">Network identifier of the former neighbor server.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> NeighborhoodRemoveNeighbor(byte[] NeighborId)
    {
      log.Trace("(NeighborId:'{0}')", NeighborId.ToHex());

      bool res = false;
      List<Guid> imagesToDelete = new List<Guid>();
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        bool success = false;

        // Disable change tracking for faster multiple deletes.
        unitOfWork.Context.ChangeTracker.AutoDetectChangesEnabled = false;

        // Delete neighbor from the list of neighbors.
        DatabaseLock lockObject = UnitOfWork.NeighborLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.Id == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            unitOfWork.NeighborRepository.Delete(neighbor);
            await unitOfWork.SaveThrowAsync();
            success = true;
            log.Debug("Neighbor ID '{0}' deleted from database.", NeighborId.ToHex());
          }
          else
          {
            log.Warn("Neighbor ID '{0}' not found.", NeighborId.ToHex());
            // If the neighbor does not exist, we set success to true as the result of the operation is as we want it 
            // and we gain nothing by trying to repeat the action later.
            success = true;
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred: {0}", e.ToString());
        }
        unitOfWork.ReleaseLock(lockObject);

        // Delete neighbor's profiles from the database.
        if (success)
        {
          success = false;

          lockObject = UnitOfWork.NeighborIdentityLock;
          await unitOfWork.AcquireLockAsync(lockObject);
          try
          {
            List<NeighborIdentity> identities = (await unitOfWork.NeighborIdentityRepository.GetAsync(i => i.HostingServerId == NeighborId)).ToList();
            if (identities.Count > 0)
            {
              log.Debug("There are {0} identities of removed neighbor ID '{1}'.", identities.Count, NeighborId.ToHex());
              foreach (NeighborIdentity identity in identities)
              {
                if (identity.ProfileImage != null) imagesToDelete.Add(identity.ProfileImage.Value);
                if (identity.ThumbnailImage != null) imagesToDelete.Add(identity.ThumbnailImage.Value);

                unitOfWork.NeighborIdentityRepository.Delete(identity);
              }

              await unitOfWork.SaveThrowAsync();
              success = true;
              log.Debug("{0} identities hosted on neighbor ID '{1}' deleted from database.", identities.Count, NeighborId.ToHex());
            }
            else
            {
              log.Trace("No profiles hosted on neighbor ID '{0}' found.", NeighborId.ToHex());
              success = true;
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }

          unitOfWork.ReleaseLock(lockObject);
        }

        if (success)
        {
          success = false;
          lockObject = UnitOfWork.NeighborhoodActionLock;
          await unitOfWork.AcquireLockAsync(lockObject);
          try
          {
            List<NeighborhoodAction> actions = unitOfWork.NeighborhoodActionRepository.Get(a => a.ServerId == NeighborId).ToList();
            if (actions.Count > 0)
            {
              log.Debug("There are {0} neighborhood actions for removed neighbor ID '{1}'.", actions.Count, NeighborId.ToHex());
              foreach (NeighborhoodAction action in actions)
                unitOfWork.NeighborhoodActionRepository.Delete(action);

              await unitOfWork.SaveThrowAsync();
              success = true;
              log.Debug("{0} neighborhood actions for neighbor ID '{1}' deleted from database.", actions.Count, NeighborId.ToHex());
            }
            else
            {
              log.Debug("No neighborhood actions for neighbor ID '{0}' found.", NeighborId.ToHex());
              success = true;
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }

          unitOfWork.ReleaseLock(lockObject);
        }

        res = success;
      }


      foreach (Guid guid in imagesToDelete)
        if (!ImageHelper.DeleteImageFile(guid))
          log.Warn("Unable to delete image file of image GUID '{0}'.", guid);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Connects to a follower profile server and propagates an update regarding changes in a profile of a hosted identity.
    /// </summary>
    /// <param name="FollowerId">Network identifer of the follower server.</param>
    /// <param name="IdentityId">Identifier of the identity which profile has changed.</param>
    /// <param name="ActionType">Type of profile change action.</param>
    /// <param name="AdditionalData">Additional action data or null if action has no additional data.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> NeighborhoodProfileUpdate(byte[] FollowerId, byte[] IdentityId, NeighborhoodActionType ActionType, string AdditionalData)
    {
      log.Trace("(FollowerId:'{0}',IdentityId:'{1}',ActionType:{2},AdditionalData:'{3}')", FollowerId.ToHex(), IdentityId.ToHex(), ActionType, AdditionalData);

      bool res = false;

      // First, we find the target server, to which we want to send the update.
      IPEndPoint endPoint = await GetFollowerServerContact(FollowerId);
      if (endPoint != null)
      {
        // Then we construct the update message.
        SharedProfileUpdateItem updateItem = null;
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          try
          {
            switch (ActionType)
            {
              case NeighborhoodActionType.AddProfile:
                {
                  HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
                  if (identity != null)
                  {
                    byte[] thumbnailImage = await identity.GetThumbnailImageDataAsync();
                    GpsLocation location = identity.GetInitialLocation();
                    SharedProfileAddItem item = new SharedProfileAddItem()
                    {
                      Version = ProtocolHelper.ByteArrayToByteString(identity.Version),
                      IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(identity.PublicKey),
                      Name = identity.Name,
                      Type = identity.Type,
                      SetThumbnailImage = thumbnailImage != null,
                      Latitude = location.GetLocationTypeLatitude(),
                      Longitude = location.GetLocationTypeLongitude(),
                      ExtraData = identity.ExtraData
                    };
                    if (item.SetThumbnailImage)
                      item.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(thumbnailImage);

                    updateItem = new SharedProfileUpdateItem();
                    updateItem.Add = item;
                  }
                  else log.Error("Unable to find hosted identity ID '{0}'.", IdentityId.ToHex());

                  break;
                }

              case NeighborhoodActionType.ChangeProfile:
                {
                  HostedIdentity identity = (await unitOfWork.HostedIdentityRepository.GetAsync(i => i.IdentityId == IdentityId)).FirstOrDefault();
                  if (identity != null)
                  {
                    SharedProfileChangeItem additionalDataItem = SharedProfileChangeItem.Parser.ParseJson(AdditionalData);

                    byte[] thumbnailImage = additionalDataItem.SetThumbnailImage ? await identity.GetThumbnailImageDataAsync() : null;
                    GpsLocation location = identity.GetInitialLocation();
                    SharedProfileChangeItem item = new SharedProfileChangeItem()
                    {
                      IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(identity.IdentityId),

                      SetVersion = additionalDataItem.SetVersion,
                      SetName = additionalDataItem.SetName,
                      SetLocation = additionalDataItem.SetLocation,
                      SetExtraData = additionalDataItem.SetExtraData,

                      // Thumbnail image could have been erased since the action was created.
                      SetThumbnailImage = thumbnailImage != null
                    };

                    if (item.SetVersion) item.Version = ProtocolHelper.ByteArrayToByteString(identity.Version);
                    if (item.SetName) item.Name = identity.Name;
                    if (item.SetThumbnailImage) item.ThumbnailImage = ProtocolHelper.ByteArrayToByteString(thumbnailImage);
                    if (item.SetLocation)
                    {
                      item.Latitude = location.GetLocationTypeLatitude();
                      item.Longitude = location.GetLocationTypeLongitude();
                    }
                    if (item.SetExtraData) item.ExtraData = identity.ExtraData;

                    updateItem = new SharedProfileUpdateItem();
                    updateItem.Change = item;
                  }
                  else log.Error("Unable to find hosted identity ID '{0}'.", IdentityId.ToHex());
                  break;
                }

              case NeighborhoodActionType.RemoveProfile:
                {
                  SharedProfileDeleteItem item = new SharedProfileDeleteItem()
                  {
                    IdentityNetworkId = ProtocolHelper.ByteArrayToByteString(IdentityId)
                  };

                  updateItem = new SharedProfileUpdateItem();
                  updateItem.Delete = item;
                  break;
                }


              case NeighborhoodActionType.RefreshProfiles:
                {
                  SharedProfileRefreshAllItem item = new SharedProfileRefreshAllItem();

                  updateItem = new SharedProfileUpdateItem();
                  updateItem.Refresh = item;
                  break;
                }


              default:
                log.Error("Invalid action type '{0}'.", ActionType);
                break;
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
          }
        }

        if (updateItem != null)
        {
          using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
          {
            // If we successfully constructed the update for the follower server, we connect to it and send it.
            if (await client.ConnectAndVerifyIdentityAsync())
            {
              if (client.MatchServerId(FollowerId))
              {
                List<SharedProfileUpdateItem> updateList = new List<SharedProfileUpdateItem>() { updateItem };
                Message updateMessage = client.MessageBuilder.CreateNeighborhoodSharedProfileUpdateRequest(updateList);
                if (await client.SendNeighborhoodSharedProfileUpdate(updateMessage))
                {
                  if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Refresh)
                  {
                    // If database update fails, the follower server will just be refreshed again later.
                    await UpdateFollowerLastRefreshTime(FollowerId);
                  }

                  res = true;
                }
                else log.Error("Sending update to follower ID '{0}' failed.", FollowerId.ToHex());
              }
              else log.Error("Server identity differs from expected ID '{0}'.", FollowerId.ToHex());
            }
            else log.Warn("Unable to initiate conversation with follower ID '{0}' on address {1}.", FollowerId.ToHex(), endPoint);
          }
        }
      }
      else log.Error("Unable to find follower ID '{0}' IP and port information.", FollowerId.ToHex());

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes NeighborhoodSharedProfileUpdateRequest message from client sent during neighborhood initialization process.
    /// <para>Saves incoming profiles information to the temporary memory location and profiles images to the temporary disk directory.</para>
    /// </summary>
    /// <param name="Client">Client that received the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the remote peer.</returns>
    private async Task<Message> ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync(OutgoingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      MessageBuilder messageBuilder = Client.MessageBuilder;
      Message res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

      NeighborhoodSharedProfileUpdateRequest neighborhoodSharedProfileUpdateRequest = RequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate;

      bool error = false;
      int itemIndex = 0;
      foreach (SharedProfileUpdateItem updateItem in neighborhoodSharedProfileUpdateRequest.Items)
      {
        if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add)
        {
          SharedProfileAddItem addItem = updateItem.Add;
          Message errorResponse;
          Dictionary<byte[], NeighborIdentity> identityDatabase = (Dictionary<byte[], NeighborIdentity>)Client.Context;
          if (ValidateInMemorySharedProfileAddItem(addItem, itemIndex, identityDatabase, Client.MessageBuilder, RequestMessage, out errorResponse))
          {
            Guid? thumbnailImageGuid = null;
            byte[] thumbnailImageData = addItem.SetThumbnailImage ? addItem.ThumbnailImage.ToByteArray() : null;
            if (thumbnailImageData != null)
            {
              thumbnailImageGuid = Guid.NewGuid();
              if (!await ImageHelper.SaveTempImageDataAsync(thumbnailImageGuid.Value, thumbnailImageData))
              {
                log.Error("Unable to save image GUID '{0}' data to temporary directory.", thumbnailImageGuid.Value);
                error = true;
                break;
              }
            }

            byte[] pubKey = addItem.IdentityPublicKey.ToByteArray();
            byte[] id = Crypto.Sha256(pubKey);
            GpsLocation location = new GpsLocation(addItem.Latitude, addItem.Longitude);
            NeighborIdentity identity = new NeighborIdentity()
            {
              IdentityId = id,
              HostingServerId = Client.ServerId,
              PublicKey = pubKey,
              Version = addItem.Version.ToByteArray(),
              Name = addItem.Name,
              Type = addItem.Type,
              InitialLocationLatitude = location.Latitude,
              InitialLocationLongitude = location.Longitude,
              ExtraData = addItem.ExtraData,
              ExpirationDate = null,
              ProfileImage = null,
              ThumbnailImage = thumbnailImageGuid
            };
            identityDatabase.Add(identity.IdentityId, identity);
          }
          else
          {
            res = errorResponse;
            error = true;
            break;
          }
        }
        else
        {
          log.Warn("Invalid profile update item action type '{0}' received during the neighborhood initialization process.", updateItem.ActionTypeCase);
          res = messageBuilder.CreateErrorInvalidValueResponse(RequestMessage, itemIndex.ToString() + ".actionType");
          error = true;
          break;
        }
        itemIndex++;
      }

      if (!error) res = messageBuilder.CreateNeighborhoodSharedProfileUpdateResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Validates incoming SharedProfileAddItem update item.
    /// </summary>
    /// <param name="AddItem">Update item to validate.</param>
    /// <param name="Index">Index of the update item in the message.</param>
    /// <param name="IdentityDatabase">In-memory temporary database of identities hosted on the neighbor server that were already received and processed.</param>
    /// <param name="MessageBuilder">Client's network message builder.</param>
    /// <param name="RequestMessage">Full request message received by the client.</param>
    /// <param name="ErrorResponse">If the validation fails, this is filled with response message to be sent to the neighbor.</param>
    /// <returns>true if the validation is successful, false otherwise.</returns>
    private bool ValidateInMemorySharedProfileAddItem(SharedProfileAddItem AddItem, int Index, Dictionary<byte[], NeighborIdentity> IdentityDatabase, MessageBuilder MessageBuilder, Message RequestMessage, out Message ErrorResponse)
    {
      log.Trace("(Index:{0})", Index);
      
      bool res = false;
      ErrorResponse = null;

      string details = null;
      if (IdentityDatabase.Count >= IdentityBase.MaxHostedIdentities)
      {
        log.Debug("Target server already sent too many profiles.");
        details = "add";
      }

      if (details == null)
      {
        SemVer version = new SemVer(AddItem.Version);

        // Currently only supported version is 1.0.0.
        if (!version.Equals(SemVer.V100))
        {
          log.Debug("Unsupported version '{0}'.", version);
          details = "add.version";
        }
      }

      if (details == null)
      {
        byte[] pubKey = AddItem.IdentityPublicKey.ToByteArray();
        bool pubKeyValid = (0 < pubKey.Length) && (pubKey.Length <= IdentityBase.MaxPublicKeyLengthBytes);
        if (pubKeyValid)
        {
          if (IdentityDatabase.ContainsKey(pubKey))
          {
            log.Debug("Identity with public key '{0}' already exists.", pubKey.ToHex());
            details = "add.identityPublicKey";
          }
        }
        else 
        {
          log.Debug("Invalid public key length '{0}'.", pubKey.Length);
          details = "add.identityPublicKey";
        }
      }

      if (details == null)
      {
        int nameSize = Encoding.UTF8.GetByteCount(AddItem.Name);
        bool nameValid = nameSize <= IdentityBase.MaxProfileNameLengthBytes;
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "add.name";
        }
      }

      if (details == null)
      {
        int typeSize = Encoding.UTF8.GetByteCount(AddItem.Type);
        bool typeValid = typeSize <= IdentityBase.MaxProfileTypeLengthBytes;
        if (!typeValid)
        {
          log.Debug("Invalid type size in bytes {0}.", typeSize);
          details = "add.type";
        }
      }

      if ((details == null) && AddItem.SetThumbnailImage)
      {
        byte[] thumbnailImage = AddItem.ThumbnailImage.ToByteArray();

        bool imageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && ImageHelper.ValidateImageFormat(thumbnailImage);
        if (!imageValid)
        {
          log.Debug("Invalid thumbnail image.");
          details = "add.thumbnailImage";
        }
      }

      if (details == null)
      {
        GpsLocation locLat = new GpsLocation(AddItem.Latitude, 0);
        if (!locLat.IsValid())
        {
          log.Debug("Invalid latitude {0}.", AddItem.Latitude);
          details = "add.latitude";
        }
      }

      if (details == null)
      {
        GpsLocation locLon = new GpsLocation(AddItem.Longitude, 0);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", AddItem.Longitude);
          details = "add.latitude";
        }
      }

      if (details == null)
      {
        int extraDataSize = Encoding.UTF8.GetByteCount(AddItem.ExtraData);
        bool extraDataValid = extraDataSize <= IdentityBase.MaxProfileExtraDataLengthBytes;
        if (!extraDataValid)
        {
          log.Debug("Invalid extraData size in bytes {0}.", extraDataSize);
          details = "add.extraData";
        }
      }


      if (details == null)
      {
        res = true;
      }
      else ErrorResponse = MessageBuilder.CreateErrorInvalidValueResponse(RequestMessage, Index.ToString() + "." + details);

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes FinishNeighborhoodInitializationRequest message from client sent during neighborhood initialization process.
    /// <para>Saves the temporary in-memory database of profiles to the database and moves the relevant images from the temporary directory to the images directory.</para>
    /// </summary>
    /// <param name="Client">Client that received the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the remote peer.</returns>
    private async Task<Message> ProcessMessageFinishNeighborhoodInitializationRequestAsync(OutgoingClient Client, Message RequestMessage)
    {
      log.Trace("()");

      MessageBuilder messageBuilder = Client.MessageBuilder;
      Message res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

      FinishNeighborhoodInitializationRequest finishNeighborhoodInitializationRequest = RequestMessage.Request.ConversationRequest.FinishNeighborhoodInitialization;

      bool error = false;
      Dictionary<byte[], NeighborIdentity> identityDatabase = (Dictionary<byte[], NeighborIdentity>)Client.Context;

      // First we move image files from temporary directory to images directory.
      HashSet<Guid> imagesAlreadyMoved = new HashSet<Guid>();
      foreach (NeighborIdentity identity in identityDatabase.Values)
      {
        if (identity.ThumbnailImage != null)
        {
          if (ImageHelper.MoveImageFileFromTemp(identity.ThumbnailImage.Value))
          {
            imagesAlreadyMoved.Add(identity.ThumbnailImage.Value);
          }
          else
          {
            error = true;
            log.Error("Unable to move image GUID '{0}' from temporary directory to images folder.", identity.ThumbnailImage.Value);
            break;
          }
        }
      }


      // Then we save new identities to the database.
      if (!error)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          bool success = false;
          DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborIdentityLock, UnitOfWork.NeighborLock };
          using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
          {
            try
            {
              Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.Id == Client.ServerId)).FirstOrDefault();
              if (neighbor != null)
              {
                // The neighbor is now initialized and is allowed to send us updates.
                neighbor.LastRefreshTime = DateTime.UtcNow;
                unitOfWork.NeighborRepository.Update(neighbor);

                // Insert all its identities.
                foreach (NeighborIdentity identity in identityDatabase.Values)
                  unitOfWork.NeighborIdentityRepository.Insert(identity);

                await unitOfWork.SaveThrowAsync();
                transaction.Commit();
                success = true;
              }
              else log.Error("Unable to find neighbor ID '{0}'.", Client.ServerId.ToHex());
            }
            catch (Exception e)
            {
              log.Error("Exception occurred: {0}", e.ToString());
            }

            if (!success)
            {
              log.Warn("Rolling back transaction.");
              unitOfWork.SafeTransactionRollback(transaction);
              error = true;
            }

            unitOfWork.ReleaseLock(lockObjects);
          }
        }
      }

      // Finally, if there was an error, we delete all relevant image files.
      if (error)
      {
        log.Debug("Error occurred, erasing images of all profiles received from neighbor ID '{0}'.", Client.ServerId.ToHex());
        foreach (NeighborIdentity identity in identityDatabase.Values)
        {
          if (identity.ThumbnailImage != null)
          {
            if (imagesAlreadyMoved.Contains(identity.ThumbnailImage.Value))
            {
              // This image is in images folder.
              if (!ImageHelper.DeleteImageFile(identity.ThumbnailImage.Value))
                log.Error("Unable to delete image GUID '{0}' from images folder.", identity.ThumbnailImage.Value);
            }
            else
            {
              // This image is in temporary folder.
              if (!ImageHelper.DeleteTempImageFile(identity.ThumbnailImage.Value))
                log.Error("Unable to delete image GUID '{0}' from temporary folder.", identity.ThumbnailImage.Value);
            }
          }
        }
      }


      if (!error) res = messageBuilder.CreateFinishNeighborhoodInitializationResponse(RequestMessage);

      log.Trace("(-):*.Response.Status={0}", res.Response.Status);
      return res;
    }


    /// <summary>
    /// Updates LastRefreshTime of a follower server.
    /// </summary>
    /// <param name="FollowerId">Identifier of the follower server to update.</param>
    /// <returns>true if the function succeeds, false otherwise.</returns>
    private async Task<bool> UpdateFollowerLastRefreshTime(byte[] FollowerId)
    {
      log.Trace("(FollowerId:'{0}')", FollowerId.ToHex());

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = UnitOfWork.FollowerLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          Follower follower = (await unitOfWork.FollowerRepository.GetAsync(f => f.Id == FollowerId)).FirstOrDefault();
          if (follower != null)
          {
            follower.LastRefreshTime = DateTime.UtcNow;
            unitOfWork.FollowerRepository.Update(follower);
            await unitOfWork.SaveThrowAsync();
            res = true;
          }
          else
          {
            log.Error("Follower ID '{0}' not found.", FollowerId.ToHex());
          }
        }
        catch (Exception e)
        {
          log.Error("Exception occurred while trying to update LastRefreshTime of follower ID '{0}': {1}", FollowerId.ToHex(), e.ToString());
        }

        unitOfWork.ReleaseLock(lockObject);
      }

      log.Trace("(-):{0}", res);
      return res;
    }


  }
}
