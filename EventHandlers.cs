using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Cvars; // 解決 ConVar 報錯
using CounterStrikeSharp.API.Modules.Entities; // 解決 Utilities 報錯
using System.Linq;

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
                playerReadyStatus[player.UserId.Value] = (readyAvailable && !matchStarted) ? false : true;
            }
            if (readyAvailable && !matchStarted && GetRealPlayersCount() == 1)
            {
                Log($"[FULL CONNECT] First player has connected, starting warmup!");
                ExecUnpracCommands();
                AutoStart();
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
            if (!IsPlayerValid(player) || !player!.UserId.HasValue) return HookResult.Continue;
            int userId = player.UserId.Value;

            if (playerReadyStatus.ContainsKey(userId)) playerReadyStatus.Remove(userId);
            playerData.Remove(userId);

            if (matchzyTeam1.coach.Contains(player))
            {
                matchzyTeam1.coach.Remove(player);
                SetPlayerVisible(player);
            }
            else if (matchzyTeam2.coach.Contains(player))
            {
                matchzyTeam2.coach.Remove(player);
                SetPlayerVisible(player);
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
        // 修正：使用標準格式，目前不需要處理此事件，直接回傳繼續
        return HookResult.Continue;
    }

    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        try
        {
            // 修正：改用 FindAllEntitiesByDesignerName，解決 Utilities.GetEntities 報錯
            int ctScore = 0;
            int tScore = 0;
            
            // 與 Utility.cs 保持一致，抓取 cs_team_manager
            var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            foreach (var team in teams) {
                if (team.TeamNum == (byte)CsTeam.CounterTerrorist) ctScore = team.Score;
                else if (team.TeamNum == (byte)CsTeam.Terrorist) tScore = team.Score;
            }

            // 抓取伺服器目前隊名，解決換邊反轉問題
            string name1 = ConVar.Find("mp_teamname_1")?.StringValue ?? ""; 
            string name2 = ConVar.Find("mp_teamname_2")?.StringValue ?? "";
            
            string? realWinnerName = null;
            if (ctScore > tScore) realWinnerName = name1;
            else if (tScore > ctScore) realWinnerName = name2;

            // 呼叫 EndSeries
            EndSeries(realWinnerName, 10, ctScore, tScore);

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
            Log($"[EventRoundStart FATAL] {e.Message}");
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
                if (!IsPlayerValid(coach) || coach.PlayerPawn.Value?.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;
                coach.ChangeTeam(CsTeam.Spectator);
                AddTimer(0.01f, () => coach.ChangeTeam(GetCoachTeam(coach)));
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundFreezeEnd FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerGivenC4(EventPlayerGivenC4 @event, GameEventInfo info) 
    {
        if (matchStarted && @event.Userid != null && reverseTeamSides["TERRORIST"].coach.Contains(@event.Userid))
        {
            TransferCoachBomb(@event.Userid);
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
                if (!projectile.IsValid || projectile.Thrower.Value?.Controller.Value == null) return;
                CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                if(!player.IsValid || player.UserId == null) return;
                
                int client = player.UserId.Value;
                string nadeType = Constants.ProjectileTypeMap[entity.Entity.DesignerName];

                if (!lastGrenadesData.ContainsKey(client)) lastGrenadesData[client] = new();
                if (!nadeSpecificLastGrenadeData.ContainsKey(client)) nadeSpecificLastGrenadeData[client] = new();

                GrenadeThrownData lastGrenadeThrown = new(
                    new Vector(projectile.AbsOrigin!.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z), 
                    new QAngle(projectile.AbsRotation!.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z), 
                    new Vector(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z), 
                    player.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin, 
                    player.PlayerPawn.Value.EyeAngles,
                    nadeType,
                    DateTime.Now,
                    projectile.ItemIndex
                );

                nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
                lastGrenadesData[client].Add(lastGrenadeThrown);
                lastGrenadeThrownTime[(int)projectile.Index] = DateTime.Now;
            });
        }
        catch (Exception e) { Log($"[OnEntitySpawnedHandler FATAL] {e.Message}"); }
    }

    public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event, GameEventInfo info)
    {
        if (matchStarted && @event.Attacker == @event.Userid && (matchzyTeam1.coach.Contains(@event.Attacker!) || matchzyTeam2.coach.Contains(@event.Attacker!)))
        {
            info.DontBroadcast = true;
        }
        return HookResult.Continue;
    }

    public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (isPractice && IsPlayerValid(@event.Userid) && lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var t)) 
        {
            PrintToPlayerChat(@event.Userid!, Localizer["matchzy.pracc.smoke", @event.Userid!.PlayerName, $"{(DateTime.Now - t).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (isPractice && IsPlayerValid(@event.Userid) && lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var t)) 
        {
            PrintToPlayerChat(@event.Userid!, Localizer["matchzy.pracc.flash", @event.Userid!.PlayerName, $"{(DateTime.Now - t).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (isPractice && IsPlayerValid(@event.Userid) && lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var t)) 
        {
            PrintToPlayerChat(@event.Userid!, Localizer["matchzy.pracc.grenade", @event.Userid!.PlayerName, $"{(DateTime.Now - t).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventMolotovDetonateHandler(EventMolotovDetonate @event, GameEventInfo info)
    {
        if (isPractice && IsPlayerValid(@event.Userid) && lastGrenadeThrownTime.TryGetValue(@event.Get<int>("entityid"), out var t)) 
        {
            PrintToPlayerChat(@event.Userid!, Localizer["matchzy.pracc.molotov", @event.Userid!.PlayerName, $"{(DateTime.Now - t).TotalSeconds:0.00}"]);
        }
        return HookResult.Continue;
    }

    public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event, GameEventInfo info)
    {
        if (isPractice && IsPlayerValid(@event.Userid) && lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var t)) 
        {
            PrintToPlayerChat(@event.Userid!, Localizer["matchzy.pracc.decoy", @event.Userid!.PlayerName, $"{(DateTime.Now - t).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }
}
