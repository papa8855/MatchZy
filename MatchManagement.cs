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

        // --- 新增：攔截換隊指令，解決熱身選錯隊伍按 M 沒反應的問題 ---
        [ConsoleCommand("jointeam", "攔截換隊指令以允許熱身期間自由換隊")]
        public void OnJoinTeamCommand(CCSPlayerController? player, CommandInfo command)
        {
            if (player == null || !player.IsValid) return;

            // 如果目前不是比賽進行中 (Warmup)
            if (!isMatchLive)
            {
                if (command.ArgCount >= 2)
                {
                    if (int.TryParse(command.ArgByIndex(1), out int teamSide))
                    {
                        // 使用 SwitchTeam 強制搬移 (CS2 插件通用方法)
                        player.SwitchTeam((CsTeam)teamSide);
                        return;
                    }
                }
                return; 
            }

            // 比賽開始後才進行限制
            if (!IsPlayerAdmin(player, "css_jointeam", "@css/config")) {
                ReplyToUserCommand(player, Localizer["matchzy.mm.teamcannotbechanged"]);
            }
        }

        [ConsoleCommand("matchzy_loadmatch", "Loads a match from the given JSON file path (relative to the csgo/ directory)")]
        public void LoadMatch(CCSPlayerController? player, CommandInfo command)
        {
            try
            {
                if (player != null) return;
                if (isMatchSetup)
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.matchisalreadysetup", liveMatchId]);
                    Log($"[LoadMatch] A match is already setup with id: {liveMatchId}, cannot load a new match!");
                    return;
                }
                string fileName = command.ArgString;
                string filePath = Path.Join(Server.GameDirectory + "/csgo", fileName);
                if (!File.Exists(filePath)) 
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.filedoesntexist"]);
                    Log($"[LoadMatch] Provided file does not exist! Usage: matchzy_loadmatch <filename>");
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
                return;
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
                Log($"[LoadMatchDataCommand] A match is already setup with id: {liveMatchId}, cannot load a new match!");
                return;
            }
            string url = command.ArgByIndex(1);

            string headerName = command.ArgCount > 3 ? command.ArgByIndex(2) : "";
            string headerValue = command.ArgCount > 3 ? command.ArgByIndex(3) : "";

            Log($"[LoadMatchDataCommand] Match setup request received with URL: {url} headerName: {headerName} and headerValue: {headerValue}");

            if (!IsValidUrl(url))
            {
                ReplyToUserCommand(player, Localizer["matchzy.mm.invalidurl", url]);
                Log($"[LoadMatchDataCommand] Invalid URL: {url}. Please provide a valid URL to load the match!");
                return;
            }
            try
            {
                HttpClient httpClient = new();
                if (headerName != "")
                {
                    httpClient.DefaultRequestHeaders.Add(headerName, headerValue);
                }
                HttpResponseMessage response = httpClient.GetAsync(url).Result;

                if (response.IsSuccessStatusCode)
                {
                    string jsonData = response.Content.ReadAsStringAsync().Result;
                    Log($"[LoadMatchFromURL] Received following data: {jsonData}");

                    bool success = LoadMatchFromJSON(jsonData);
                    if (!success)
                    {
                        ReplyToUserCommand(player, Localizer["matchzy.mm.matchloadfailed"]);
                        ResetMatch();
                    }
                    loadedConfigFile = url;
                }
                else
                {
                    ReplyToUserCommand(player, Localizer["matchzy.mm.httprequestfailed", response.StatusCode]);
                    Log($"[LoadMatchFromURL] HTTP request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception e)
            {
                Log($"[LoadMatchFromURL - FATAL] An error occured: {e.Message}");
                return;
            }
        }

        static string ValidateMatchJsonStructure(JObject jsonData)
        {
            string[] requiredFields = { "maplist", "team1", "team2", "num_maps" };

            foreach (string field in requiredFields)
            {
                if (jsonData[field] == null)
                {
                    return $"Missing mandatory field: {field}";
                }
            }

            foreach (var property in jsonData.Properties())
            {
                string field = property.Name;

                switch (field)
                {
                    case "matchid":
                    case "players_per_team":
                    case "min_players_to_ready":
                    case "min_spectators_to_ready":
                    case "num_maps":
                        if (!int.TryParse(jsonData[field]!.ToString(), out int numMaps))
                        {
                            return $"{field} should be an integer!";
                        }
                        if (field == "num_maps" && numMaps > jsonData["maplist"]!.ToObject<List<string>>()!.Count)
                        {
                            return $"{field} should be equal to or greater than maplist!";
                        }
                        break;
                    
                    case "cvars":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }
                        break;

                    case "team1":
                    case "team2":
                    case "spectators":
                        if (jsonData[field]!.Type != JTokenType.Object)
                        {
                            return $"{field} should be a JSON structure!";
                        }
                        if ((field != "spectators") && (jsonData[field]!["players"] == null || jsonData[field]!["players"]!.Type != JTokenType.Object)) 
                        {
                            return $"{field} should have 'players' JSON!";
                        }
                        break;

                    case "veto_mode":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }
                        break;

                    case "maplist":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }
                        if (!jsonData[field]!.Any())
                        {
                            return $"{field} should contain atleast 1 map!";
                        }
                        break;

                    case "map_sides":
                        if (jsonData[field]!.Type != JTokenType.Array)
                        {
                            return $"{field} should be an Array!";
                        }
                        string[] allowedValues = { "team1_ct", "team1_t", "team2_ct", "team2_t", "knife" };
                        bool allElementsValid = jsonData[field]!.All(element => allowedValues.Contains(element.ToString()));

                        if (!allElementsValid) {
                            return $"{field} should be \"team1_ct\", \"team1_t\", \"team2_ct\", \"team2_t\", or \"knife\"!";
                        }
                        
                        if (jsonData[field]!.ToObject<List<string>>()!.Count < jsonData["num_maps"]!.Value<int>()) {
                            return $"{field} should be equal to or greater than num_maps!";
                        }
                        break;

                    case "skip_veto":
                    case "clinch_series":
                    case "wingman":
                        if (!bool.TryParse(jsonData[field]!.ToString(), out bool _))
                        {
                            return $"{field} should be a boolean!";
                        }
                        break;
                }
            }

            return "";
        }

        public bool LoadMatchFromJSON(string jsonData)
        {
            JObject jsonDataObject = JObject.Parse(jsonData);
            string validationError = ValidateMatchJsonStructure(jsonDataObject);

            if (validationError != "")
            {
                Log($"[LoadMatchDataCommand] {validationError}");
                return false;
            }

            if(jsonDataObject["matchid"] != null)
            {
                liveMatchId = (long)jsonDataObject["matchid"]!;
            }
            JToken team1 = jsonDataObject["team1"]!;
            JToken team2 = jsonDataObject["team2"]!;
            JToken maplist = jsonDataObject["maplist"]!;

            if (team1["id"] != null) matchzyTeam1.id = team1["id"]!.ToString();
            if (team2["id"] != null) matchzyTeam2.id = team2["id"]!.ToString();

            matchzyTeam1.teamName = RemoveSpecialCharacters(team1["name"]!.ToString());
            matchzyTeam2.teamName = RemoveSpecialCharacters(team2["name"]!.ToString());
            matchzyTeam1.teamPlayers = team1["players"];
            matchzyTeam2.teamPlayers = team2["players"];

            matchConfig = new()
            {
                MatchId = liveMatchId,
                MapsPool = maplist.ToObject<List<string>>()!,
                MapsLeftInVetoPool = maplist.ToObject<List<string>>()!,
                NumMaps = jsonDataObject["num_maps"]!.Value<int>(),
                MinPlayersToReady = minimumReadyRequired
            };

            GetOptionalMatchValues(jsonDataObject);

            if (matchConfig.MapsPool.Count == matchConfig.NumMaps)
            {
                matchConfig.SkipVeto = true;
                isPreVeto = false;
            }
            else if (matchConfig.MapsPool.Count < matchConfig.NumMaps)
            {
                Log($"[LOADMATCH] The map pool {matchConfig.MapsPool.Count} is not large enough to play a series of {matchConfig.NumMaps} maps.");
                return false;
            }

            if (!matchConfig.SkipVeto)
            {
                if (matchConfig.MapBanOrder.Count != 0)
                {
                    if (!ValidateMapBanLogic()) return false;
                }
                else
                {
                    GenerateDefaultVetoSetup();
                }
            }

            GetCvarValues(jsonDataObject);

            LoadClientNames();

            if (matchConfig.SkipVeto)
            {
                for (int i = 0; i < matchConfig.NumMaps; i++) 
                {
                    matchConfig.Maplist.Add(matchConfig.MapsPool[i]);

                    if (matchConfig.MapSides.Count < matchConfig.Maplist.Count) {
                        if (matchConfig.MatchSideType == "standard" || matchConfig.MatchSideType == "always_knife") {
                            matchConfig.MapSides.Add("knife");
                        } else if (matchConfig.MatchSideType == "random") {
                            matchConfig.MapSides.Add(new Random().Next(0, 2) == 0 ? "team1_ct" : "team1_t");
                        } else {
                            matchConfig.MapSides.Add("team1_ct");
                        }
                    }
                }
                string currentMapName = Server.MapName;
                string mapName = matchConfig.Maplist[0].ToString();

                if (IsMapReloadRequiredForGameMode(matchConfig.Wingman) || mapReloadRequired || currentMapName != mapName) 
                {
                    SetCorrectGameMode();
                    ChangeMap(mapName, 0);
                }
            }
            else
            {
                isPreVeto = true;
            } 

            readyAvailable = true;
            ExecuteChangedConvars();
            StartWarmup();
            isMatchSetup = true;

            if(matchConfig.SkipVeto) SetMapSides();

            SetTeamNames();
            UpdatePlayersMap();
            UpdateHostname();

            var seriesStartedEvent = new MatchZySeriesStartedEvent
            {
                MatchId = liveMatchId,
                NumberOfMaps = matchConfig.NumMaps,
                Team1 = new(matchzyTeam1.id, matchzyTeam1.teamName),
                Team2 = new(matchzyTeam2.id, matchzyTeam2.teamName),
            };

            Task.Run(async () => {
                await SendEventAsync(seriesStartedEvent);
            });

            Log($"[LoadMatchFromJSON] Success with matchid: {liveMatchId}!");
            return true;
        }

        public void SetMapSides() {
            int mapNumber = matchConfig.CurrentMapNumber;
            
            if (mapNumber < 0 || mapNumber >= matchConfig.MapSides.Count) return;

            string sideSetting = matchConfig.MapSides[mapNumber];
            Log($"[SetMapSides] Map index: {mapNumber}, Setting: {sideSetting}");

            teamSides.Clear();
            reverseTeamSides.Clear();

            // 修正：Astralis (Team1) 永遠是 matchzyTeam1, NaVi (Team2) 永遠是 matchzyTeam2
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
            else // knife
            {
                isKnifeRequired = true;
                teamSides[matchzyTeam1] = "CT";
                teamSides[matchzyTeam2] = "TERRORIST";
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
            }

            SetTeamNames();
            UpdatePlayersMap();
        }

        // --- 核心修正：解決記分板與 EResult 3 報錯問題 ---
        public void SetTeamNames()
        {
            if (reverseTeamSides.ContainsKey("CT") && reverseTeamSides.ContainsKey("TERRORIST"))
            {
                // mp_teamname_1 固定對應 CT, mp_teamname_2 固定對應 T
                Server.ExecuteCommand($"mp_teamname_1 \"{reverseTeamSides["CT"].teamName}\"");
                Server.ExecuteCommand($"mp_teamname_2 \"{reverseTeamSides["TERRORIST"].teamName}\"");
                
                Log($"[SetTeamNames] CT: {reverseTeamSides["CT"].teamName}, T: {reverseTeamSides["TERRORIST"].teamName}");
            }
        }

        public void GetCvarValues(JObject jsonDataObject)
        {
            try
            {
                if (jsonDataObject["cvars"] == null) return;

                foreach (JProperty cvarData in jsonDataObject["cvars"]!)
                {
                    string cvarName = cvarData.Name;
                    string cvarValue = cvarData.Value.ToString();

                    var cvar = ConVar.Find(cvarName);
                    matchConfig.ChangedCvars[cvarName] = cvarValue;
                    if (cvar != null)
                    {
                        matchConfig.OriginalCvars[cvarName] = GetConvarStringValue(cvar);
                    }
                }

            }
            catch (Exception e)
            {
                Log($"[GetCvarValues FATAL] An error occurred: {e.Message}");
            }
        }

        public void GetOptionalMatchValues(JObject jsonDataObject)
        {
            if(jsonDataObject["map_sides"] != null)
            {
                matchConfig.MapSides = jsonDataObject["map_sides"]!.ToObject<List<string>>()!;
            }
            if(jsonDataObject["players_per_team"] != null)
            {
                matchConfig.PlayersPerTeam = jsonDataObject["players_per_team"]!.Value<int>();
            }
            if(jsonDataObject["min_players_to_ready"] != null)
            {
                matchConfig.MinPlayersToReady = jsonDataObject["min_players_to_ready"]!.Value<int>();
            }
            if(jsonDataObject["min_spectators_to_ready"] != null)
            {
                matchConfig.MinSpectatorsToReady = jsonDataObject["min_spectators_to_ready"]!.Value<int>();
            }
            if (jsonDataObject["spectators"] != null && jsonDataObject["spectators"]!["players"] != null)
            {
                matchConfig.Spectators = jsonDataObject["spectators"]!["players"]!;
                if (matchConfig.Spectators is JArray spectatorsArray && spectatorsArray.Count == 0)
                {
                    matchConfig.Spectators = new JObject();
                }
            }
            if (jsonDataObject["clinch_series"] != null)
            {
                matchConfig.SeriesCanClinch = bool.Parse(jsonDataObject["clinch_series"]!.ToString());
            }
            if (jsonDataObject["skip_veto"] != null)
            {
                matchConfig.SkipVeto = bool.Parse(jsonDataObject["skip_veto"]!.ToString());
            }
            if (jsonDataObject["wingman"] != null)
            {
                matchConfig.Wingman = bool.Parse(jsonDataObject["wingman"]!.ToString());
            }
            if (jsonDataObject["veto_mode"] != null)
            {
                matchConfig.MapBanOrder = jsonDataObject["veto_mode"]!.ToObject<List<string>>()!;
            }
        }

        public void HandleTeamNameChangeCommand(CCSPlayerController? player, string teamName, int teamNum) {
            if (!IsPlayerAdmin(player, "css_team", "@css/config")) {
                SendPlayerNotAdminMessage(player);
                return;
            }
            if (matchStarted) {
                ReplyToUserCommand(player, Localizer["matchzy.mm.teamcannotbechanged"]);
                return;
            }
            teamName = RemoveSpecialCharacters(teamName.Trim());
            if (teamName == "") {
                ReplyToUserCommand(player, Localizer["matchzy.cc.usage", $"!team{teamNum} <name>"]);
            }

            if (teamNum == 1) {
                matchzyTeam1.teamName = teamName;
            } else if (teamNum == 2) {
                matchzyTeam2.teamName = teamName;
            }
            SetTeamNames();
        }

        // --- 核心修正 2：徹底解決刀局結束選邊後的數據交換與 UI 閃爍 ---
        public void SwapSidesInTeamData(bool swapTeams) {
            Log($"[SwapSidesInTeamData] Start Side Swap.");

            // 1. 交換 Team 物件內部的陣營標記
            string oldTeam1Side = teamSides[matchzyTeam1];
            teamSides[matchzyTeam1] = teamSides[matchzyTeam2];
            teamSides[matchzyTeam2] = oldTeam1Side;

            // 2. 更新反向字典
            if (teamSides[matchzyTeam1] == "CT") {
                reverseTeamSides["CT"] = matchzyTeam1;
                reverseTeamSides["TERRORIST"] = matchzyTeam2;
            } else {
                reverseTeamSides["CT"] = matchzyTeam2;
                reverseTeamSides["TERRORIST"] = matchzyTeam1;
            }

            // 3. 解決記分板不刷新的暴力手段：先清除，0.2秒後重新設定
            Server.ExecuteCommand("mp_teamname_1 \" \"");
            Server.ExecuteCommand("mp_teamname_2 \" \"");

            AddTimer(0.2f, () => {
                SetTeamNames();
                UpdatePlayersMap();
            });
        }

        private CsTeam GetPlayerTeam(CCSPlayerController player)
        {
            if (!isMatchLive) return (CsTeam)player.TeamNum;

            var steamId = player.SteamID.ToString();

            if (matchzyTeam1.teamPlayers != null && matchzyTeam1.teamPlayers[steamId] != null)
            {
                return teamSides.ContainsKey(matchzyTeam1) && teamSides[matchzyTeam1] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            }
            if (matchzyTeam2.teamPlayers != null && matchzyTeam2.teamPlayers[steamId] != null)
            {
                return teamSides.ContainsKey(matchzyTeam2) && teamSides[matchzyTeam2] == "CT" ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            }

            if ((matchzyTeam1.teamPlayers == null || !matchzyTeam1.teamPlayers.HasValues) &&
                (matchzyTeam2.teamPlayers == null || !matchzyTeam2.teamPlayers.HasValues))
            {
                return (CsTeam)player.TeamNum;
            }
            return isWhitelistRequired ? CsTeam.None : (CsTeam)player.TeamNum;
        }

        public void EndSeries(string? winnerName, int restartDelay, int t1score, int t2score)
        {
            long matchId = liveMatchId;
            (int team1Score, int team2Score) = (matchzyTeam1.seriesScore, matchzyTeam2.seriesScore);
            if (winnerName == null)
            {
                PrintToAllChat($"{ChatColors.Green}{matchzyTeam1.teamName}{ChatColors.Default} and {ChatColors.Green}{matchzyTeam2.teamName}{ChatColors.Default} have tied the match");
            }
            else
            {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{winnerName}{ChatColors.Default} has won the match");
            }

            string winnerTeam = "none";
            
            if (winnerName != null) 
            {
                if (matchzyTeam1.teamName == winnerName) winnerTeam = "team1";
                else if (matchzyTeam2.teamName == winnerName) winnerTeam = "team2";
                else winnerTeam = matchzyTeam1.seriesScore > matchzyTeam2.seriesScore ? "team1" : "team2";
            }
            else
            {
                winnerTeam = matchzyTeam1.seriesScore > matchzyTeam2.seriesScore ? "team1" : "team2";
            }

            var seriesResultEvent = new MatchZySeriesResultEvent()
            {
                MatchId = matchId,
                Winner = new Winner(t1score > t2score && reverseTeamSides["CT"] == matchzyTeam1 ? "3" : "2", winnerTeam),
                Team1SeriesScore = team1Score,
                Team2SeriesScore = team2Score,
                TimeUntilRestore = 10,
            };

            Task.Run(async () => {
                await database.SetMatchEndData(matchId, winnerName ?? "Draw", team1Score, team2Score);
                await Task.Delay(2000);
                await SendEventAsync(seriesResultEvent);
            });

            if (resetCvarsOnSeriesEnd) ResetChangedConvars();
            isMatchLive = false;
            AddTimer(restartDelay, () => {
                ResetMatch(false);
            });
        }

        public void HandlePlayoutConfig()
        {
            if (isPlayOutEnabled) {
                Server.ExecuteCommand("mp_overtime_enable 0");
                Server.ExecuteCommand("mp_match_can_clinch false");
            } else {
                var absoluteCfgPath = Path.Join(Server.GameDirectory + "/csgo/cfg", GetGameMode() == 1 ? liveCfgPath : liveWingmanCfgPath);
                string? matchCanClinch = GetConvarValueFromCFGFile(absoluteCfgPath, "mp_match_can_clinch");
                string? overtimeEnabled = GetConvarValueFromCFGFile(absoluteCfgPath, "mp_overtime_enable");
                Server.ExecuteCommand($"mp_match_can_clinch {matchCanClinch ?? "1"}");
                Server.ExecuteCommand($"mp_overtime_enable {overtimeEnabled ?? "1"}");
            }
        }
    }
}
