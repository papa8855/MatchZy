using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Events;

namespace MatchZy
{
    [MinimumApiVersion(227)]
    public partial class MatchZy : BasePlugin
    {
        public override string ModuleName => "MatchZy";
        public override string ModuleVersion => "0.8.15-Fixed";
        public override string ModuleAuthor => "WD- (Modified for No-Whitelist)";
        public override string ModuleDescription => "A plugin for running matches without restrictions!";

        public string chatPrefix = $"[{ChatColors.Green}MatchZy{ChatColors.Default}]";
        public string adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";

        public bool isPractice = false;
        public bool isSleep = false;
        public bool readyAvailable = false;
        public bool matchStarted = false;
        public bool isWarmup = false;
        public bool isKnifeRound = false;
        public bool isSideSelectionPhase = false;
        public bool isMatchLive = false;
        public long liveMatchId = -1;
        public int autoStartMode = 1;
        public bool mapReloadRequired = false;
        public bool isPaused = false;
        public Dictionary<string, object> unpauseData = new Dictionary<string, object> {
            { "ct", false }, { "t", false }, { "pauseTeam", "" }
        };
        bool isPauseCommandForTactical = false;
        public int knifeWinner = 0;
        public string knifeWinnerName = "";
        public int connectedPlayers = 0;
        private Dictionary<int, bool> playerReadyStatus = new Dictionary<int, bool>();
        private Dictionary<int, CCSPlayerController> playerData = new Dictionary<int, CCSPlayerController>();
        private Dictionary<string, string> loadedAdmins = new Dictionary<string, string>();
        public CounterStrikeSharp.API.Modules.Timers.Timer? unreadyPlayerMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? sideSelectionMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? pausedStateTimer = null;
        public int chatTimerDelay = 13;
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2;
        public bool isWhitelistRequired = false;
        public bool isSaveNadesAsGlobalEnabled = false;
        public bool isPlayOutEnabled = false;
        public bool playerHasTakenDamage = false;
        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;
        private Database database = new();

        public override void Load(bool hotReload)
        {
            LoadAdmins();
            database.InitializeDatabase(ModuleDirectory);
            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            // 強制關閉白名單
            isWhitelistRequired = false;

            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;

            if (!hotReload) AutoStart();
            else { UpdatePlayersMap(); AutoStart(); }

            commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>> {
                { ".ready", OnPlayerReady }, { ".r", OnPlayerReady }, { ".forceready", OnForceReadyCommandCommand },
                { ".unready", OnPlayerUnReady }, { ".notready", OnPlayerUnReady }, { ".ur", OnPlayerUnReady },
                { ".stay", OnTeamStay }, { ".switch", OnTeamSwitch }, { ".swap", OnTeamSwitch },
                { ".tech", OnTechCommand }, { ".p", OnPauseCommand }, { ".pause", OnPauseCommand },
                { ".unpause", OnUnpauseCommand }, { ".up", OnUnpauseCommand },
                { ".forcepause", OnForcePauseCommand }, { ".fp", OnForcePauseCommand },
                { ".forceunpause", OnForceUnpauseCommand }, { ".fup", OnForceUnpauseCommand },
                { ".tac", OnTacCommand }, { ".roundknife", OnKnifeCommand }, { ".rk", OnKnifeCommand },
                { ".playout", OnPlayoutCommand }, { ".start", OnStartCommand }, { ".force", OnStartCommand },
                { ".forcestart", OnStartCommand }, { ".skipveto", OnSkipVetoCommand }, { ".sv", OnSkipVetoCommand },
                { ".restart", OnRestartMatchCommand }, { ".rr", OnRestartMatchCommand },
                { ".endmatch", OnEndMatchCommand }, { ".forceend", OnEndMatchCommand },
                { ".reloadmap", OnMapReloadCommand }, { ".settings", OnMatchSettingsCommand },
                { ".whitelist", OnWLCommand }, { ".globalnades", OnSaveNadesAsGlobalCommand },
                { ".reload_admins", OnReloadAdmins }, { ".tactics", OnPracCommand }, { ".prac", OnPracCommand },
                { ".showspawns", OnShowSpawnsCommand }, { ".hidespawns", OnHideSpawnsCommand },
                { ".dryrun", OnDryRunCommand }, { ".dry", OnDryRunCommand }, { ".noflash", OnNoFlashCommand },
                { ".noblind", OnNoFlashCommand }, { ".break", OnBreakCommand }, { ".bot", OnBotCommand },
                { ".cbot", OnCrouchBotCommand }, { ".crouchbot", OnCrouchBotCommand }, { ".boost", OnBoostBotCommand },
                { ".crouchboost", OnCrouchBoostBotCommand }, { ".nobots", OnNoBotsCommand },
                { ".solid", OnSolidCommand }, { ".impacts", OnImpactsCommand }, { ".traj", OnTrajCommand },
                { ".pip", OnTrajCommand }, { ".god", OnGodCommand }, { ".ff", OnFastForwardCommand },
                { ".fastforward", OnFastForwardCommand }, { ".clear", OnClearCommand }, { ".match", OnMatchCommand },
                { ".uncoach", OnUnCoachCommand }, { ".exitprac", OnMatchCommand }, { ".stop", OnStopCommand },
                { ".help", OnHelpCommand }, { ".t", OnTCommand }, { ".ct", OnCTCommand }, { ".spec", OnSpecCommand },
                { ".fas", OnFASCommand }, { ".watchme", OnFASCommand }, { ".last", OnLastCommand },
                { ".throw", OnRethrowCommand }, { ".rethrow", OnRethrowCommand }, { ".rt", OnRethrowCommand },
                { ".throwsmoke", OnRethrowSmokeCommand }, { ".rethrowsmoke", OnRethrowSmokeCommand },
                { ".thrownade", OnRethrowGrenadeCommand }, { ".rethrownade", OnRethrowGrenadeCommand },
                { ".rethrowgrenade", OnRethrowGrenadeCommand }, { ".throwgrenade", OnRethrowGrenadeCommand },
                { ".rethrowflash", OnRethrowFlashCommand }, { ".throwflash", OnRethrowFlashCommand },
                { ".rethrowdecoy", OnRethrowDecoyCommand }, { ".throwdecoy", OnRethrowDecoyCommand },
                { ".throwmolotov", OnRethrowMolotovCommand }, { ".rethrowmolotov", OnRethrowMolotovCommand },
                { ".timer", OnTimerCommand }, { ".lastindex", OnLastIndexCommand },
                { ".bestspawn", OnBestSpawnCommand }, { ".worstspawn", OnWorstSpawnCommand },
                { ".bestctspawn", OnBestCTSpawnCommand }, { ".worstctspawn", OnWorstCTSpawnCommand },
                { ".besttspawn", OnBestTSpawnCommand }, { ".worsttspawn", OnWorstTSpawnCommand },
                { ".savepos", OnSavePosCommand}, { ".loadpos", OnLoadPosCommand}
            };

            RegisterEventHandler<EventPlayerConnectFull>(EventPlayerConnectFullHandler);
            RegisterEventHandler<EventPlayerDisconnect>(EventPlayerDisconnectHandler);
            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);

            RegisterEventHandler<EventPlayerTeam>((@event, info) => {
                CCSPlayerController? player = @event.Userid;
                if (!IsPlayerValid(player)) return HookResult.Continue;
                if (matchzyTeam1.coach.Contains(player!) || matchzyTeam2.coach.Contains(player!)) {
                    @event.Silent = true;
                    return HookResult.Changed;
                }
                return HookResult.Continue;
            }, HookMode.Pre);

            RegisterEventHandler<EventPlayerTeam>((@event, info) =>
            {
                if (!isMatchSetup && !isVeto) return HookResult.Continue;
                CCSPlayerController? player = @event.Userid;
                if (!IsPlayerValid(player)) return HookResult.Continue;
                if (player!.IsHLTV || player.IsBot) return HookResult.Continue;

                // --- 修改重點：內聯判定邏輯，不使用會造成重複定義的函數 ---
                CsTeam teamToAssign = player.Team; 
                string sId = player.SteamID.ToString();
                if (matchConfig.Team1.Players.ContainsKey(sId)) teamToAssign = CsTeam.Terrorist;
                else if (matchConfig.Team2.Players.ContainsKey(sId)) teamToAssign = CsTeam.CounterTerrorist;
                // 如果都沒有，就維持 player.Team (路人選哪隊就是哪隊)

                SwitchPlayerTeam(player, teamToAssign);
                return HookResult.Continue;
            });

            AddCommandListener("jointeam", (player, info) =>
            {
                return HookResult.Continue; // 放行選隊限制
            });

            AddCommandListener("noclip", OnConsoleNoClip);

            RegisterEventHandler<EventRoundEnd>((@event, info) => 
            {
                if (!isKnifeRound) return HookResult.Continue;
                DetermineKnifeWinner();
                @event.Winner = knifeWinner;
                int finalEvent = 10;
                if (knifeWinner == 3) finalEvent = 8;
                else if (knifeWinner == 2) finalEvent = 9;
                @event.Reason = finalEvent;
                isSideSelectionPhase = true;
                isKnifeRound = false;
                StartAfterKnifeWarmup();
                return HookResult.Changed;
            }, HookMode.Pre);

            RegisterEventHandler<EventRoundEnd>((@event, info) => {
                try {
                    if (isDryRun) { StartPracticeMode(); isDryRun = false; return HookResult.Continue; }
                    if (!isMatchLive) return HookResult.Continue;
                    HandlePostRoundEndEvent(@event);
                    return HookResult.Continue;
                } catch (Exception e) {
                    Log($"[EventRoundEnd FATAL] Error: {e.Message}");
                    return HookResult.Continue;
                }
            }, HookMode.Post);

            RegisterListener<Listeners.OnMapStart>(mapName => { 
                AddTimer(1.0f, () => {
                    if (!isMatchSetup) { AutoStart(); return; }
                    if (isWarmup) StartWarmup();
                    if (isPractice) StartPracticeMode();
                });
            });

            RegisterEventHandler<EventPlayerDeath>((@event, info) => {
                var player = @event.Userid;
                if (!isWarmup || !IsPlayerValid(player)) return HookResult.Continue;
                if (player!.InGameMoneyServices != null) player.InGameMoneyServices.Account = 16000;
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerHurt>((@event, info) =>
			{
				CCSPlayerController? attacker = @event.Attacker;
                CCSPlayerController? victim = @event.Userid;
                if (!IsPlayerValid(attacker) || !IsPlayerValid(victim)) return HookResult.Continue;
                if (isPractice && victim!.IsBot) {
                    PrintToPlayerChat(attacker!, Localizer["matchzy.pracc.damage", @event.DmgHealth, victim.PlayerName, @event.Health]);
                    return HookResult.Continue;
                }
				if (!attacker!.IsValid || (attacker.IsBot && !(@event.DmgHealth > 0 || @event.DmgArmor > 0))) return HookResult.Continue;
                if (matchStarted && victim!.TeamNum != attacker.TeamNum) {
                    UpdatePlayerDamageInfo(@event, (int)victim.UserId!);
                    if (attacker != victim) playerHasTakenDamage = true;
                }
				return HookResult.Continue;
			});

            RegisterEventHandler<EventPlayerChat>((@event, info) => {
                int index = @event.Userid + 1;
                var playerUserId = NativeAPI.GetUseridFromIndex(index);
                var originalMessage = @event.Text.Trim();
                var message = @event.Text.Trim().ToLower();
                var parts = originalMessage.Split(' ');
                var messageCommandArg = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

                CCSPlayerController? player = null;
                if (playerData.TryGetValue(playerUserId, out CCSPlayerController? v)) player = v;
                if (player == null) { UpdatePlayersMap(); player = playerData[playerUserId]; }

                if (commandActions.ContainsKey(message)) commandActions[message](player, null);
                if (message.StartsWith(".map")) HandleMapChangeCommand(player, messageCommandArg);
                if (message.StartsWith(".readyrequired")) HandleReadyRequiredCommand(player, messageCommandArg);
                if (message.StartsWith(".restore")) HandleRestoreCommand(player, messageCommandArg);
                if (message.StartsWith(".asay")) {
                    if (IsPlayerAdmin(player, "css_asay", "@css/chat")) {
                        if (messageCommandArg != "") Server.PrintToChatAll($"{adminChatPrefix} {messageCommandArg}");
                        else ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".asay <message>"]);
                    } else SendPlayerNotAdminMessage(player);
                }
                if (message.StartsWith(".savenade") || message.StartsWith(".sn")) HandleSaveNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".delnade") || message.StartsWith(".dn")) HandleDeleteNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".importnade") || message.StartsWith(".in")) HandleImportNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".listnades") || message.StartsWith(".lin")) HandleListNadesCommand(player, messageCommandArg);
                if (message.StartsWith(".loadnade") || message.StartsWith(".ln")) HandleLoadNadeCommand(player, messageCommandArg);
                if (message.StartsWith(".spawn")) HandleSpawnCommand(player, messageCommandArg, player.TeamNum, "spawn");
                if (message.StartsWith(".ctspawn") || message.StartsWith(".cts")) HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.CounterTerrorist, "ctspawn");
                if (message.StartsWith(".tspawn") || message.StartsWith(".ts")) HandleSpawnCommand(player, messageCommandArg, (byte)CsTeam.Terrorist, "tsspawn");
                if (message.StartsWith(".team1")) HandleTeamNameChangeCommand(player, messageCommandArg, 1);
                if (message.StartsWith(".team2")) HandleTeamNameChangeCommand(player, messageCommandArg, 2);
                if (message.StartsWith(".rcon")) {
                    if (IsPlayerAdmin(player, "css_rcon", "@css/rcon")) { Server.ExecuteCommand(messageCommandArg); ReplyToUserCommand(player, "Command sent!"); }
                    else SendPlayerNotAdminMessage(player);
                }
                if (message.StartsWith(".coach")) HandleCoachCommand(player, messageCommandArg);
                if (message.StartsWith(".ban")) HandeMapBanCommand(player, messageCommandArg);
                if (message.StartsWith(".pick")) HandeMapPickCommand(player, messageCommandArg);
                if (message.StartsWith(".back")) HandleBackCommand(player, messageCommandArg);
                if (message.StartsWith(".delay")) HandleDelayCommand(player, messageCommandArg);
                if (message.StartsWith(".throwindex")) HandleThrowIndexCommand(player, messageCommandArg);

                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerBlind>((@event, info) => {
                CCSPlayerController? player = @event.Userid;
                CCSPlayerController? attacker = @event.Attacker;
                if (!isPractice || !IsPlayerValid(player) || !IsPlayerValid(attacker)) return HookResult.Continue;
                if (attacker!.IsValid) PrintToPlayerChat(attacker, Localizer["matchzy.pracc.blind", player!.PlayerName, Math.Round(@event.BlindDuration, 2)]);
                if (player!.UserId != null && noFlashList.Contains((int)player.UserId)) Server.NextFrame(() => KillFlashEffect(player));
                return HookResult.Continue;
            });

            RegisterEventHandler<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            RegisterEventHandler<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventMolotovDetonate>(EventMolotovDetonateHandler);
            RegisterEventHandler<EventDecoyStarted>(EventDecoyDetonateHandler);

            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] Whitelist Bypassed.");
        }
    }
}
