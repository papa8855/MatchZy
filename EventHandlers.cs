using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;
public partial class MatchZy
{
    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (!IsPlayerValid(player)) return HookResult.Continue;
            Log($"[FULL CONNECT] Player ID: {player!.UserId}, Name: {player.PlayerName} has connected!");

            // 保持你要求的：不進行 JSON/SteamID 檢查，也不踢人
            if (player.UserId.HasValue)
            {
                playerData[player.UserId.Value] = player;
                connectedPlayers++;
                if (readyAvailable && !matchStarted)
                {
                    playerReadyStatus[player.UserId.Value] = false;
                }
                else
                {
                    playerReadyStatus[player.UserId.Value] = true;
                }
            }

            if (readyAvailable && !matchStarted)
            {
                if (GetRealPlayersCount() == 1)
                {
                    Log($"[FULL CONNECT] First player has connected, starting warmup!");
                    ExecUnpracCommands();
                    AutoStart();
                }
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerConnectFull FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerDisconnectHandler(EventPlayerDisconnect @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            if (!player!.UserId.HasValue) return HookResult.Continue;
            int userId = player.UserId.Value;

            if (playerReadyStatus.ContainsKey(userId))
            {
                playerReadyStatus.Remove(userId);
                connectedPlayers--;
            }
            playerData.Remove(userId);

            if (matchzyTeam1.coach.Contains(player))
            {
                matchzyTeam1.coach.Remove(player);
                SetPlayerVisible(player);
                player.Clan = "";
            }
            else if (matchzyTeam2.coach.Contains(player))
            {
                matchzyTeam2.coach.Remove(player);
                SetPlayerVisible(player);
                player.Clan = "";
            }
            noFlashList.Remove(userId);
            lastGrenadesData.Remove(userId);
            nadeSpecificLastGrenadeData.Remove(userId);

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerDisconnect FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventCsWinPanelRoundHandler(EventCsWinPanelRound @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }

    // --- 核心修正處：解決地圖結束換圖時，聊天室隊名反轉的問題 ---
    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        try
        {
            if (!isMatchLive) return HookResult.Continue;

            // 加上 1.0f (1秒) 的延遲，確保最後一回合的分數與隊名在引擎中完全定格
            AddTimer(1.0f, () => {
                int ctScore = 0;
                int tScore = 0;
                string currentCtName = "";
                
                // 從引擎抓取最真實的物理數據
                var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
                foreach (var team in teams) {
                    if (team.TeamNum == 3) {
                        ctScore = team.Score; 
                        currentCtName = team.Teamname; 
                    }
                    if (team.TeamNum == 2) tScore = team.Score;
                }

                // 根據「目前的物理 CT 隊名」來進行分數與系統變數的對位
                int sendT1, sendT2;
                if (currentCtName == matchzyTeam1.teamName) {
                    // 目前畫面的 CT 是系統的 Team1
                    sendT1 = ctScore;
                    sendT2 = tScore;
                } else {
                    // 目前畫面的 CT 是系統的 Team2
                    sendT1 = tScore;
                    sendT2 = ctScore;
                }

                // 算出正確的贏家名字
                string winnerName = (sendT1 > sendT2) ? matchzyTeam1.teamName : matchzyTeam2.teamName;

                // 呼叫原本正常的 EndSeries 進行廣播與換圖
                // 因為有了這 1 秒的緩衝，MatchManagement 就不會因為太快執行而錯亂
                EndSeries(winnerName, 10, sendT1, sendT2);
            });

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventCsWinPanelMatch FATAL] {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
    {
        try
        {
            HandlePostRoundStartEvent(@event);
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundStart FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundFreezeEndHandler(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        try
        {
            if (!matchStarted) return HookResult.Continue;
            HashSet<CCSPlayerController> coaches = GetAllCoaches();
            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach)) continue;
                if (coach.PlayerPawn.Value?.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                Position coachPosition = new(coach.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin, coach.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsRotation);
                coach!.PlayerPawn.Value!.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
                AddTimer(1.5f, () =>
                {
                    coach!.PlayerPawn.Value!.Teleport(new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f), coachPosition.PlayerAngle, new Vector(0, 0, 0));
                    CsTeam oldTeam = GetCoachTeam(coach);
                    coach.ChangeTeam(CsTeam.Spectator);
                    AddTimer(0.01f, () => coach.ChangeTeam(oldTeam));
                });
            }
            return HookResult.Continue;
        }
        catch (Exception e) { return HookResult.Continue; }
    }

    public HookResult EventPlayerGivenC4(EventPlayerGivenC4 @event, GameEventInfo info) {
        try {
            if (!matchStarted) return HookResult.Continue;
            if (@event.Userid == null) return HookResult.Continue;
            var recv = @event.Userid;
            var coaches = reverseTeamSides["TERRORIST"].coach;
            if (coaches.Contains(recv)) TransferCoachBomb(recv);
        } catch (Exception e) { }
        return HookResult.Continue;
    }

    public void OnEntitySpawnedHandler(CEntityInstance entity)
    {
        try
        {
            if (!isPractice || entity == null || entity.Entity == null) return;
            if (!Constants.ProjectileTypeMap.ContainsKey(entity.Entity.DesignerName)) return;

            Server.NextFrame(() => {
                CBaseCSGrenadeProjectile projectile = new CBaseCSGrenadeProjectile(entity.Handle);
                if (!projectile.IsValid || !projectile.Thrower.IsValid || projectile.Thrower.Value?.Controller.Value == null) return;

                CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                if(!player.IsValid || player.PlayerPawn.Value == null) return;
                int client = player.UserId!.Value;
                
                string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];
                if (!lastGrenadesData.ContainsKey(client)) lastGrenadesData[client] = new();
                if (!nadeSpecificLastGrenadeData.ContainsKey(client)) nadeSpecificLastGrenadeData[client] = new(){};

                GrenadeThrownData lastGrenadeThrown = new(
                    new Vector(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z), 
                    new QAngle(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z), 
                    new Vector(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z), 
                    player.PlayerPawn.Value.CBodyComponent!.SceneNode!.AbsOrigin, 
                    player.PlayerPawn.Value.EyeAngles,
                    nadeType, DateTime.Now, projectile.ItemIndex
                );

                nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
                lastGrenadesData[client].Add(lastGrenadeThrown);
                if (maxLastGrenadesSavedLimit != 0 && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit) lastGrenadesData[client].RemoveAt(0);

                lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
            });
        } catch (Exception e) { }
    }

    public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!matchStarted) return HookResult.Continue;
        if (@event.Attacker == @event.Userid && (matchzyTeam1.coach.Contains(@event.Attacker!) || matchzyTeam2.coach.Contains(@event.Attacker!))) info.DontBroadcast = true;
        return HookResult.Continue;
    }

    public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) lastGrenadeThrownTime.Remove(@event.Entityid);
        return HookResult.Continue;
    }

    public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) lastGrenadeThrownTime.Remove(@event.Entityid);
        return HookResult.Continue;
    }

    public HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) lastGrenadeThrownTime.Remove(@event.Entityid);
        return HookResult.Continue;
    }

    public HookResult EventMolotovDetonateHandler(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        return HookResult.Continue;
    }

    public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) lastGrenadeThrownTime.Remove(@event.Entityid);
        return HookResult.Continue;
    }
}
