using Microsoft.EntityFrameworkCore.Storage;
using Newtonsoft.Json;
using ProfileServer.Data;
using ProfileServer.Data.Models;
using IopCrypto;
using IopProtocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IopCommon;
using IopServerCore.Kernel;
using IopServerCore.Data;
using IopServerCore.Network;
using Iop.Profileserver;
using Iop.Shared;

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
#warning Regtest needed - neighborhood initialization process takes too long -> server retries again later
#warning Regtest needed - shared profile refresh and expiration
#warning Regtest needed - update failed soft -> server retries again
#warning Regtest needed - update failed hard -> follower is deleted
#warning Regtest needed - update failed hard -> follower is deleted; server won't send updates to the follower -> neighbor expires on the follower -> LOC refreshes -> neighbor relationship established again
    /// <summary>Name of the component.</summary>
    public const string ComponentName = "Network.NeighborhoodActionProcessor";

    /// <summary>Class logger.</summary>
    private static Logger log = new Logger("ProfileServer." + ComponentName);



    /// <summary>Profile server's primary interface port.</summary>
    private uint primaryPort;

    /// <summary>Profile server neighbors interface port.</summary>
    private uint srNeighborPort;


    /// <summary>Lock to protect access to actionExecutorCounter.</summary>
    private object actionExecutorLock = new object();

    /// <summary>Number of action executions in progress.</summary>
    private int actionExecutorCounter = 0;


    /// <summary>
    /// Initializes the component.
    /// </summary>
    public NeighborhoodActionProcessor():
      base(ComponentName)
    {
    }


    public override bool Init()
    {
      log.Info("()");

      bool res = false;
      try
      {
        Server serverComponent = (Server)Base.ComponentDictionary[Server.ComponentName];
        List<TcpRoleServer<IncomingClient>> roleServers = serverComponent.GetRoleServers();
        foreach (TcpRoleServer<IncomingClient> roleServer in roleServers)
        {
          if ((roleServer.Roles & (uint)ServerRole.Primary) != 0)
            primaryPort = (uint)roleServer.EndPoint.Port;

          if ((roleServer.Roles & (uint)ServerRole.ServerNeighbor) != 0)
            srNeighborPort = (uint)roleServer.EndPoint.Port;
        }

        RegisterCronJobs();

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
      }

      log.Info("(-):{0}", res);
      return res;
    }


    public override void Shutdown()
    {
      log.Info("()");

      ShutdownSignaling.SignalShutdown();

      bool done = false;
      int counter = 0;
      log.Trace("Waiting for action executors to finish.");
      while (!done)
      {
        int actionCounter = 0;
        lock (actionExecutorLock)
        {
          actionCounter = actionExecutorCounter;
        }

        done = actionCounter == 0;
        if (!done)
        {
          log.Debug("Still {0} actions in progress.", actionCounter);

          counter++;
          if (counter >= 65)
          {
            log.Error("Waiting for action executors took too long, terminating.");
            break;
          }

          Thread.Sleep(1000);
        }
      }

      log.Info("(-)");
    }


    /// <summary>
    /// Registers component's cron jobs.
    /// </summary>
    public void RegisterCronJobs()
    {
      log.Trace("()");

      List<CronJob> cronJobDefinitions = new List<CronJob>()
      {
        // Checks if there are any neighborhood actions to process.
        { new CronJob() { Name = "checkNeighborhoodActionList", StartDelay = 20 * 1000, Interval = 20 * 1000, HandlerAsync = CronJobHandlerCheckNeighborhoodActionListAsync } },
      };

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.AddJobs(cronJobDefinitions);

      log.Trace("(-)");
    }


    /// <summary>
    /// Handler for "checkNeighborhoodActionList" cron job.
    /// </summary>
    public async void CronJobHandlerCheckNeighborhoodActionListAsync()
    {
      log.Trace("()");

      if (ShutdownSignaling.IsShutdown)
      {
        log.Trace("(-):[SHUTDOWN]");
        return;
      }

      await CheckActionListAsync();

      log.Trace("(-)");
    }


    /// <summary>
    /// Signals check action list event.
    /// </summary>
    public void Signal()
    {
      log.Trace("()");

      Cron cron = (Cron)Base.ComponentDictionary[Cron.ComponentName];
      cron.SignalEvent("checkNeighborhoodActionList");

      log.Trace("(-)");
    }

    

    /// <summary>
    /// Loads list of actions from the database and executes actions that can be executed.
    /// </summary>
    public async Task CheckActionListAsync()
    {
      log.Trace("()");

      try
      {
        while (!ShutdownSignaling.IsShutdown)
        {
          NeighborhoodAction action = await LoadNextActionAsync();
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
        await Task.Delay(5000);
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

      bool shutdown = false;
      lock (actionExecutorLock)
      {
        shutdown = ShutdownSignaling.IsShutdown;
        if (!shutdown)
          actionExecutorCounter++;
      }

      if (!shutdown)
      {
        bool removeAction = await ExecuteActionAsync(Action);
        if (removeAction)
        {
          // If the action was processed successfully, we remove it from the database.
          // Otherwise its ExecuteAfter flag is set to the future and the action will 
          // attempt to be processed again later.
          if (await RemoveActionAsync(Action.Id))
            log.Info("Action ID {0} removed from the database.", Action.Id);

          // We signal the event to have the action queue checked, because this action 
          // that just finished might have blocked another action in the queue.
          Signal();
        }
        else log.Info("Processing of action ID {0} failed.", Action.Id);


        lock (actionExecutorLock)
        {
          actionExecutorCounter--;
        }
      }
      else log.Trace("Shutdown detected.");

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
    private async Task<NeighborhoodAction> LoadNextActionAsync()
    {
      log.Trace("()");

      NeighborhoodAction res = null;

      int actionsInProgress = 0;
      lock (actionExecutorLock)
      {
        actionsInProgress = actionExecutorCounter;
      }

      // Never execute more than 5 actions at once.
      if (actionsInProgress >= 5)
      {
        log.Trace("(-)[TOO_MANY_ACTIONS]:null");
        return res;
      }


      // Network IDs of servers which profile actions can't be executed because a blocking action with future ExecuteAfter exists.
      HashSet<byte[]> profileActionsLockedIds = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);
      
      // Network IDs of servers which server actions can't be executed because a blocking action with future ExecuteAfter exists.
      HashSet<byte[]> serverActionsLockedIds = new HashSet<byte[]>(StructuralEqualityComparer<byte[]>.Default);

      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DateTime now = DateTime.UtcNow;
        DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          NeighborhoodAction actionToProcess = null;
          List<NeighborhoodAction> actions = (await unitOfWork.NeighborhoodActionRepository.GetAsync(null, q => q.OrderBy(a => a.Id))).ToList();
          foreach (NeighborhoodAction action in actions)
          {
            bool isProfileAction = action.IsProfileAction();

            bool isLocked = (isProfileAction && profileActionsLockedIds.Contains(action.ServerId))
              || (!isProfileAction && serverActionsLockedIds.Contains(action.ServerId));

            log.Trace("Action ID {0}, action type is {1}, isProfileAction is {2}, isLocked is {3}, execute after time is {4}.", action.Id, action.Type, isProfileAction, isLocked, action.ExecuteAfter != null ? action.ExecuteAfter.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null");

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
            await unitOfWork.SaveThrowAsync();

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
          else log.Info("Unable to find action ID {0} in the database, it has probably been removed already.", ActionId);
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
    /// <returns>true if the action should be removed, false otherwise.</returns>
    private async Task<bool> ExecuteActionAsync(NeighborhoodAction Action)
    {
      log.Trace("(Action.Id:{0},Action.Type:{1})", Action.Id, Action.Type);

      bool res = false;

      switch (Action.Type)
      {
        case NeighborhoodActionType.AddNeighbor:
          res = await NeighborhoodInitializationProcess(Action.ServerId, Action.ExecuteAfter.Value, Action.Id);
          break;

        case NeighborhoodActionType.RemoveNeighbor:
          res = await NeighborhoodRemoveNeighbor(Action.ServerId, Action.Id);
          break;

        case NeighborhoodActionType.StopNeighborhoodUpdates:
          res = await NeighborhoodRequestStopUpdates(Action.ServerId, Action.AdditionalData);
          break;

        case NeighborhoodActionType.AddProfile:
        case NeighborhoodActionType.ChangeProfile:
        case NeighborhoodActionType.RemoveProfile:
        case NeighborhoodActionType.RefreshProfiles:
          res = await NeighborhoodProfileUpdate(Action.ServerId, Action.TargetIdentityId, Action.Type, Action.AdditionalData, Action.Id);
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
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodInitializationProcess(byte[] NeighborId, DateTime MustFinishBefore, int CurrentActionId)
    {
      log.Trace("(NeighborId:'{0}',MustFinishBefore:{1},CurrentActionId:{2})", NeighborId.ToHex(), MustFinishBefore.ToString("yyyy-MM-dd HH:mm:ss"), CurrentActionId);

      bool res = false;

      // We MUST finish before MustFinishBefore time, so to be sure, we will terminate the process 
      // if we find ourselves running 90 seconds before that time. We have 60 seconds read timeout 
      // on the stream, so in the worst case, we should have 30 seconds reserve.
      DateTime processStart = DateTime.UtcNow;
      DateTime safeDeadline = MustFinishBefore.AddSeconds(-90);
      log.Trace("Setting up safe deadline to {0}.", safeDeadline.ToString("yyyy-MM-dd HH:mm:ss"));

      bool deleteNeighbor = false;
      bool resetSrNeighborPort = false;

      IPEndPoint endPoint = await GetNeighborServerContact(NeighborId);
      if (endPoint != null)
      {
        using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
        {
          Dictionary<byte[], NeighborIdentity> identityDatabase = new Dictionary<byte[], NeighborIdentity>(StructuralEqualityComparer<byte[]>.Default);
          client.Context = identityDatabase;

          bool connected = await client.ConnectAndVerifyIdentityAsync();
          if (!connected)
          {
            log.Debug("Failed to connect to neighbor ID '{0}' on address '{1}'. Trying to get new value of its srNeighbor port from its primaryPort.", NeighborId.ToHex(), endPoint);
            IPEndPoint newEndPoint = await GetNeighborServerContact(NeighborId, true);
            if (newEndPoint != null)
            {
              endPoint = newEndPoint;
              client.SetRemoteEndPoint(endPoint);
              connected = await client.ConnectAndVerifyIdentityAsync();
            }
            else log.Debug("Failed to get srNeighbor port from primaryPort of neighbor ID '{0}'.", NeighborId.ToHex());
          }

          if (connected)
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
                    log.Warn("Intialization process took too long, the safe deadline {0} has been reached, will retry later.", safeDeadline.ToString("yyyy-MM-dd HH:mm:ss"));
                    error = true;
                    break;
                  }

                  PsProtocolMessage requestMessage = await client.ReceiveMessageAsync();
                  if (requestMessage != null)
                  {
                    PsProtocolMessage responseMessage = client.MessageBuilder.CreateErrorProtocolViolationResponse(requestMessage);

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
                            log.Error("Invalid conversation request type '{0}' received, will retry later.", conversationRequest.RequestTypeCase);
                            error = true;
                            break;
                        }
                      }
                      else
                      {
                        log.Warn("Invalid conversation type '{0}' received, will retry later.", request.ConversationTypeCase);
                        error = true;
                      }
                    }
                    else
                    {
                      log.Warn("Invalid message type '{0}' received, expected Request, will retry later.", requestMessage.MessageTypeCase);
                      error = true;
                    }

                    // Send response to neighbor.
                    if (!await client.SendMessageAsync(responseMessage))
                    {
                      log.Warn("Unable to send response to neighbor, will retry later.");
                      error = true;
                      break;
                    }

                    if (client.ForceDisconnect)
                    {
                      error = true;
                      break;
                    }
                  }
                  else
                  {
                    log.Warn("Connection has been terminated, initialization process has not been completed, will retry later.");
                    error = true;
                  }
                }

                res = !error;

                if (!res)
                {
                  log.Debug("Error occurred, erasing images of all profiles received from neighbor ID '{0}'.", NeighborId.ToHex());

                  ImageManager imageManager = (ImageManager)Base.ComponentDictionary[ImageManager.ComponentName];
                  foreach (NeighborIdentity identity in identityDatabase.Values)
                  {
                    if (identity.ThumbnailImage != null)
                      imageManager.RemoveImageReference(identity.ThumbnailImage);
                  }
                }
              }
              else
              {
                if (client.LastResponseStatus == Status.ErrorBusy)
                {
                  // Target server is busy at the moment and does not want to talk to us about neighborhood initialization.
                  log.Debug("Neighbor ID '{0}' is busy now, let's try later.", NeighborId.ToHex());
                }
                else if (client.LastResponseStatus == Status.ErrorBadRole)
                {
                  log.Info("Neighbor ID '{0}' rejected start of initialization process to bad port usage (port {1} was used), reseting srNeighbor port for this follower, will retry later.", NeighborId.ToHex(), endPoint.Port);
                  resetSrNeighborPort = true;
                }
                else log.Warn("Starting the intialization process failed with neighbor ID '{0}', will retry later.", NeighborId.ToHex());
              }
            }
            else
            {
              log.Warn("Server identity differs from expected ID '{0}', deleting this neighbor.", NeighborId.ToHex());
              deleteNeighbor = true;
            }
          }
          else log.Warn("Unable to initiate conversation with neighbor ID '{0}' on address {1}, will retry later.", NeighborId.ToHex(), endPoint);
        }
      }
      else
      {
        log.Warn("Unable to find neighbor ID '{0}' IP and port information, will retry later.", NeighborId.ToHex());
        //
        // If we are here it means that srNeighborPort of this neighbor was reset before because we received ERROR_BAD_ROLE from the old srNeighborPort port.
        // This could happen if the neighbor server changed its ports and the port we know is not used as srNeighborPort anymore.
        // Then we attempted to connect to its primaryPort to get new value for its srNeighborPort and this failed as well,
        // which means that the server was offline.
        //
        // The solution here is to wait. All actions to this neighbor will be blocked and this particular action will be retried later again.
        // If the neighbor gets online and we succeed, actions for it will be unblocked again. Otherwise it will be eventually deleted 
        // when it is unresponsive for long, either we will get notified from LOC server, or we find out on ourselves in Database.CheckExpiredNeighborsAsync().
        //
      }

      if (deleteNeighbor)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          if (await unitOfWork.NeighborRepository.DeleteNeighborAsync(NeighborId, CurrentActionId))
          {
            // Neighbor was deleted from the database, all its actions should be deleted 
            // except for this one that is currently being executed.
            // Nothing more to do here.
            log.Info("Neighbor ID '{0}' has been deleted.", NeighborId.ToHex());
            res = true;
          }
          else log.Error("Unable to delete neighbor ID '{0}' from database.", NeighborId.ToHex());
        }
      }

      if (resetSrNeighborPort)
      {
        using (UnitOfWork unitOfWork = new UnitOfWork())
        {
          if (await unitOfWork.NeighborRepository.ResetSrNeighborPortAsync(NeighborId)) log.Info("srNeighbor port of neighbor ID '{0}' has been reset.", NeighborId.ToHex());
          else log.Error("Unable to reset srNeighbor port of neighbor ID '{0}'.", NeighborId.ToHex());
        }
      }


      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Obtains IP address and srNeighbor port from neighbor server's network identifier.
    /// </summary>
    /// <param name="NeighborId">Network identifer of the neighbor server.</param>
    /// <param name="IgnoreDbPortValue">If set to true, the function will ignore SrNeighborPort value of the neighbor even if it is set in the database 
    /// and will contact the neighbor on its primary port and then update SrNeighborPort in the database, if it successfully gets its value.</param>
    /// <returns>End point description or null if the function fails.</returns>
    private async Task<IPEndPoint> GetNeighborServerContact(byte[] NeighborId, bool IgnoreDbPortValue = false)
    {
      log.Trace("(NeighborId:'{0}',IgnoreDbPortValue:{1})", NeighborId.ToHex(), IgnoreDbPortValue);

      IPEndPoint res = null;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = null;
        bool unlock = false;
        try
        {
          Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == NeighborId)).FirstOrDefault();
          if (neighbor != null)
          {
            IPAddress addr = IPAddress.Parse(neighbor.IpAddress);
            if (!IgnoreDbPortValue && (neighbor.SrNeighborPort != null))
            {
              res = new IPEndPoint(addr, neighbor.SrNeighborPort.Value);
            }
            else
            {
              // We do not know srNeighbor port of this neighbor yet (or we ignore it), we have to connect to its primary port and get that information.
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
              else log.Debug("Unable to obtain srNeighbor port from primary port of neighbor ID '{0}'.", NeighborId.ToHex());
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
    /// <param name="IgnoreDbPortValue">If set to true, the function will ignore SrNeighborPort value of the follower even if it is set in the database 
    /// and will contact the follower on its primary port and then update SrNeighborPort in the database, if it successfully gets its value.</param>
    /// <returns>End point description or null if the function fails.</returns>
    private async Task<IPEndPoint> GetFollowerServerContact(byte[] FollowerId, bool IgnoreDbPortValue = false)
    {
      log.Trace("(FollowerId:'{0}',IgnoreDbPortValue:{1})", FollowerId.ToHex(), IgnoreDbPortValue);

      IPEndPoint res = null;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        DatabaseLock lockObject = null;
        bool unlock = false;
        try
        {
          Follower follower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == FollowerId)).FirstOrDefault();
          if (follower != null)
          {
            IPAddress addr = IPAddress.Parse(follower.IpAddress);
            if (!IgnoreDbPortValue && (follower.SrNeighborPort != null))
            {
              res = new IPEndPoint(addr, follower.SrNeighborPort.Value);
            }
            else
            {
              // We do not know srNeighbor port of this follower yet (or we ignore it), we have to connect to its primary port and get that information.
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
              else log.Debug("Unable to obtain srNeighbor port from primary port of follower ID '{0}'.", FollowerId.ToHex());
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
    /// A neighbor expired and needs to be removed from a neighborhood. We delete it and all its shared profiles from our database.
    /// </summary>
    /// <param name="NeighborId">Network identifier of the former neighbor server.</param>
    /// <param name="CurrentActionId">Identifier of the action being executed.</param>
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodRemoveNeighbor(byte[] NeighborId, int CurrentActionId)
    {
      log.Trace("(NeighborId:'{0}',CurrentActionId:{1})", NeighborId.ToHex(), CurrentActionId);

      bool res = false;
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == NeighborId, null, false)).FirstOrDefault();
        string neighborInfo = JsonConvert.SerializeObject(neighbor);

        // Delete neighbor completely.
        res = await unitOfWork.NeighborRepository.DeleteNeighborAsync(NeighborId, CurrentActionId);

        // Add action that will contact the neighbor and ask it to stop sending updates.
        // Note that the neighbor information will be deleted by the time this action 
        // is executed and this is why we have to fill in AdditionalData.
        NeighborhoodAction stopUpdatesAction = new NeighborhoodAction()
        {
          ServerId = NeighborId,
          Type = NeighborhoodActionType.StopNeighborhoodUpdates,
          TargetIdentityId = null,
          ExecuteAfter = DateTime.UtcNow,
          Timestamp = DateTime.UtcNow,
          AdditionalData = neighborInfo
        };

        DatabaseLock lockObject = UnitOfWork.NeighborhoodActionLock;
        await unitOfWork.AcquireLockAsync(lockObject);
        try
        {
          await unitOfWork.NeighborhoodActionRepository.InsertAsync(stopUpdatesAction);

          await unitOfWork.SaveThrowAsync();
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
    /// Profile server removed neighbor server from its neighborhood and needs to contact it and ask it to stop sending updates.
    /// </summary>
    /// <param name="NeighborId">Network identifier of the former neighbor server.</param>
    /// <param name="NeighborInfo">Serialized neighbor information.</param>
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodRequestStopUpdates(byte[] NeighborId, string NeighborInfo)
    {
      log.Trace("(NeighborId:'{0}',NeighborInfo:'{1}')", NeighborId.ToHex(), NeighborInfo);

      bool res = false;

      Neighbor neighbor = null;
      IPAddress ipAddress = IPAddress.Any;
      try
      {
        neighbor = JsonConvert.DeserializeObject<Neighbor>(NeighborInfo);
        ipAddress = IPAddress.Parse(neighbor.IpAddress);
      }
      catch (Exception e)
      {
        log.Error("Exception occurred: {0}", e.ToString());
      }

      if (neighbor == null)
      {
        // Delete this action, it is corrupted.
        log.Trace("(-)[ERROR]:true");
        return true;
      }

      bool newNeighborPortObtained = false;
      int srNeighborPort = neighbor.SrNeighborPort != null ? neighbor.SrNeighborPort.Value : 0;

      if (srNeighborPort == 0)
      {
        srNeighborPort = await GetServerRolePortFromPrimaryPort(ipAddress, neighbor.PrimaryPort, ServerRoleType.SrNeighbor);
        newNeighborPortObtained = true;
      }

      if (srNeighborPort != 0)
      {
        IPEndPoint endPoint = new IPEndPoint(ipAddress, srNeighborPort);
        using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
        {
          bool connected = await client.ConnectAndVerifyIdentityAsync();
          if (!connected && !newNeighborPortObtained)
          {
            log.Debug("Failed to connect to neighbor ID '{0}' on address '{1}'. Trying to get new value of its srNeighborPort from its primaryPort.", NeighborId.ToHex(), endPoint);
            int newSrNeighborPort = await GetServerRolePortFromPrimaryPort(ipAddress, neighbor.PrimaryPort, ServerRoleType.SrNeighbor);
            if (newSrNeighborPort != 0)
            {
              client.SetRemoteEndPoint(new IPEndPoint(ipAddress, newSrNeighborPort));
              connected = await client.ConnectAndVerifyIdentityAsync();
            }
            else log.Debug("Failed to get srNeighborPort from primaryPort of neighbor ID '{0}'.", NeighborId.ToHex());
          }

          if (connected)
          {
            if (client.MatchServerId(NeighborId))
            {
              PsProtocolMessage requestMessage = client.MessageBuilder.CreateStopNeighborhoodUpdatesRequest();
              if (!await client.SendStopNeighborhoodUpdates(requestMessage))
              {
                if (client.LastResponseStatus == Status.ErrorNotFound) log.Info("Neighbor ID '{0}' does not register us as followers.", NeighborId.ToHex());
                else log.Warn("Sending update to neighbor ID '{0}' failed, error status {1}.", NeighborId.ToHex(), client.LastResponseStatus);
              }
            }
            else log.Warn("Server identity differs from expected ID '{0}'.", NeighborId.ToHex());
          }
          else log.Info("Unable to initiate conversation with neighbor ID '{0}' on address '{1}'.", NeighborId.ToHex(), endPoint);
        }
      }
      else log.Warn("Unable to find neighbor ID '{0}' IP and port information.", NeighborId.ToHex());

      // In case of failure, we will not try again. The neighbor is deleted from our database 
      // and hopefully will not send us any more updates. If it does, we will reject them.
      res = true;

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
    /// <param name="ActionId">Identifier of the neighborhood action currently being executed.</param>
    /// <returns>true if the neighborhood action responsible for executing this method should be removed, false otherwise.</returns>
    private async Task<bool> NeighborhoodProfileUpdate(byte[] FollowerId, byte[] IdentityId, NeighborhoodActionType ActionType, string AdditionalData, int ActionId)
    {
      log.Trace("(FollowerId:'{0}',IdentityId:'{1}',ActionType:{2},AdditionalData:'{3}',ActionId:{4})", FollowerId.ToHex(), IdentityId.ToHex(), ActionType, AdditionalData, ActionId);

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
                  else
                  {
                    log.Warn("Unable to find hosted identity ID '{0}'.", IdentityId.ToHex());
                    //
                    // This happens when the profile's hosting has been cancelled and the cron task deleted 
                    // the profile from the database before we managed to send the update to the follower. 
                    // 
                    // We have to remove this action from the queue because otherwise it would block all actions 
                    // to this follower forever. However, the problem is that the follower is not aware of this 
                    // identity and we may have change or remove updates in the action queue. Thus if we just 
                    // returned true here, the subsequent actions could fail because of this inconsistency.
                    //
                    // We solve this problem using a hack, which is creating an artifical profile for this 
                    // (no longer hosted) identity. We propagate that artifical profile to the follower 
                    // and so the subsequent profile removal update action can happen flawlessly.
                    //
                    // We also skip all change profile update actions if there are any.
                    //
                    // Alternatively, we could process the action queue, either here or during cancelling 
                    // the hosting or during deleting inactive profiles from the database. However, such 
                    // a solution is complex and error prone. This is by far the easiest way to deal 
                    // with this somewhat rare case.
                    //

                    byte[] identityPublicKey = AdditionalData.FromHex();
                    SharedProfileAddItem item = new SharedProfileAddItem()
                    {
                      Version =  SemVer.V100.ToByteString(),
                      IdentityPublicKey = ProtocolHelper.ByteArrayToByteString(identityPublicKey),
                      Name = AdditionalData,
                      Type = "Invalid",
                      SetThumbnailImage = false,
                      Latitude = 0,
                      Longitude = 0,
                      ExtraData = ""
                    };

                    updateItem = new SharedProfileUpdateItem();
                    updateItem.Add = item;
                  }

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
                  else
                  {
                    log.Warn("Unable to find hosted identity ID '{0}'.", IdentityId.ToHex());
                    //
                    // This happens when the profile's hosting has been cancelled and the cron task deleted 
                    // the profile from the database before we managed to send the update to the follower. 
                    // See the analysis in AddProfile case for more information.
                    //
                    // We simply return true here to erase this action from the queue.
                    //
                    res = true;
                  }
                  break;
                }

              case NeighborhoodActionType.RemoveProfile:
                {
                  // Because of using the artifical profile, we can be sure here that the follower is aware of 
                  // this profile even if it was deleted from our database before add profile update action 
                  // was taken from the queue. See the analysis in AddProfile case for more information.
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
                
                // Should never happen. Same resolution as in case of exception below.
                break;
            }
          }
          catch (Exception e)
          {
            log.Error("Exception occurred: {0}", e.ToString());
            //
            // We should never be here, there probably is a bug and this action will probably cause 
            // this bug again if we don't remove it. However, removing the action is even worse because 
            // it would cause silent inconsistency of the follower's database, which may even be occasionally 
            // fixed by future updates, but we would rather want to know about bugs of this severity,
            // so we leave the action in the queue and hopefully someone will notice sooner or later.
            //
          }
        }

        if (updateItem != null)
        {
          bool deleteFollower = false;
          bool resetSrNeighborPort = false;

          using (OutgoingClient client = new OutgoingClient(endPoint, true, ShutdownSignaling.ShutdownCancellationTokenSource.Token))
          {
            // If we successfully constructed the update for the follower server, we connect to it and send it.
            bool connected = await client.ConnectAndVerifyIdentityAsync();
            if (!connected)
            {
              log.Debug("Failed to connect to follower ID '{0}' on address '{1}'. Trying to get new value of its srNeighbor port from its primaryPort.", FollowerId.ToHex(), endPoint);
              IPEndPoint newEndPoint = await GetFollowerServerContact(FollowerId, true);
              if (newEndPoint != null)
              {
                endPoint = newEndPoint;
                client.SetRemoteEndPoint(endPoint);
                connected = await client.ConnectAndVerifyIdentityAsync();
              }
              else log.Debug("Failed to get srNeighbor port from primaryPort of follower ID '{0}'.", FollowerId.ToHex());
            }

            if (connected)
            {
              if (client.MatchServerId(FollowerId))
              {
                List<SharedProfileUpdateItem> updateList = new List<SharedProfileUpdateItem>() { updateItem };
                PsProtocolMessage updateMessage = client.MessageBuilder.CreateNeighborhoodSharedProfileUpdateRequest(updateList);
                if (await client.SendNeighborhoodSharedProfileUpdate(updateMessage))
                {
                  if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Refresh)
                  {
                    // If database update fails, the follower server will just be refreshed again later.
                    await UpdateFollowerLastRefreshTime(FollowerId);
                  }

                  res = true;
                }
                else
                {
                  if (client.LastResponseStatus == Status.ErrorRejected)
                  {
                    log.Info("Follower ID '{0}' rejected an update, deleting this follower.", FollowerId.ToHex());
                    deleteFollower = true;
                  }
                  else if (client.LastResponseStatus == Status.ErrorBadRole)
                  {
                    log.Info("Follower ID '{0}' rejected an update due to bad port usage (port {1} was used), reseting srNeighbor port for this follower, will retry later.", FollowerId.ToHex(), endPoint.Port);
                    resetSrNeighborPort = true;
                  }
                  else if (client.LastResponseStatus == Status.ErrorInvalidValue)
                  {
                    //
                    // There are two possibilities here. First case is that we are in trouble because the database of the follower is not synchronized with ours
                    // in which case retrying would not make it any better, and our best move is to delete the follower. Our shared profiles with it will expire 
                    // in time and the follower will contact us again to reestablish the relationship.
                    // 
                    // The second case is a very rare case that can happen if the server sent NeighborhoodSharedProfileUpdateRequest to its follower 
                    // in the past and the follower did accept and did process the update and saved it into its database, but it failed to deliver 
                    // NeighborhoodSharedProfileUpdateResponse to this server (e.g. due to a power failure on either side, or a network failure).
                    // We could handle this special case differently but it is not easy to detect it. This is why we delete the follower as well in this case 
                    // although it might not be necessary, but it is definitely the easiest thing to do.
                    //
                    log.Warn("Sending update to follower ID '{0}' failed, error status {1}, deleting follower!", FollowerId.ToHex(), client.LastResponseStatus);
                    deleteFollower = true;
                  }
                  else if (client.LastResponseStatus != Status.Ok)
                  {
                    // In this case we have received unexpected error from the follower, we do not know how to recover from this, 
                    // so we just delete it and hope it will get better next time.
                    log.Warn("Sending update to follower ID '{0}' failed, unexpected error status {1}, deleting follower!", FollowerId.ToHex(), client.LastResponseStatus);
                    deleteFollower = true;
                  }
                  else
                  {
                    // This means that there was a problem with connection, we can try later and should be able to recover.
                    log.Warn("Sending update to follower ID '{0}' failed, will retry later.", FollowerId.ToHex());
                  }
                }
              }
              else
              {
                log.Warn("Server identity differs from expected ID '{0}', deleting this follower.", FollowerId.ToHex());
                deleteFollower = true;
              }
            }
            else
            {
              log.Info("Unable to initiate conversation with follower ID '{0}' on address {1}, will retry later.", FollowerId.ToHex(), endPoint);

              // We have failed to connect to the follower's srNeighborPort as well as to get the current srNeighborPort from its primaryPort.
              // The follower is probably offline and we will try to process this action later.
            }
          }

          if (deleteFollower)
          {
            using (UnitOfWork unitOfWork = new UnitOfWork())
            {
              Status status = await unitOfWork.FollowerRepository.DeleteFollowerAsync(FollowerId, ActionId);
              if ((status == Status.Ok) || (status == Status.ErrorNotFound))
              {
                // Follower was deleted from the database, all its actions should be deleted by now 
                // except for this one that is currently being executed.
                // Nothing more to do here.
                log.Info("Follower ID '{0}' has been deleted.", FollowerId.ToHex());
                res = true;
              }
              else log.Error("Unable to delete follower ID '{0}' from database.", FollowerId.ToHex());
            }
          }

          if (resetSrNeighborPort)
          {
            using (UnitOfWork unitOfWork = new UnitOfWork())
            {
              if (await unitOfWork.FollowerRepository.ResetSrNeighborPortAsync(FollowerId)) log.Info("srNeighbor port of follower ID '{0}' has been reset.", FollowerId.ToHex());
              else log.Error("Unable to reset srNeighbor port of follower ID '{0}'.", FollowerId.ToHex());
            }
          }
        }
        else
        {
          // Only change profile update action can cause this when the profile was deleted from our database already.
          // In that case result is set to true, so the action will be removed from the queue.
          if (!res) log.Error("No update item was created but the action remains in the queue.");
        }
      }
      else
      {
        //
        // If we are here it means that srNeighborPort of this follower was reset before because we received ERROR_BAD_ROLE from the old srNeighborPort port.
        // This could happen if the follower server changed its ports and the port we know is not used as srNeighborPort anymore.
        // Then we attempted to connect to its primaryPort to get new value for its srNeighborPort and this failed as well,
        // which means that the server was offline.
        //
        // The solution here is to wait. All actions to this follower will be blocked and this particular action will be retried later again.
        // If the follower gets online and we succeed, actions for it will be unblocked again. Otherwise it will be eventually deleted 
        // when it is unresponsive for long time in Database.CheckFollowersRefreshAsync().
        //
        log.Warn("Unable to find follower ID '{0}' IP and port information, will retry later.", FollowerId.ToHex());
      }

      log.Trace("(-):{0}", res);
      return res;
    }


    /// <summary>
    /// Processes NeighborhoodSharedProfileUpdateRequest message from client sent during neighborhood initialization process.
    /// <para>Saves incoming profiles information to the temporary memory location.</para>
    /// </summary>
    /// <param name="Client">Client that received the request.</param>
    /// <param name="RequestMessage">Full request message.</param>
    /// <returns>Response message to be sent to the remote peer.</returns>
    private async Task<PsProtocolMessage> ProcessMessageNeighborhoodSharedProfileUpdateRequestAsync(OutgoingClient Client, PsProtocolMessage RequestMessage)
    {
      log.Trace("()");

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      PsProtocolMessage res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

      bool error = false;
      NeighborhoodSharedProfileUpdateRequest neighborhoodSharedProfileUpdateRequest = RequestMessage.Request.ConversationRequest.NeighborhoodSharedProfileUpdate;
      log.Debug("Received {0} update items.", neighborhoodSharedProfileUpdateRequest.Items.Count);

      Dictionary<byte[], NeighborIdentity> identityDatabase = (Dictionary<byte[], NeighborIdentity>)Client.Context;
      int itemIndex = 0;
      foreach (SharedProfileUpdateItem updateItem in neighborhoodSharedProfileUpdateRequest.Items)
      {
        if (updateItem.ActionTypeCase == SharedProfileUpdateItem.ActionTypeOneofCase.Add)
        {
          SharedProfileAddItem addItem = updateItem.Add;
          PsProtocolMessage errorResponse;
          if (ValidateInMemorySharedProfileAddItem(addItem, itemIndex, identityDatabase, Client.MessageBuilder, RequestMessage, out errorResponse))
          {
            byte[] thumbnailImageHash = null;
            byte[] thumbnailImageData = addItem.SetThumbnailImage ? addItem.ThumbnailImage.ToByteArray() : null;
            if (thumbnailImageData != null)
            {
              thumbnailImageHash = Crypto.Sha256(thumbnailImageData);
              if (!await ImageManager.SaveImageDataAsync(thumbnailImageHash, thumbnailImageData))
              {
                log.Error("Unable to save image hash '{0}' data to images directory.", thumbnailImageHash.ToHex());
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
              ThumbnailImage = thumbnailImageHash
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

      // Setting Client.ForceDisconnect to true will cause newly added images to be deleted.
      if (!error) res = messageBuilder.CreateNeighborhoodSharedProfileUpdateResponse(RequestMessage);
      else Client.ForceDisconnect = true;

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
    private bool ValidateInMemorySharedProfileAddItem(SharedProfileAddItem AddItem, int Index, Dictionary<byte[], NeighborIdentity> IdentityDatabase, PsMessageBuilder MessageBuilder, PsProtocolMessage RequestMessage, out PsProtocolMessage ErrorResponse)
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
        bool pubKeyValid = (0 < pubKey.Length) && (pubKey.Length <= ProtocolHelper.MaxPublicKeyLengthBytes);
        if (pubKeyValid)
        {
          byte[] id = Crypto.Sha256(pubKey);
          if (IdentityDatabase.ContainsKey(id))
          {
            log.Debug("Identity with public key '{0}' (ID '{1}') already exists.", pubKey.ToHex(), id.ToHex());
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
        bool nameValid = !string.IsNullOrEmpty(AddItem.Name) && (nameSize <= IdentityBase.MaxProfileNameLengthBytes);
        if (!nameValid)
        {
          log.Debug("Invalid name size in bytes {0}.", nameSize);
          details = "add.name";
        }
      }

      if (details == null)
      {
        int typeSize = Encoding.UTF8.GetByteCount(AddItem.Type);
        bool typeValid = (0 < typeSize) && (typeSize <= IdentityBase.MaxProfileTypeLengthBytes);
        if (!typeValid)
        {
          log.Debug("Invalid type size in bytes {0}.", typeSize);
          details = "add.type";
        }
      }

      if ((details == null) && AddItem.SetThumbnailImage)
      {
        byte[] thumbnailImage = AddItem.ThumbnailImage.ToByteArray();

        bool imageValid = (thumbnailImage.Length <= IdentityBase.MaxThumbnailImageLengthBytes) && ImageManager.ValidateImageFormat(thumbnailImage);
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
        GpsLocation locLon = new GpsLocation(0, AddItem.Longitude);
        if (!locLon.IsValid())
        {
          log.Debug("Invalid longitude {0}.", AddItem.Longitude);
          details = "add.longitude";
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
    private async Task<PsProtocolMessage> ProcessMessageFinishNeighborhoodInitializationRequestAsync(OutgoingClient Client, PsProtocolMessage RequestMessage)
    {
      log.Trace("()");

      PsMessageBuilder messageBuilder = Client.MessageBuilder;
      PsProtocolMessage res = messageBuilder.CreateErrorInternalResponse(RequestMessage);

      FinishNeighborhoodInitializationRequest finishNeighborhoodInitializationRequest = RequestMessage.Request.ConversationRequest.FinishNeighborhoodInitialization;

      bool error = false;
      Dictionary<byte[], NeighborIdentity> identityDatabase = (Dictionary<byte[], NeighborIdentity>)Client.Context;

      // Save new identities to the database.
      using (UnitOfWork unitOfWork = new UnitOfWork())
      {
        bool success = false;
        DatabaseLock[] lockObjects = new DatabaseLock[] { UnitOfWork.NeighborIdentityLock, UnitOfWork.NeighborLock };
        using (IDbContextTransaction transaction = await unitOfWork.BeginTransactionWithLockAsync(lockObjects))
        {
          try
          {
            Neighbor neighbor = (await unitOfWork.NeighborRepository.GetAsync(n => n.NeighborId == Client.ServerId)).FirstOrDefault();
            if (neighbor != null)
            {
              // The neighbor is now initialized and is allowed to send us updates.
              neighbor.LastRefreshTime = DateTime.UtcNow;
              neighbor.SharedProfiles = identityDatabase.Count;
              unitOfWork.NeighborRepository.Update(neighbor);

              // Insert all its identities.
              foreach (NeighborIdentity identity in identityDatabase.Values)
                await unitOfWork.NeighborIdentityRepository.InsertAsync(identity);

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


      // If there was an error, we delete all relevant image files, this is done in NeighborhoodInitializationProcess
      // when Client.ForceDisconnect is true or any other error occurs.
      if (!error) res = messageBuilder.CreateFinishNeighborhoodInitializationResponse(RequestMessage);
      else Client.ForceDisconnect = true;

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
          Follower follower = (await unitOfWork.FollowerRepository.GetAsync(f => f.FollowerId == FollowerId)).FirstOrDefault();
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
