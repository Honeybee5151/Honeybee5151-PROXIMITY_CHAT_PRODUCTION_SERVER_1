//8812938
//777592
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace WorldServer.core
{
    /// <summary>
    /// Publishes server stats to Redis every 5 seconds for the admin dashboard.
    /// Minimal overhead: just a few Redis hash writes per tick.
    /// </summary>
    public class AdminStatsPublisher
    {
        private readonly GameServer _gameServer;
        private readonly IDatabase _db;
        private readonly DateTime _startedAt;
        private volatile bool _running;

        public AdminStatsPublisher(GameServer gameServer)
        {
            _gameServer = gameServer;
            _db = gameServer.Database.Conn;
            _startedAt = DateTime.UtcNow;
        }

        public void Start()
        {
            _running = true;
            _ = Task.Run(PublishLoop);
        }

        public void Stop()
        {
            _running = false;
        }

        private async Task PublishLoop()
        {
            while (_running)
            {
                try
                {
                    await Task.Delay(5000);
                    PublishServerInfo();
                    PublishVoiceStats();
                    PublishOnlinePlayers();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AdminStats] Error: {ex.Message}");
                }
            }
        }

        private void PublishServerInfo()
        {
            try
            {
                var config = _gameServer.Configuration;
                var uptime = (long)(DateTime.UtcNow - _startedAt).TotalSeconds;
                var playerCount = _gameServer.ConnectionManager?.Clients?.Count ?? 0;

                _db.HashSet("admin:server_info", new HashEntry[]
                {
                    new("uptime", uptime.ToString()),
                    new("version", config.serverSettings.version ?? "unknown"),
                    new("players", playerCount.ToString()),
                    new("maxPlayers", config.serverSettings.maxPlayers.ToString()),
                    new("name", config.serverInfo.name ?? "unknown"),
                    new("voiceTestMode", networking.VoiceTestMode.ENABLED.ToString().ToLower()),
                    new("lastUpdated", DateTime.UtcNow.ToString("O"))
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminStats] ServerInfo error: {ex.Message}");
            }
        }

        private void PublishVoiceStats()
        {
            try
            {
                var udpVoice = _gameServer.ConnectionListener?.UdpVoiceHandler;
                var voiceHandler = _gameServer.ConnectionListener?.VoiceHandler;

                if (udpVoice == null)
                {
                    _db.HashSet("admin:voice_stats", new HashEntry[]
                    {
                        new("status", "not_started"),
                        new("lastUpdated", DateTime.UtcNow.ToString("O"))
                    });
                    return;
                }

                var grid = voiceHandler?.GetSpatialGrid();

                _db.HashSet("admin:voice_stats", new HashEntry[]
                {
                    new("authenticatedPlayers", udpVoice.GetConnectedPlayerCount().ToString()),
                    new("activeSpeakers", udpVoice.GetActiveSpeakerCount().ToString()),
                    new("packetsPerSecond", udpVoice.GetPacketsPerSecond().ToString()),
                    new("rateLimitHits", udpVoice.GetRateLimitHits().ToString()),
                    new("speakerCapHits", udpVoice.GetSpeakerCapHits().ToString()),
                    new("occupiedCells", (grid?.GetOccupiedCellCount() ?? 0).ToString()),
                    new("trackedPlayers", (grid?.GetTrackedPlayerCount() ?? 0).ToString()),
                    new("lastUpdated", DateTime.UtcNow.ToString("O"))
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminStats] VoiceStats error: {ex.Message}");
            }
        }

        private void PublishOnlinePlayers()
        {
            try
            {
                var clients = _gameServer.ConnectionManager?.Clients;
                if (clients == null) return;

                var udpVoice = _gameServer.ConnectionListener?.UdpVoiceHandler;
                var voicePlayerIds = udpVoice?.GetConnectedPlayerIds() ?? Array.Empty<string>();
                var voiceSet = new HashSet<string>(voicePlayerIds);

                // Clear old data and write fresh
                _db.KeyDelete("admin:online_players");

                var entries = new List<HashEntry>();
                foreach (var clientPair in clients)
                {
                    var client = clientPair.Key;
                    if (client?.Player == null) continue;

                    var accountId = client.Player.AccountId.ToString();
                    var playerData = new
                    {
                        accountId = client.Player.AccountId,
                        name = client.Player.Name ?? "Unknown",
                        worldId = client.Player.World?.Id ?? -1,
                        worldName = client.Player.World?.IdName ?? "Unknown",
                        voiceConnected = voiceSet.Contains(accountId),
                        isSpeaking = false // Would need active speaker tracking
                    };

                    entries.Add(new HashEntry(accountId, JsonConvert.SerializeObject(playerData)));
                }

                if (entries.Count > 0)
                    _db.HashSet("admin:online_players", entries.ToArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AdminStats] OnlinePlayers error: {ex.Message}");
            }
        }
    }
}
