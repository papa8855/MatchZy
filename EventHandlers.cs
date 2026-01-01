using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy
{
    public partial class MatchZy
    {
        public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
        {
            try
            {
                CCSPlayerController? player = @event.Userid;

                if (!IsPlayerValid(player)) return HookResult.Continue;
                Log($"[FULL CONNECT] Player ID: {player!.UserId}, Name: {player.PlayerName} has connected!");

                // Handling whitelisted players
                if (!player.IsBot || !player.IsHLTV)
                {
                    var steamId = player.SteamID;

                    bool kicked = isWhitelistRequired && HandlePlayerWhitelist(player, steamId.ToString());
                    if (kicked) return HookResult.Continue;

                    if (isMatchSetup || matchModeOnly)
                    {
                        CsTeam team = GetPlayerTeam(player);
                        if (team == CsTeam.None && isWhitelistRequired)
                        {
                            Log($"[EventPlayerConnectFull] KICKING PLAYER STEAMID: {steamId}, Name: {player.PlayerName} (NOT ALLOWED!)");
                            PrintToAllChat($"Kicking player {player.PlayerName} - Not a player in this game.");
                            KickPlayer(player);
                            return HookResult.Continue;
                        }
                    }
                }

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

        public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)
        {
            try
            {
                Log($"[EventCsWinPanelMatch] Match end. Resolving scores from engine (Non-Steam Environment)...");

                // 1. 直接從引擎獲取當前數據，不依賴 MatchZy 內部的隊伍邏輯
                var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
                string engineCtTeamName = "";
                int engineCtScore = 0;
                int engineTScore = 0;

                foreach (var team in teams)
                {
                    if (team.TeamNum == (int)CsTeam.CounterTerrorist)
                    {
                        engineCtTeamName = team.Teamname;
                        engineCtScore = team.Score;
                    }
                    else if (team.TeamNum == (int)CsTeam.Terrorist)
                    {
                        engineTScore = team.Score;
                    }
                }

                // 2. 校正邏輯：比對 engineCtTeamName 是否等於 matchzyTeam1.teamName
                int t1MapScore = 0;
                int t2MapScore = 0;
                string winnerName = "Draw";

                if (engineCtTeamName == matchzyTeam1.teamName)
                {
                    t1MapScore = engineCtScore;
                    t2MapScore = engineTScore;
                }
                else
                {
                    // 代表 Team2 目前是 CT
                    t1MapScore = engineTScore;
                    t2MapScore = engineCtScore;
                }

                // 3. 決定贏家字串 (用於 EndSeries)
                if (t1MapScore > t2MapScore) winnerName = matchzyTeam1.teamName;
                else if (t2MapScore > t1MapScore) winnerName = matchzyTeam2.teamName;

                Log($"[EventCsWinPanelMatch] Final Scores -> T1({matchzyTeam1.teamName}): {t1MapScore}, T2({matchzyTeam2.teamName}): {t2MapScore}");

                // 4. 更新狀態並調用 EndSeries
                // 根據 MatchManagement.cs 定義：EndSeries(winnerName, restartDelay, t1score, t2score)
                isMatchLive = false;
                HandleMatchEnd();
                
                // 強制延遲 15 秒後換圖/重置
                EndSeries(winnerName, 15, t1MapScore, t2MapScore);

                return HookResult.Continue;
            }
            catch (Exception e)
            {
                Log($"[EventCsWinPanelMatch FATAL] An error occurred: {e.Message}");
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
            catch (Exception e)
            {
                Log($"[EventRoundFreezeEnd FATAL] An error occurred: {e.Message}");
                return HookResult.Continue;
            }
        }

        public HookResult EventPlayerGivenC4(EventPlayerGivenC4 @event, GameEventInfo info) {
            try {
                if (!matchStarted) return HookResult.Continue;
                if (@event.Userid == null) return HookResult.Continue;
                var recv = @event.Userid;

                var coaches = reverseTeamSides["TERRORIST"].coach;
                if (coaches.Contains(recv)) {
                    TransferCoachBomb(recv);
                }
            } catch (Exception e) {
                Log($"[EventPlayerGivenC4 FATAL] An error occured: {e.Message}");
            }
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

                    if (!projectile.IsValid ||
                        !projectile.Thrower.IsValid ||
                        projectile.Thrower.Value == null ||
                        projectile.Thrower.Value.Controller.Value == null ||
                        projectile.Globalname == "custom"
                    ) return;

                    CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                    if(!player.IsValid || player.PlayerPawn.Value == null || !player.PlayerPawn.IsValid) return;
                    int client = player.UserId!.Value;
                    
                    Vector position = new(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z);
                    QAngle angle = new(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z);
                    Vector velocity = new(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z);
                    string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];

                    if (!lastGrenadesData.ContainsKey(client)) {
                        lastGrenadesData[client] = new();
                    }

                    if (!nadeSpecificLastGrenadeData.ContainsKey(client))
                    {
                        nadeSpecificLastGrenadeData[client] = new(){};
                    }

                    GrenadeThrownData lastGrenadeThrown = new(
                        position, 
                        angle, 
                        velocity, 
                        player.PlayerPawn.Value.CBodyComponent!.SceneNode!.AbsOrigin, 
                        player.PlayerPawn.Value.EyeAngles,
                        nadeType,
                        DateTime.Now,
                        projectile.ItemIndex
                    );

                    nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
                    lastGrenadesData[client].Add(lastGrenadeThrown);

                    if (maxLastGrenadesSavedLimit != 0 && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit)
                    {
                        lastGrenadesData[client].RemoveAt(0);
                    }

                    lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
                    if (smokeColorEnabled.Value && nadeType == "smoke")
                    {
                        CSmokeGrenadeProjectile smokeProjectile = new(entity.Handle);
                        smokeProjectile.SmokeColor.X = GetPlayerTeammateColor(player).R;
                        smokeProjectile.SmokeColor.Y = GetPlayerTeammateColor(player).G;
                        smokeProjectile.SmokeColor.Z = GetPlayerTeammateColor(player).B;
                    }
                });
            }
            catch (Exception e)
            {
                Log($"[OnEntitySpawnedHandler FATAL] An error occurred: {e.Message}");
            }
        }

        public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event, GameEventInfo info)
        {
            try
            {
                if (!matchStarted) return HookResult.Continue;

                if (@event.Attacker == @event.Userid)
                {
                    if (matchzyTeam1.coach.Contains(@event.Attacker!) || matchzyTeam2.coach.Contains(@event.Attacker!))
                    {
                        info.DontBroadcast = true;
                    }
                }
                return HookResult.Continue;
            }
            catch (Exception e)
            {
                Log($"[EventPlayerDeathPreHandler FATAL] An error occurred: {e.Message}");
                return HookResult.Continue;
            }
        }

        public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event, GameEventInfo info)
        {
            if (!isPractice || isDryRun) return HookResult.Continue;
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
            {
                PrintToPlayerChat(player!, Localizer["matchzy.pracc.smoke", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
                lastGrenadeThrownTime.Remove(@event.Entityid);
            }
            return HookResult.Continue;
        }

        public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event, GameEventInfo info)
        {
            if (!isPractice || isDryRun) return HookResult.Continue;
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
            {
                PrintToPlayerChat(player!, Localizer["matchzy.pracc.flash", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
                lastGrenadeThrownTime.Remove(@event.Entityid);
            }
            return HookResult.Continue;
        }

        public HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
        {
            if (!isPractice || isDryRun) return HookResult.Continue;
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
            {
                PrintToPlayerChat(player!, Localizer["matchzy.pracc.grenade", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
                lastGrenadeThrownTime.Remove(@event.Entityid);
            }
            return HookResult.Continue;
        }

        public HookResult EventMolotovDetonateHandler(EventMolotovDetonate @event, GameEventInfo info)
        {
            if (!isPractice || isDryRun) return HookResult.Continue;
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            if(lastGrenadeThrownTime.TryGetValue(@event.Get<int>("entityid"), out var thrownTime)) 
            {
                PrintToPlayerChat(player!, Localizer["matchzy.pracc.molotov", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
            }
            return HookResult.Continue;
        }

        public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event, GameEventInfo info)
        {
            if (!isPractice || isDryRun) return HookResult.Continue;
            CCSPlayerController? player = @event.Userid;
            if (!IsPlayerValid(player)) return HookResult.Continue;
            if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var thrownTime)) 
            {
                PrintToPlayerChat(player!, Localizer["matchzy.pracc.decoy", player!.PlayerName, $"{(DateTime.Now - thrownTime).TotalSeconds:0.00}"]);
                lastGrenadeThrownTime.Remove(@event.Entityid);
            }
            return HookResult.Continue;
        }
    }
}
