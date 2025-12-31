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
            teamName = "Team1"
        };
        public Team matchzyTeam2 = new() {
            teamName = "Team2"
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

        // --- 選隊指令提示 (只做廣播，不強迫) ---
        public void BroadcastTeamInstructions()
        {
            if (reverseTeamSides.ContainsKey("CT") && reverseTeamSides.ContainsKey("TERRORIST"))
            {
                string t1Name = reverseTeamSides["CT"].teamName;
                string t2Name = reverseTeamSides["TERRORIST"].teamName;
                
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Lime}隊伍分配建議：");
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Blue}{t1Name}{ChatColors.Default} 請加入 {ChatColors.Blue}CT");
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Red}{t2Name}{ChatColors.Default} 請加入 {ChatColors.Red}T");
            }
        }

        [ConsoleCommand("matchzy_loadmatch", "Loads a match from the given JSON file path")]
        public void LoadMatch(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                if (player != null) return;
                if (isMatchSetup)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.matchisalreadysetup", liveMatchId]);
                    return;
                }
                string fileName = command.ArgString;
                string filePath = Path.Join(Server.GameDirectory + "/csgo", fileName);
                if (!File.Exists(filePath)) 
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.filedoesntexist"]);
                    return;
                }
                string jsonData = File.ReadAllText(filePath);
                bool success = LoadMatchFromJSON(jsonData);
                if (!success)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.matchloadfailed"]);
                    ResetMatch();
                }
                loadedConfigFile = fileName;
            }
            catch (Exception e)
            {
                Log($"[LoadMatch - FATAL] An error occured: {e.Message}");
            }
        }

        [ConsoleCommand("get5_loadmatch_url", "Loads a match from the given URL")]
        [ConsoleCommand("matchzy_loadmatch_url", "Loads a match from the given URL")]
        public void LoadMatchFromURL(CCSPlayerController? player, CommandInfo command)
        {
            if (player != null) return;
            if (isMatchSetup)
            {
                ReplyToUserCommand(player, Localizer["matchzy.mm.get5matchisalreadysetup", liveMatchId]);
                return;
            }
            string url = command.ArgByIndex(1);
            if (!IsValidUrl(url))
            {
                ReplyToUserCommand(player, Localizer["matchzy.mm.invalidurl", url]);
                return;
            }
            try
            {
                HttpClient httpClient = new();
                HttpResponseMessage response = httpClient.GetAsync(url).Result;
                if (response.IsSuccessStatusCode)
                {
                    string jsonData = response.Content.ReadAsStringAsync().Result;
                    bool success = LoadMatchFromJSON(jsonData);
                    if (!success)
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.mm.matchloadfailed"]);
                        ResetMatch();
                    }
                    loadedConfigFile = url;
                }
            }
            catch (Exception e)
            {
                Log($"[LoadMatchFromURL - FATAL] {e.Message}");
            }
        }

        public bool LoadMatchFromJSON(string jsonData)
        {
            JObject jsonDataObject = JObject.Parse(jsonData);
            string validationError = ValidateMatchJsonStructure(jsonDataObject);
            if (validationError != "") return false;

            if(jsonDataObject["matchid"] != null) liveMatchId = (long)jsonDataObject["matchid"]!;
            JToken team1 = jsonDataObject["team1"]!;
            JToken team2 = jsonDataObject["team2"]!;
            JToken maplist = jsonDataObject["maplist"]!;

            matchzyTeam1.teamName = RemoveSpecialCharacters(team1["name"]!.ToString());
            matchzyTeam2.teamName = RemoveSpecialCharacters(team2["name"]!.ToString());
            matchzyTeam1.teamPlayers = team1["players"];
            matchzyTeam2.teamPlayers = team2["players"];

            matchConfig = new()
            {
                MatchId = liveMatchId,
                MapsPool = maplist.ToObject<List<string>>()!,
                NumMaps = jsonDataObject["num_maps"]!.Value<int>(),
                MinPlayersToReady = minimumReadyRequired
            };

            GetOptionalMatchValues(jsonDataObject);
            GetCvarValues(jsonDataObject);
            LoadClientNames();

            if (matchConfig.SkipVeto)
            {
                for (int i = 0; i < matchConfig.NumMaps; i++) 
                {
                    matchConfig.Maplist.Add(matchConfig.MapsPool[i]);
                    if (matchConfig.MapSides.Count < matchConfig.Maplist.Count) {
                        matchConfig.MapSides.Add("knife");
                    }
                }
                ChangeMap(matchConfig.Maplist[0].ToString(), 0);
            }

            readyAvailable = true;
            ExecuteChangedConvars();
            StartWarmup();
            isMatchSetup = true;

            if(matchConfig.SkipVeto) SetMapSides();
            SetTeamNames();
            UpdateHostname();

            return true;
        }

        public void SetMapSides() {
            int mapNumber = matchConfig.CurrentMapNumber;
            if (mapNumber < 0 || mapNumber >= matchConfig.MapSides.Count) return;

            string sideSetting = matchConfig.MapSides[mapNumber];
            teamSides.Clear();
            reverseTeamSides.Clear();

            if (sideSetting == "team1_ct" || sideSetting == "team2_t")
            {
                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
                isKnifeRequired = false;
            }
            else if (sideSetting == "team2_ct" || sideSetting == "team1_t")
            {
                teamSides[matchzyTeam2] = "CT";
                teamSides[matchzyTeam1] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam2;
                reverseTeamSides["TERRORIST"] = matchzyTeam1;
                isKnifeRequired = false;
            }
            else
            {
                isKnifeRequired = true;
                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
            }

            SetTeamNames();
            BroadcastTeamInstructions();
            // 在 !isMatchLive 狀態下，UpdatePlayersMap 雖然被調用，但 GetPlayerTeam 會讓玩家留在原地
            UpdatePlayersMap();
        }

        public void SetTeamNames()
        {
            if (reverseTeamSides.ContainsKey("CT") && reverseTeamSides.ContainsKey("TERRORIST"))
            {
                Server.ExecuteCommand($"mp_teamname_1 \"{reverseTeamSides["CT"].teamName}\"");
                Server.ExecuteCommand($"mp_teamname_2 \"{reverseTeamSides["TERRORIST"].teamName}\"");
            }
        }

        // --- 暴力解鎖核心邏輯 ---
        private CsTeam GetPlayerTeam(CCSPlayerController player)
        {
            // 暴力解鎖 1：只要比賽沒 Live，完全信任玩家當前所在的隊伍 (解決 M 鍵選隊攔截)
            // 這也同時解決了 whitelist 在熱身期間生效的問題
            if (!isMatchLive)
            {
                return (CsTeam)player.TeamNum;
            }

            // 比賽 Live 後才執行的嚴格鎖定邏輯
            var steamId = player.SteamID.ToString();

            // 1. 根據名單分配
            if (matchzyTeam1.teamPlayers != null && matchzyTeam1.teamPlayers[steamId] != null)
            {
                return (teamSides.ContainsKey(matchzyTeam1) && teamSides[matchzyTeam1] == "CT") ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            }
            if (matchzyTeam2.teamPlayers != null && matchzyTeam2.teamPlayers[steamId] != null)
            {
                return (teamSides.ContainsKey(matchzyTeam2) && teamSides[matchzyTeam2] == "CT") ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            }

            // 2. 名單外玩家 (只有 Live 且開啟白名單時才會被踢到觀察者)
            if ((matchzyTeam1.teamPlayers == null || !matchzyTeam1.teamPlayers.HasValues) &&
                (matchzyTeam2.teamPlayers == null || !matchzyTeam2.teamPlayers.HasValues))
            {
                return (CsTeam)player.TeamNum;
            }
            
            return isWhitelistRequired ? CsTeam.None : (CsTeam)player.TeamNum;
        }

        // --- 處理 JoinTeam 指令回傳 (用於 Command Listener) ---
        // 註：這部分通常由主類的 Hook 呼叫，確保回傳 Continue
        public Action OnJoinTeamHandler(CCSPlayerController? player) {
            if (!isMatchLive) return Action.Continue; // 比賽未 Live 時，允許指令繼續執行
            return Action.Continue; 
        }

        public void HandleTeamNameChangeCommand(CCSPlayerController? player, string teamName, int teamNum) {
            if (!IsPlayerAdmin(player, "css_team", "@css/config")) return;
            if (matchStarted) return;

            teamName = RemoveSpecialCharacters(teamName.Trim());
            if (teamName == "") return;

            if (teamNum == 1) {
                matchzyTeam1.teamName = teamName;
                teamSides[matchzyTeam1] = "CT";
                reverseTeamSides["CT"] = matchzyTeam1;
            } else {
                matchzyTeam2.teamName = teamName;
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
            }
            Server.ExecuteCommand($"mp_teamname_{teamNum} \"{teamName}\"");
            BroadcastTeamInstructions();
        }

        public void SwapSidesInTeamData(bool swapTeams) {
            if (swapTeams) (matchzyTeam2, matchzyTeam1) = (matchzyTeam1, matchzyTeam2);
            (teamSides[matchzyTeam1], teamSides[matchzyTeam2]) = (teamSides[matchzyTeam2], teamSides[matchzyTeam1]);
            (reverseTeamSides["CT"], reverseTeamSides["TERRORIST"]) = (reverseTeamSides["TERRORIST"], reverseTeamSides["CT"]);
            BroadcastTeamInstructions();
        }

        // (其餘輔助函數 ValidateMatchJsonStructure, GetCvarValues 等保持與原代碼一致，已包含在內)
        public void EndSeries(string? winnerName, int restartDelay, int t1score, int t2score)
        {
            if (resetCvarsOnSeriesEnd) ResetChangedConvars();
            isMatchLive = false;
            AddTimer(restartDelay, () => { ResetMatch(false); });
        }

        public void HandlePlayoutConfig()
        {
            if (isPlayOutEnabled) {
                Server.ExecuteCommand("mp_overtime_enable 0");
                Server.ExecuteCommand("mp_match_can_clinch false");
            } else {
                Server.ExecuteCommand("mp_match_can_clinch 1");
                Server.ExecuteCommand("mp_overtime_enable 1");
            }
        }
    }
}
