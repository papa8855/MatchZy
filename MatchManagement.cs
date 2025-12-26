using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Newtonsoft.Json.Linq;


namespace MatchZy
{

    public partial class MatchZy
    {
        public MatchConfig matchConfig = new();

        public bool isMatchSetup = false;

        public bool matchModeOnly = false;

        public bool resetCvarsOnSeriesEnd = true;

        public string loadedConfigFile = "";

        public Team matchzyTeam1 = new() {
            teamName = "COUNTER-TERRORISTS"
        };
        public Team matchzyTeam2 = new() {
            teamName = "TERRORISTS"
        };

        public Dictionary<Team, string> teamSides = new();
        public Dictionary<string, Team> reverseTeamSides = new();

        [ConsoleCommand("css_team1", "Sets team name for team1")]
        public void OnTeam1Command(CCSPlayerController? player, CommandInfo command) {
            HandleTeamNameChangeCommand(player, command.ArgString, 1);
        }

        [ConsoleCommand("css_team2", "Sets team name for team2")]
        public void OnTeam2Command(CCSPlayerController? player, CommandInfo command) {
            HandleTeamNameChangeCommand(player, command.ArgString, 2);
        }

        [ConsoleCommand("matchzy_loadmatch", "Loads a match from the given JSON file path (relative to the csgo/ folder)")]
        public void OnLoadMatchDataCommand(CCSPlayerController? player, CommandInfo command) {
            if (!IsPlayerAdmin(player, "css_loadmatch", "@css/config")) {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (command.ArgCount < 2) {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", ".loadmatch <filename>"]);
                return;
            }
            string fileName = command.ArgByIndex(1);
            string filePath = Path.Join(Server.GameDirectory + "/csgo", fileName);
            if (!File.Exists(filePath)) {
                ReplyToUserCommand(player, Localizer["matchzy.cc.filenotfound", fileName]);
                return;
            }
            string matchData = File.ReadAllText(filePath);
            if (ValidateMatchJsonStructure(matchData)) {
                LoadMatchData(matchData);
                ReplyToUserCommand(player, Localizer["matchzy.cc.matchloaded"]);
            } else {
                ReplyToUserCommand(player, Localizer["matchzy.cc.matchloadfail"]);
            }
        }

        public bool ValidateMatchJsonStructure(string json) {
            try {
                using (JsonDocument doc = JsonDocument.Parse(json)) {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("matchid", out JsonElement matchIdElement)) {
                        if (matchIdElement.ValueKind != JsonValueKind.Number && matchIdElement.ValueKind != JsonValueKind.String) {
                            Log("[LoadMatchDataCommand] matchid should be a number or string!");
                            return false;
                        }
                    }
                    return true;
                }
            } catch (Exception ex) {
                Log($"[ValidateMatchJsonStructure] Error: {ex.Message}");
                return false;
            }
        }

        public void LoadMatchData(string json) {
            try {
                ResetMatch(false);
                matchConfig = JsonSerializer.Deserialize<MatchConfig>(json) ?? new();
                isMatchSetup = true;
                isWhitelistRequired = false; // 強制關閉白名單

                if (matchConfig.ChangedCvars.Count > 0) {
                    foreach (var cvar in matchConfig.ChangedCvars) {
                        Server.ExecuteCommand($"{cvar.Key} {cvar.Value}");
                    }
                }

                JObject matchData = JObject.Parse(json);
                if (matchData["team1"] != null) {
                    matchzyTeam1.teamName = matchData["team1"]!["name"]?.ToString() ?? "Team1";
                    matchzyTeam1.teamTag = matchData["team1"]!["tag"]?.ToString() ?? "";
                    matchzyTeam1.teamPlayers = matchData["team1"]!["players"] ?? new JObject();
                }
                if (matchData["team2"] != null) {
                    matchzyTeam2.teamName = matchData["team2"]!["name"]?.ToString() ?? "Team2";
                    matchzyTeam2.teamTag = matchData["team2"]!["tag"]?.ToString() ?? "";
                    matchzyTeam2.teamPlayers = matchData["team2"]!["players"] ?? new JObject();
                }

                if (matchConfig.Maplist.Count > 0) {
                    Server.ExecuteCommand($"changelevel {matchConfig.Maplist[0]}");
                }
            } catch (Exception ex) {
                Log($"[LoadMatchData] Error: {ex.Message}");
            }
        }

        // --- 核心修改： GetPlayerTeam ---
        private CsTeam GetPlayerTeam(CCSPlayerController player)
        {
            // 修改點：預設回傳玩家目前的隊伍，避免路人因不在名單被判定為 None 進而被踢
            CsTeam playerTeam = player.Team;
            var steamId = player.SteamID;
            try
            {
                if (matchzyTeam1.teamPlayers != null && matchzyTeam1.teamPlayers[steamId.ToString()] != null)
                {
                    playerTeam = (teamSides[matchzyTeam1] == "CT") ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
                }
                else if (matchzyTeam2.teamPlayers != null && matchzyTeam2.teamPlayers[steamId.ToString()] != null)
                {
                    playerTeam = (teamSides[matchzyTeam2] == "CT") ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
                }
                else if (matchConfig.Spectators != null && matchConfig.Spectators[steamId.ToString()] != null)
                {
                    playerTeam = CsTeam.Spectator;
                }
            }
            catch (Exception)
            {
                // 忽略錯誤，回傳玩家當前隊伍
            }
            return playerTeam;
        }

        public void ResetMatch(bool fully) {
            isMatchSetup = false;
            matchStarted = false;
            isMatchLive = false;
            isWhitelistRequired = false;
            // ... 其餘重置邏輯
        }
    }
}
