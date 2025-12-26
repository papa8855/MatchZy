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
        public override string ModuleVersion => "0.8.15-AntiKick";

        public override string ModuleAuthor => "WD- / Mod by papa8855";
        public override string ModuleDescription => "A plugin for running and managing CS2 matches - Whitelist Disabled!";

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
        // Pause Data
        public bool isPaused = false;
        public Dictionary<string, object> unpauseData = new Dictionary<string, object> {
            { "ct", false },
            { "t", false },
            { "pauseTeam", "" }
        };
        bool isPauseCommandForTactical = false;

        // Knife Data
        public int knifeWinner = 0;
        public string knifeWinnerName = "";

        // Players Data (including admins)
        public int connectedPlayers = 0;
        private Dictionary<int, bool> playerReadyStatus = new Dictionary<int, bool>();
        private Dictionary<int, CCSPlayerController> playerData = new Dictionary<int, CCSPlayerController>();
        // Admin Data
        private Dictionary<string, string> loadedAdmins = new Dictionary<string, string>();
        // Timers
        public CounterStrikeSharp.API.Modules.Timers.Timer? unreadyPlayerMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? sideSelectionMessageTimer = null;
        public CounterStrikeSharp.API.Modules.Timers.Timer? pausedStateTimer = null;

        public int chatTimerDelay = 13;
        // Game Config
        public bool isKnifeRequired = true;
        public int minimumReadyRequired = 2;
        
        // 修改點 1：預設不啟用白名單
        public bool isWhitelistRequired = false; 
        
        public bool isSaveNadesAsGlobalEnabled = false;
        public bool isPlayOutEnabled = false;
        public bool playerHasTakenDamage = false;
        public Dictionary<string, Action<CCSPlayerController?, CommandInfo?>>? commandActions;
        private Database database = new();

        public override void Load(bool hotReload) {
            
            LoadAdmins();
            database.InitializeDatabase(ModuleDirectory);

            Server.ExecuteCommand("execifexists MatchZy/config.cfg");
            teamSides[matchzyTeam1] = "CT";
            teamSides[matchzyTeam2] = "TERRORIST";
            reverseTeamSides["CT"] = matchzyTeam1;
            reverseTeamSides["TERRORIST"] = matchzyTeam2;

            if (!hotReload) {
                AutoStart();
            } else {
                UpdatePlayersMap();
                AutoStart();
            }

            commandActions = new Dictionary<string, Action<CCSPlayerController?, CommandInfo?>> {
                { ".ready", OnPlayerReady },
                { ".r", OnPlayerReady },
                { ".forceready", OnForceReadyCommandCommand },
                { ".unready", OnPlayerUnReady },
                { ".notready", OnPlayerUnReady },
                { ".ur", OnPlayerUnReady },
                { ".stay", OnTeamStay },
                { ".switch", OnTeamSwitch },
                { ".swap", OnTeamSwitch },
                { ".tech", OnTechCommand },
                { ".p", OnPauseCommand },
                { ".pause", OnPauseCommand },
                { ".unpause", OnUnpauseCommand },
                { ".up", OnUnpauseCommand },
                { ".forcepause", OnForcePauseCommand },
                { ".fp", OnForcePauseCommand },
                { ".forceunpause", OnForceUnpauseCommand },
                { ".fup", OnForceUnpauseCommand },
                { ".tac", OnTacCommand },
                { ".roundknife", OnKnifeCommand },
                { ".rk", OnKnifeCommand },
                { ".playout", OnPlayoutCommand },
                { ".start", OnStartCommand },
                { ".force", OnStartCommand },
                { ".forcestart", OnStartCommand },
                { ".skipveto", OnSkipVetoCommand },
                { ".sv", OnSkipVetoCommand },
                { ".restart", OnRestartMatchCommand },
                { ".rr", OnRestartMatchCommand },
                { ".endmatch", OnEndMatchCommand },
                { ".forceend", OnEndMatchCommand },
                { ".reloadmap", OnMapReloadCommand },
                { ".settings", OnMatchSettingsCommand },
                { ".whitelist", OnWLCommand },
                { ".globalnades", OnSaveNadesAsGlobalCommand },
                { ".reload_admins", OnReloadAdmins },
                { ".tactics", OnPracCommand },
                { ".prac", OnPracCommand },
                { ".showspawns", OnShowSpawnsCommand },
                { ".hidespawns", OnHideSpawnsCommand },
                { ".dryrun", OnDryRunCommand },
                { ".dry", OnDryRunCommand },
                { ".noflash", OnNoFlashCommand },
                { ".noblind", OnNoFlashCommand },
                { ".break", OnBreakCommand },
                { ".bot", OnBotCommand },
                { ".cbot", OnCrouchBotCommand },
                { ".crouchbot", OnCrouchBotCommand },
                { ".boost", OnBoostBotCommand },
                { ".crouchboost", OnCrouchBoostBotCommand },
                { ".nobots", OnNoBotsCommand },
                { ".solid", OnSolidCommand },
                { ".impacts", OnImpactsCommand },
                { ".traj", OnTrajCommand },
                { ".pip", OnTrajCommand },
                { ".god", OnGodCommand },
                { ".ff", OnFastForwardCommand },
                { ".fastforward", OnFastForwardCommand },
                { ".clear", OnClearCommand },
                { ".match", OnMatchCommand },
                { ".uncoach", OnUnCoachCommand },
                { ".exitprac", OnMatchCommand },
                { ".stop", OnStopCommand },
                { ".help", OnHelpCommand },
                { ".t", OnTCommand },
                { ".ct", OnCTCommand },
                { ".spec", OnSpecCommand },
                { ".fas", OnFASCommand },
                { ".watchme", OnFASCommand },
                { ".last", OnLastCommand },
                { ".throw", OnRethrowCommand },
                { ".rethrow", OnRethrowCommand },
                { ".rt", OnRethrowCommand },
                { ".throwsmoke", OnRethrowSmokeCommand },
                { ".rethrowsmoke", OnRethrowSmokeCommand },
                { ".thrownade", OnRethrowGrenadeCommand },
                { ".rethrownade", OnRethrowGrenadeCommand },
                { ".rethrowgrenade", OnRethrowGrenadeCommand },
                { ".throwgrenade", OnRethrowGrenadeCommand },
                { ".rethrowflash", OnRethrowFlashCommand },
                { ".throwflash", OnRethrowFlashCommand },
                { ".rethrowdecoy", OnRethrowDecoyCommand },
                { ".throwdecoy", OnRethrowDecoyCommand },
                { ".throwmolotov", OnRethrowMolotovCommand },
                { ".rethrowmolotov", OnRethrowMolotovCommand },
                { ".timer", OnTimerCommand },
                { ".lastindex", OnLastIndexCommand },
                { ".bestspawn", OnBestSpawnCommand },
                { ".worstspawn", OnWorstSpawnCommand },
                { ".bestctspawn", OnBestCTSpawnCommand },
                { ".worstctspawn", OnWorstCTSpawnCommand },
                { ".besttspawn", OnBestTSpawnCommand },
                { ".worsttspawn", OnWorstTSpawnCommand },
                { ".savepos", OnSavePosCommand},
                { ".loadpos", OnLoadPosCommand}
            };

            // 修改點 2：攔截玩家連線，強制關閉白名單檢查
            RegisterEventHandler<EventPlayerConnectFull>((@event, info) => {
                isWhitelistRequired = false;
                return EventPlayerConnectFullHandler(@event, info);
            }, HookMode.Pre);

            RegisterEventHandler<EventPlayerDisconnect>(EventPlayerDisconnectHandler);
            RegisterEventHandler<EventCsWinPanelRound>(EventCsWinPanelRoundHandler, hookMode: HookMode.Pre);
            RegisterEventHandler<EventCsWinPanelMatch>(EventCsWinPanelMatchHandler);
            RegisterEventHandler<EventRoundStart>(EventRoundStartHandler);
            RegisterEventHandler<EventRoundFreezeEnd>(EventRoundFreezeEndHandler);
            RegisterEventHandler<EventPlayerGivenC4>(EventPlayerGivenC4);
            RegisterEventHandler<EventPlayerDeath>(EventPlayerDeathPreHandler, hookMode: HookMode.Pre);
            RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawnedHandler);

            RegisterEventHandler<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            RegisterEventHandler<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventMolotovDetonate>(EventMolotovDetonateHandler);
            RegisterEventHandler<EventDecoyStarted>(EventDecoyDetonateHandler);

            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] Anti-Kick Mod by papa8855");
        }
    }
}
