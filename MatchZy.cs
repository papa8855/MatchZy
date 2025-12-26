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

        public override string ModuleVersion => "0.8.15";

        public override string ModuleAuthor => "WD- (https://github.com/shobhit-pathak/)";

        public override string ModuleDescription => "A plugin for running and managing CS2 practice/pugs/scrims/matches!";

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
        private Database database = new();

        // 這裡開始接續剩下的內容
        public override void Load(bool hotReload) {
            
            database.InitializeDatabase(ModuleDirectory);

            // 修改點：強制關閉白名單，讓路人可以進來
            isWhitelistRequired = false;

            // 修改點：攔截 kickid 指令，防止系統因為路人不在名單內而踢人
            AddCommandListener("kickid", (player, info) => {
                if (info.ArgString.Contains("not part of this match")) return HookResult.Handled;
                return HookResult.Continue;
            });

            Server.ExecuteCommand("execifexists MatchZy/config.cfg");

            RegisterEventHandler<EventPlayerConnectFull>((@event, info) => {
                UpdatePlayersMap();
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerDisconnect>((@event, info) => {
                UpdatePlayersMap();
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerSpawn>((@event, info) => {
                if (!isPractice) return HookResult.Continue;
                CCSPlayerController? player = @event.Userid;
                if (IsPlayerValid(player)) {
                    // 練習模式下的重生邏輯
                }
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerTeam>((@event, info) =>
            {
                if (!isMatchSetup) return HookResult.Continue;
                CCSPlayerController? player = @event.Userid;
                if (player == null || !player.IsValid || player.IsBot) return HookResult.Continue;

                // 呼叫我們在 MatchManagement.cs 修改過的 GetPlayerTeam
                CsTeam playerTeam = GetPlayerTeam(player);
                
                if (player.Team != playerTeam && playerTeam != CsTeam.None) {
                    player.SwitchTeam(playerTeam);
                }
                return HookResult.Continue;
            });

            // 修改點：允許路人使用 jointeam 指令自由選隊
            AddCommandListener("jointeam", (player, info) =>
            {
                return HookResult.Continue; 
            });

            RegisterEventHandler<EventRoundStart>((@event, info) => {
                HandleRoundStart();
                return HookResult.Continue;
            });

            RegisterEventHandler<EventRoundEnd>((@event, info) => {
                HandleRoundEnd(@event);
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerJump>((@event, info) => {
                if (!isPractice) return HookResult.Continue;
                // 練習模式跳躍邏輯
                return HookResult.Continue;
            });

            RegisterEventHandler<EventPlayerBlind>((@event, info) =>
            {
                CCSPlayerController? player = @event.Userid;
                CCSPlayerController? attacker = @event.Attacker;
                if (!isPractice) return HookResult.Continue;

                if (!IsPlayerValid(player) || !IsPlayerValid(attacker)) return HookResult.Continue;

                if (attacker!.IsValid)
                {
                    double roundedBlindDuration = Math.Round(@event.BlindDuration, 2);
                    PrintToPlayerChat(attacker, Localizer["matchzy.pracc.blind", player!.PlayerName, roundedBlindDuration]);
                }
                var userId = player!.UserId;
                if (userId != null && noFlashList.Contains((int)userId))
                {
                    Server.NextFrame(() => KillFlashEffect(player));
                }

                return HookResult.Continue;
            });

            RegisterEventHandler<EventSmokegrenadeDetonate>(EventSmokegrenadeDetonateHandler);
            RegisterEventHandler<EventFlashbangDetonate>(EventFlashbangDetonateHandler);
            RegisterEventHandler<EventHegrenadeDetonate>(EventHegrenadeDetonateHandler);
            RegisterEventHandler<EventMolotovDetonate>(EventMolotovDetonateHandler);
            RegisterEventHandler<EventDecoyStarted>(EventDecoyDetonateHandler);

            Console.WriteLine($"[{ModuleName} {ModuleVersion} LOADED] MatchZy by WD- (Modified for Public Access)");
        }
    }
}
