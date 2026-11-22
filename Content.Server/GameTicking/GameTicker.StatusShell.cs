using System.Linq;
using System.Text.Json.Nodes;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Robust.Server.ServerStatus;
using Robust.Shared.Configuration;
using Content.Shared._Harmony.Common.JoinQueue; // Harmony Queue
using Content.Shared._Harmony.CCVars; // Harmony Queue

namespace Content.Server.GameTicking
{
    public sealed partial class GameTicker
    {
        /// <summary>
        ///     Used for thread safety, given <see cref="IStatusHost.OnStatusRequest"/> is called from another thread.
        /// </summary>
        private readonly object _statusShellLock = new();

        /// <summary>
        ///     Round start time in UTC, for status shell purposes.
        /// </summary>
        [ViewVariables]
        private DateTime _roundStartDateTime;
        private JsonObject? _lastRoundResult;

        private void SaveRoundStatusResult(RoundEndMessageEvent result, List<(string Character, int Change)> bankResults)
        {
            var balances = new JsonArray();
            foreach (var (character, change) in bankResults.OrderByDescending(row => row.Change))
            {
                balances.Add(new JsonObject
                {
                    ["character"] = character,
                    ["change"] = change,
                });
            }

            var snapshot = new JsonObject
            {
                ["round_id"] = result.RoundId,
                ["started_at"] = _roundStartDateTime.ToString("o"),
                ["ended_at"] = DateTime.UtcNow.ToString("o"),
                ["duration_seconds"] = (long) result.RoundDuration.TotalSeconds,
                ["map"] = _gameMapManager.GetSelectedMap()?.MapName,
                ["preset"] = result.GamemodeTitle,
                ["bank_results"] = balances,
                ["participants"] = result.AllPlayersEndInfo
                    .Where(player => !player.Observer && player.PlayerGuid.HasValue)
                    .Select(player => player.PlayerGuid)
                    .Distinct()
                    .Count(),
            };

            lock (_statusShellLock)
                _lastRoundResult = snapshot;
        }

        /// <summary>
        ///     For access to CVars in status responses.
        /// </summary>
        [Dependency] private IConfigurationManager _cfg = default!;
        /// <summary>
        ///     For access to the round ID in status responses.
        /// </summary>
        [Dependency] private SharedGameTicker _gameTicker = default!;

        [Dependency] private IJoinQueueManager _joinQueue = default!; // Harmony Queue

        private void InitializeStatusShell()
        {
            IoCManager.Resolve<IStatusHost>().OnStatusRequest += GetStatusResponse;
        }

        private void GetStatusResponse(JsonNode jObject)
        {
            var preset = CurrentPreset ?? Preset;

            // This method is raised from another thread, so this better be thread safe!
            lock (_statusShellLock)
            {
                jObject["name"] = _baseServer.ServerName;
                jObject["map"] = _gameMapManager.GetSelectedMap()?.MapName;
                jObject["round_id"] = _gameTicker.RoundId;
                jObject["players"] = _cfg.GetCVar(CCVars.AdminsCountInReportedPlayerCount)
                    ? _playerManager.PlayerCount
                    : _playerManager.PlayerCount - _adminManager.ActiveAdmins.Count()
                    // Only adjust the play count if the Harmony Queue is enabled, this is to minimize the changes to the shell status code
                    - (_cfg.GetCVar(HCCVars.EnableQueue) ? _joinQueue.PlayerInQueueCount : 0); // Harmony Queue
                jObject["soft_max_players"] = _cfg.GetCVar(CCVars.SoftMaxPlayers);
                jObject["panic_bunker"] = _cfg.GetCVar(CCVars.PanicBunkerEnabled);
                jObject["run_level"] = (int) _runLevel;
                if (_lastRoundResult != null)
                    jObject["last_round_result"] = _lastRoundResult.DeepClone();
                if (preset != null)
                    jObject["preset"] = Loc.GetString(preset.ModeTitle);
                if (_runLevel >= GameRunLevel.InRound)
                {
                    jObject["round_start_time"] = _roundStartDateTime.ToString("o");
                }
            }
        }
    }
}
