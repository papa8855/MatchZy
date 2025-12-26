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

        public override string ModuleVersion => "0.8.15-Bypass";

        public override string ModuleAuthor => "WD- (Modified)";

        public override string ModuleDescription => "Whitelist Bypass Version";

        public string chatPrefix = $"[{ChatColors.Green}MatchZy{ChatColors.Default}]";
        public string adminChatPrefix = $"[{ChatColors.Red}ADMIN{ChatColors.Default}]";

        // Plugin start phase data
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
            { "ct", false },
            { "t", false },
            { "pauseTeam", "" }
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

            // --- 核心修改：攔截所有白名單相關的踢人動作 ---
            isWhitelistRequired = false; 

            AddCommandListener("kickid", (player, info) => {
                if (info.ArgString.Contains("not part of this match")) {
                    Console.WriteLine("[MatchZy] 偵測到路人玩家，已攔截踢人指令！");
                    return HookResult.Handled; 
                }
                return HookResult.Continue;
            });
            // ------------------------------------------

            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;

            if (!hotReload)
            {
                AutoStart();
            }
            else
            {
                UpdatePlayersMap();
                AutoStart();
            }

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
                if (!IsPlayerValid(player) || player!.IsHLTV || player.IsBot) return HookResult.Continue;

                // 呼叫修改過的 GetPlayerTeam，這會讓路人直接選隊成功
                CsTeam teamToAssign = GetPlayerTeam(player);
                SwitchPlayerTeam(player, teamToAssign);

                return HookResult.Continue;
            });

            AddCommandListener("jointeam", (player, info) =>
            {
                // 放行所有隊伍加入限制
                return HookResult.Continue;
            });

            AddCommandListener("noclip", OnConsoleNoClip);
            
            // ... 其餘事件處理保持不變 ...

            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] Custom MatchZy - Whitelist Fully Disabled.");
        }
    }
}
