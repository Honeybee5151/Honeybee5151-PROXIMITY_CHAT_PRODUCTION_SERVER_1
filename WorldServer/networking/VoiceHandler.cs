//777592
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Shared.database.account;
using WorldServer.core;
using WorldServer.networking.packets.outgoing;

namespace WorldServer.networking
{
    // UDP Voice Packet Types
    public class UdpVoiceData
    {
        public string PlayerId { get; set; }
        public byte[] OpusAudioData { get; set; }
        public float Volume { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class UdpAuthRequest
    {
        public string PlayerId { get; set; }
        public string VoiceId { get; set; }
        public string Command { get; set; } = "AUTH";
    }

    public class UdpPriorityCommand
    {
        public string PlayerId { get; set; }
        public string SettingType { get; set; }
        public string Value { get; set; }
        public string Command { get; set; } = "PRIORITY";
    }

    public class VoiceHandler
    {
        private readonly GameServer gameServer;
        private const float PROXIMITY_RANGE = 15.0f;
        private readonly ConcurrentDictionary<int, VoicePrioritySettings> worldPrioritySettings = new();

        // Account data cache — avoids DB lookups on every voice packet
        private readonly ConcurrentDictionary<int, CachedAccount> accountCache = new();
        private const int ACCOUNT_CACHE_TTL_SECONDS = 30;

        // Nearby players cache — recalculated every 200ms instead of per packet
        private readonly ConcurrentDictionary<string, CachedNearbyPlayers> nearbyPlayersCache = new();
        private const int NEARBY_CACHE_TTL_MS = 200;

        public VoiceHandler(GameServer server)
        {
            gameServer = server;
        }

        private DbAccount GetCachedAccount(int accountId)
        {
            if (accountCache.TryGetValue(accountId, out var cached) &&
                (DateTime.UtcNow - cached.FetchedAt).TotalSeconds < ACCOUNT_CACHE_TTL_SECONDS)
            {
                return cached.Account;
            }

            var account = gameServer.Database.GetAccount(accountId);
            accountCache[accountId] = new CachedAccount { Account = account, FetchedAt = DateTime.UtcNow };
            return account;
        }

        public void InvalidateAccountCache(int accountId)
        {
            accountCache.TryRemove(accountId, out _);
        }

        public (PlayerPosition Position, VoicePlayerInfo[] NearbyPlayers) GetCachedNearbyPlayers(string playerId)
        {
            if (nearbyPlayersCache.TryGetValue(playerId, out var cached) &&
                (DateTime.UtcNow - cached.FetchedAt).TotalMilliseconds < NEARBY_CACHE_TTL_MS)
            {
                return (cached.SpeakerPosition, cached.Players);
            }

            var pos = GetPlayerPosition(playerId);
            if (pos == null)
            {
                nearbyPlayersCache[playerId] = new CachedNearbyPlayers
                {
                    SpeakerPosition = null, Players = Array.Empty<VoicePlayerInfo>(), FetchedAt = DateTime.UtcNow
                };
                return (null, Array.Empty<VoicePlayerInfo>());
            }

            var players = GetPlayersInRange(pos.X, pos.Y, PROXIMITY_RANGE, pos.WorldId);
            nearbyPlayersCache[playerId] = new CachedNearbyPlayers
            {
                SpeakerPosition = pos, Players = players, FetchedAt = DateTime.UtcNow
            };
            return (pos, players);
        }

        public void RemoveNearbyCache(string playerId)
        {
            nearbyPlayersCache.TryRemove(playerId, out _);
        }
        
        public bool ArePlayersVoiceIgnored(string speakerId, string listenerId)
        {
            try
            {
                var speakerAccount = GetCachedAccount(int.Parse(speakerId));
                var listenerAccount = GetCachedAccount(int.Parse(listenerId));

                if (speakerAccount == null || listenerAccount == null)
                    return false;

                bool listenerIgnoresSpeaker = listenerAccount.IgnoreList.Contains(speakerAccount.AccountId);
                bool speakerIgnoresListener = speakerAccount.IgnoreList.Contains(listenerAccount.AccountId);

                return listenerIgnoresSpeaker || speakerIgnoresListener;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking voice ignore status: {ex.Message}");
                return false;
            }
        }
        
        public PlayerPosition GetPlayerPosition(string playerId)
        {
            try
            {
                var clients = gameServer.ConnectionManager.Clients;
                foreach (var clientPair in clients)
                {
                    var client = clientPair.Key;
                    if (client.Account?.AccountId.ToString() == playerId || 
                        client.Player?.AccountId.ToString() == playerId)
                    {
                        if (client.Player != null && client.Player.World != null)
                        {
                            return new PlayerPosition
                            {
                                X = client.Player.X,
                                Y = client.Player.Y,
                                WorldId = client.Player.World.Id
                            };
                        }
                    }
                }
                // [TEST_BOT_HOOK] Check test bot positions
                var botPos = VoiceTestMode.GetBotPosition(playerId);
                if (botPos != null) return botPos;
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting player position: {ex.Message}");
                return null;
            }
        }

        // [TEST_BOT_HOOK] Returns the first real connected player's position (any account ID)
        public PlayerPosition GetFirstConnectedPlayerPosition()
        {
            try
            {
                var clients = gameServer.ConnectionManager.Clients;
                foreach (var clientPair in clients)
                {
                    var client = clientPair.Key;
                    if (client.Player != null && client.Player.World != null)
                    {
                        int accountId = client.Player.AccountId;
                        if (accountId < VoiceTestMode.MIN_BOT_ID) // Skip bots
                        {
                            Console.WriteLine($"[TEST_BOT] Found real player {accountId} at ({client.Player.X:F1}, {client.Player.Y:F1}) in world {client.Player.World.Id}");
                            return new PlayerPosition
                            {
                                X = client.Player.X,
                                Y = client.Player.Y,
                                WorldId = client.Player.World.Id
                            };
                        }
                    }
                }
                Console.WriteLine("[TEST_BOT] No real players found in any world");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST_BOT] Error finding player: {ex.Message}");
                return null;
            }
        }

        public VoicePlayerInfo[] GetPlayersInRange(float speakerX, float speakerY, float range, int speakerWorldId)
        {
            try
            {
                var nearbyPlayers = new List<VoicePlayerInfo>();
                var clients = gameServer.ConnectionManager.Clients;
                
                foreach (var clientPair in clients)
                {
                    var client = clientPair.Key;
                    if (client.Player != null && client.Player.World != null)
                    {
                        // Only include players in the same world
                        if (client.Player.World.Id != speakerWorldId)
                            continue;

                        float distance = CalculateDistance(speakerX, speakerY, client.Player.X, client.Player.Y);
                        
                        if (distance <= range)
                        {
                            nearbyPlayers.Add(new VoicePlayerInfo
                            {
                                PlayerId = client.Player.AccountId.ToString(),
                                Client = client,
                                Distance = distance
                            });
                        }
                    }
                }

                // [TEST_BOT_HOOK] Include test bots in range
                nearbyPlayers.AddRange(VoiceTestMode.GetBotsInRange(speakerX, speakerY, range, speakerWorldId));
                return nearbyPlayers.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding nearby players: {ex.Message}");
                return new VoicePlayerInfo[0];
            }
        }

        public VoicePrioritySettings GetPrioritySettings(int worldId)
        {
            return worldPrioritySettings.GetOrAdd(worldId, _ => new VoicePrioritySettings());
        }

        public bool ShouldActivatePrioritySystem(int worldId, int nearbyPlayerCount)
        {
            var settings = GetPrioritySettings(worldId);
            if (!settings.EnablePriority) return false;
            
            return nearbyPlayerCount >= settings.ActivationThreshold;
        }

        public bool HasVoicePriority(string playerId, string listenerId, VoicePrioritySettings settings)
        {
            try
            {
                int playerAccountId = int.Parse(playerId);
                int listenerAccountId = int.Parse(listenerId);
        
                if (settings.HasManualPriority(playerAccountId))
                    return true;

                var playerAccount = GetCachedAccount(playerAccountId);
                var listenerAccount = GetCachedAccount(listenerAccountId);

                if (playerAccount == null || listenerAccount == null)
                    return false;

                if (settings.GuildMembersGetPriority && playerAccount.GuildId > 0)
                {
                    if (playerAccount.GuildId == listenerAccount.GuildId)
                        return true;
                }

                if (settings.LockedPlayersGetPriority && listenerAccount.LockList.Contains(playerAccountId))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking voice priority: {ex.Message}");
                return false;
            }
        }
        
        private float CalculateDistance(float x1, float y1, float x2, float y2)
        {
            float dx = x1 - x2;
            float dy = y1 - y2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public class UdpVoiceHandler
    {
        private readonly GameServer gameServer;
        private readonly VoiceHandler voiceUtils;
        private UdpClient udpServer;
        private readonly ConcurrentDictionary<string, IPEndPoint> playerUdpEndpoints = new();
        private readonly ConcurrentDictionary<string, DateTime> lastUdpActivity = new();
        private readonly ConcurrentDictionary<string, bool> authenticatedPlayers = new();
        private const float PROXIMITY_RANGE = 15.0f;
        private const int MAX_SPEAKERS_PER_LISTENER = 10;
        // Tracks active speakers per listener: listenerId -> { speakerId -> (distance, timestamp) }
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (float Distance, DateTime Time)>> listenerSpeakerSlots = new();
        private volatile bool isRunning = false;
        
        public UdpVoiceHandler(GameServer server, VoiceHandler voiceHandler)
        {
            gameServer = server;
            voiceUtils = voiceHandler;
        }

        /// <summary>
        /// Returns true if this speaker is allowed to send to this listener.
        /// Each listener can hear at most MAX_SPEAKERS_PER_LISTENER at once (closest by distance).
        /// </summary>
        private bool TryClaimSpeakerSlot(string listenerId, string speakerId, float distance)
        {
            var slots = listenerSpeakerSlots.GetOrAdd(listenerId, _ => new ConcurrentDictionary<string, (float, DateTime)>());
            var now = DateTime.UtcNow;

            // Clean stale entries (>500ms old = speaker stopped talking)
            foreach (var kvp in slots)
            {
                if ((now - kvp.Value.Time).TotalMilliseconds > 500)
                    slots.TryRemove(kvp.Key, out _);
            }

            // Already has a slot — just update
            if (slots.ContainsKey(speakerId))
            {
                slots[speakerId] = (distance, now);
                return true;
            }

            // Under the cap — take a slot
            if (slots.Count < MAX_SPEAKERS_PER_LISTENER)
            {
                slots[speakerId] = (distance, now);
                return true;
            }

            // At cap — replace farthest speaker if we're closer
            string farthestId = null;
            float farthestDist = 0f;
            foreach (var kvp in slots)
            {
                if (kvp.Value.Distance > farthestDist)
                {
                    farthestDist = kvp.Value.Distance;
                    farthestId = kvp.Key;
                }
            }

            if (farthestId != null && distance < farthestDist)
            {
                slots.TryRemove(farthestId, out _);
                slots[speakerId] = (distance, now);
                return true;
            }

            return false; // Too far — listener is already hearing 10 closer people
        }
        
        public async Task StartUdpVoiceServer(int port = 2051)
        {
            try
            {
                udpServer = new UdpClient(port);
                isRunning = true;
                
                Console.WriteLine($"UDP Voice Server started on port {port} with full feature set");
                Console.WriteLine("Features: Proximity Chat, Priority System, Ignore System, Distance-based Volume, Authentication");
                
                _ = Task.Run(ProcessUdpVoicePackets);
                _ = Task.Run(CleanupInactiveUdpConnections);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start UDP voice server: {ex.Message}");
                throw;
            }
        }
        private async Task ProcessUdpVoicePackets()
        {
            while (isRunning)
            {
                try
                {
                    var result = await udpServer.ReceiveAsync();
                    var packet = result.Buffer;
                    var clientEndpoint = result.RemoteEndPoint;
    
                    // Check packet type by examining first 4 bytes
                    if (packet.Length >= 4)  // ← FIXED: Was "packet.Length 2"
                    {
                        string packetType = Encoding.UTF8.GetString(packet, 0, 4);
        
                        if (packetType == "AUTH")
                        {
                            await ProcessAuthenticationPacket(packet, clientEndpoint);
                            continue;
                        }
                        else if (packetType == "PRIO")
                        {
                            await ProcessPriorityPacket(packet, clientEndpoint);
                            continue;
                        }
                        else if (packetType == "PING")
                        {
                            await ProcessPingPacket(packet, clientEndpoint);
                            continue;
                        }
                        // NO "VOIC" case here - server doesn't receive VOIC packets
                    }
    
                    // Voice data packet - NEW format: [2 bytes playerId][Opus audio]
                    if (packet.Length >= 2)  // ← CHANGED: Was >= 20
                    {
                        await ProcessVoiceDataPacket(packet, clientEndpoint);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UDP Voice packet error: {ex.Message}");
                    await Task.Delay(100);
                }
            }
        }
        private async Task ProcessAuthenticationPacket(byte[] packet, IPEndPoint clientEndpoint)
        {
            try
            {
                // Auth packet format: "AUTH" + JSON data
                string jsonData = Encoding.UTF8.GetString(packet, 4, packet.Length - 4);
                var authRequest = JsonSerializer.Deserialize<UdpAuthRequest>(jsonData);
                
                Console.WriteLine($"UDP: Authentication request from {authRequest.PlayerId}");

                // [TEST_BOT_HOOK] Skip auth for test bots
                if (VoiceTestMode.ShouldSkipAuth(authRequest.PlayerId))
                {
                    Console.WriteLine($"[TEST_BOT] Bypassing auth for bot {authRequest.PlayerId}");
                    string botId = authRequest.PlayerId.Trim();
                    authenticatedPlayers[botId] = true;
                    playerUdpEndpoints[botId] = clientEndpoint;
                    lastUdpActivity[botId] = DateTime.UtcNow;
                    VoiceTestMode.RegisterBot(botId, voiceUtils);
                    await SendAuthResponse(clientEndpoint, "ACCEPTED", "Test bot authenticated");
                    return;
                }

                // Validate VoiceID
                if (!ValidateVoiceID(authRequest.PlayerId, authRequest.VoiceId))
                {
                    Console.WriteLine($"UDP SECURITY: Invalid VoiceID for player {authRequest.PlayerId}");
                    await SendAuthResponse(clientEndpoint, "REJECTED", "Invalid VoiceID");
                    return;
                }

                // Verify player session
                if (!VerifyPlayerSession(authRequest.PlayerId))
                {
                    Console.WriteLine($"UDP SECURITY: Player {authRequest.PlayerId} not in active session");
                    await SendAuthResponse(clientEndpoint, "REJECTED", "Not in game");
                    return;
                }

                // Check for duplicate connections
                if (authenticatedPlayers.ContainsKey(authRequest.PlayerId))
                {
                    Console.WriteLine($"UDP: Replacing existing connection for player {authRequest.PlayerId}");
                }

                // Accept authentication
                string trimmedPlayerId = authRequest.PlayerId.Trim();
                authenticatedPlayers[trimmedPlayerId] = true;
                playerUdpEndpoints[trimmedPlayerId] = clientEndpoint;
                lastUdpActivity[trimmedPlayerId] = DateTime.UtcNow;
                
                await SendAuthResponse(clientEndpoint, "ACCEPTED", "Voice authenticated");
                Console.WriteLine($"UDP: Voice connection established for player {authRequest.PlayerId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP authentication error: {ex.Message}");
                await SendAuthResponse(clientEndpoint, "ERROR", "Server error");
            }
        }
        
        private async Task ProcessPriorityPacket(byte[] packet, IPEndPoint clientEndpoint)
        {
            try
            {
                // Priority packet format: "PRIO" + JSON data
                string jsonData = Encoding.UTF8.GetString(packet, 4, packet.Length - 4);
                var priorityCommand = JsonSerializer.Deserialize<UdpPriorityCommand>(jsonData);
                
                // Find player ID from endpoint
                string clientPlayerId = GetPlayerIdFromEndpoint(clientEndpoint);
                if (clientPlayerId == null)
                {
                    Console.WriteLine("UDP: Cannot process priority setting - client not identified");
                    return;
                }
                
                // Get player's world position
                var playerPosition = voiceUtils.GetPlayerPosition(clientPlayerId);
                // SHOULD BE != NULL BUT NULL FOR TESTING
                if (playerPosition != null)
                {
                    var settings = voiceUtils.GetPrioritySettings(playerPosition.WorldId);
                    switch (priorityCommand.SettingType)
                    {
                        case "ENABLED":
                            if (bool.TryParse(priorityCommand.Value, out bool enabled))
                            {
                                settings.EnablePriority = enabled;
                                Console.WriteLine($"UDP: Priority system {(enabled ? "enabled" : "disabled")} for world {playerPosition.WorldId}");
                            }
                            break;
                            
                        case "THRESHOLD":
                            if (int.TryParse(priorityCommand.Value, out int threshold))
                            {
                                settings.ActivationThreshold = threshold;
                                Console.WriteLine($"UDP: Priority threshold set to {threshold} for world {playerPosition.WorldId}");
                            }
                            break;
                            
                        case "NON_PRIORITY_VOLUME":
                            if (float.TryParse(priorityCommand.Value, out float volume))
                            {
                                settings.NonPriorityVolume = volume;
                                Console.WriteLine($"UDP: Non-priority volume set to {volume} for world {playerPosition.WorldId}");
                            }
                            break;
                            
                        case "ADD_MANUAL":
                            if (int.TryParse(priorityCommand.Value, out int addAccountId))
                            {
                                if (settings.AddManualPriority(addAccountId))
                                {
                                    Console.WriteLine($"UDP: Added manual priority for account {addAccountId} in world {playerPosition.WorldId}");
                                }
                                else
                                {
                                    Console.WriteLine($"UDP: Failed to add manual priority - list full ({settings.GetManualPriorityCount()}/{settings.MaxPriorityPlayers})");
                                }
                            }
                            break;
                            
                        case "REMOVE_MANUAL":
                            if (int.TryParse(priorityCommand.Value, out int removeAccountId))
                            {
                                if (settings.RemoveManualPriority(removeAccountId))
                                {
                                    Console.WriteLine($"UDP: Removed manual priority for account {removeAccountId} in world {playerPosition.WorldId}");
                                }
                                else
                                {
                                    Console.WriteLine($"UDP: Account {removeAccountId} was not in manual priority list");
                                }
                            }
                            break;
                    }
                    
                    settings.ValidateSettings();
                    await SendPriorityResponse(clientEndpoint, "SUCCESS", $"Priority setting {priorityCommand.SettingType} updated");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP priority command error: {ex.Message}");
                await SendPriorityResponse(clientEndpoint, "ERROR", "Server error");
            }
        }
        
        private async Task ProcessPingPacket(byte[] packet, IPEndPoint clientEndpoint)
        {
            try
            {
                // Respond to ping with pong
                byte[] pongResponse = Encoding.UTF8.GetBytes("PONG");
                await udpServer.SendAsync(pongResponse, pongResponse.Length, clientEndpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP ping error: {ex.Message}");
            }
        }
        
        private async Task ProcessVoiceDataPacket(byte[] packet, IPEndPoint clientEndpoint)
{
    try
    {
        // Voice packet format: [2 bytes playerId][Opus audio data]
        if (packet.Length < 2)
            return;
        
        ushort playerIdShort = BitConverter.ToUInt16(packet, 0);
        string playerId = playerIdShort.ToString();
        
        // Security check: Player must be authenticated
        if (!authenticatedPlayers.ContainsKey(playerId))
            return;
        
        // Extract Opus audio data (starts at byte 2)
        byte[] opusAudio = new byte[packet.Length - 2];
        Array.Copy(packet, 2, opusAudio, 0, opusAudio.Length);
        
        // Update activity tracking
        playerUdpEndpoints[playerId] = clientEndpoint;
        lastUdpActivity[playerId] = DateTime.UtcNow;
        
        // Create voice data object
        var voiceData = new UdpVoiceData
        {
            PlayerId = playerId,
            OpusAudioData = opusAudio,
            Volume = 1.0f,
            Timestamp = DateTime.UtcNow
        };
        
        // Broadcast to nearby players
        await BroadcastVoiceToNearbyPlayers(voiceData);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"UDP voice data processing error: {ex.Message}");
    }
}
       private async Task BroadcastVoiceToNearbyPlayers(UdpVoiceData voiceData)
{
    try
    {
        // Use cached nearby players (recalculated every 200ms, not per packet)
        var (speakerPosition, nearbyPlayers) = voiceUtils.GetCachedNearbyPlayers(voiceData.PlayerId);
        if (speakerPosition == null)
            return;
        
        // Get priority settings for this world
        var prioritySettings = voiceUtils.GetPrioritySettings(speakerPosition.WorldId);
        bool prioritySystemActive = voiceUtils.ShouldActivatePrioritySystem(speakerPosition.WorldId, nearbyPlayers.Length);
        
        var sendTasks = new List<Task>();
        
        foreach (var player in nearbyPlayers)
        {
            try
            {
                // Skip self-voice
                if (voiceData.PlayerId == player.PlayerId)
                    continue;

                // Check ignore system
                if (voiceUtils.ArePlayersVoiceIgnored(voiceData.PlayerId, player.PlayerId))
                    continue;

                // Apply priority system EARLY (before expensive volume calculations)
                if (prioritySystemActive)
                {
                    bool hasPriority = voiceUtils.HasVoicePriority(voiceData.PlayerId, player.PlayerId, prioritySettings);
                    
                    if (prioritySettings.ShouldFilterVoice(hasPriority))
                        continue;
                }

                // Calculate final volume
                float finalVolume = voiceData.Volume;

                // Apply priority volume multiplier
                if (prioritySystemActive)
                {
                    bool hasPriority = voiceUtils.HasVoicePriority(voiceData.PlayerId, player.PlayerId, prioritySettings);
                    float volumeMultiplier = prioritySettings.GetVolumeMultiplier(hasPriority);
                    finalVolume *= volumeMultiplier;
                }

                // Per-listener speaker cap: only hear the closest 10 speakers
                if (!TryClaimSpeakerSlot(player.PlayerId, voiceData.PlayerId, player.Distance))
                    continue;

                // Send to player if they have UDP connection
                if (playerUdpEndpoints.TryGetValue(player.PlayerId, out var targetEndpoint))
                {
                    // Packet format: [2 bytes speakerId][4 bytes volume][2 bytes length][Opus audio]
                    ushort speakerIdShort = ushort.Parse(voiceData.PlayerId);
                    byte[] voicePacket = new byte[2 + 4 + 2 + voiceData.OpusAudioData.Length];

                    // Speaker ID (2 bytes) - starts at offset 0
                    byte[] speakerIdBytes = BitConverter.GetBytes(speakerIdShort);
                    Array.Copy(speakerIdBytes, 0, voicePacket, 0, 2);

                    // Volume (4 bytes) - at offset 2
                    byte[] volumeBytes = BitConverter.GetBytes(finalVolume);
                    Array.Copy(volumeBytes, 0, voicePacket, 2, 4);

                    // Opus length (2 bytes) - at offset 6
                    byte[] lengthBytes = BitConverter.GetBytes((ushort)voiceData.OpusAudioData.Length);
                    Array.Copy(lengthBytes, 0, voicePacket, 6, 2);

                    // Opus audio data - starts at byte 8
                    Array.Copy(voiceData.OpusAudioData, 0, voicePacket, 8, voiceData.OpusAudioData.Length);
                    
                    sendTasks.Add(SendUdpPacketSafe(voicePacket, targetEndpoint, player.PlayerId));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP: Error processing voice for {player.PlayerId}: {ex.Message}");
            }
        }
        
        if (sendTasks.Count > 0)
            await Task.WhenAll(sendTasks);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"UDP broadcast error: {ex.Message}");
    }
}
private async Task SendUdpPacketSafe(byte[] data, IPEndPoint endpoint, string playerId)
{
    try
    {
        await udpServer.SendAsync(data, data.Length, endpoint);
        // That's it!
    }
    catch (Exception ex)
    {
        // Just log failures - the game logic handles everything else
        Console.WriteLine($"UDP: Send failed to {playerId}: {ex.Message}");
    }
}
        private async Task SendAuthResponse(IPEndPoint endpoint, string status, string message)
        {
            try
            {
                var response = new { Status = status, Message = message };
                string jsonResponse = JsonSerializer.Serialize(response);
                byte[] responseData = Encoding.UTF8.GetBytes("ARSP" + jsonResponse);
                await udpServer.SendAsync(responseData, responseData.Length, endpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP: Error sending auth response: {ex.Message}");
            }
        }
        
        private async Task SendPriorityResponse(IPEndPoint endpoint, string status, string message)
        {
            try
            {
                var response = new { Status = status, Message = message };
                string jsonResponse = JsonSerializer.Serialize(response);
                byte[] responseData = Encoding.UTF8.GetBytes("PRSP" + jsonResponse);
                await udpServer.SendAsync(responseData, responseData.Length, endpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP: Error sending priority response: {ex.Message}");
            }
        }
        
        private string GetPlayerIdFromEndpoint(IPEndPoint endpoint)
        {
            foreach (var kvp in playerUdpEndpoints)
            {
                if (kvp.Value.Equals(endpoint))
                    return kvp.Key;
            }
            return null;
        }
        
        private bool ValidateVoiceID(string playerId, string voiceId)
        {
            try
            {
                var account = gameServer.Database.GetAccount(int.Parse(playerId));
                return account != null && account.VoiceID == voiceId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP: Error validating VoiceID: {ex.Message}");
                return false;
            }
        }
        
        private bool VerifyPlayerSession(string playerId)
        {
            try
            {
                var clients = gameServer.ConnectionManager.Clients;
                foreach (var clientPair in clients)
                {
                    var client = clientPair.Key;
                    if (client.Player?.AccountId.ToString() == playerId && 
                        client.Socket?.Connected == true && 
                        client.Player.World != null)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"UDP: Error verifying player session: {ex.Message}");
                return false;
            }
        }
        
        private async Task CleanupInactiveUdpConnections()
        {
            while (isRunning)
            {
                try
                {
                    // Clean up players who haven't been active in 24 hours
                    var cutoff = DateTime.UtcNow.AddHours(-24);
                    var oldPlayers = lastUdpActivity
                        .Where(kvp => kvp.Value < cutoff)
                        .Select(kvp => kvp.Key)
                        .ToList();
            
                    foreach (var playerId in oldPlayers)
                    {
                        playerUdpEndpoints.TryRemove(playerId, out _);
                        authenticatedPlayers.TryRemove(playerId, out _);
                        lastUdpActivity.TryRemove(playerId, out _);
                        voiceUtils.RemoveNearbyCache(playerId);
                        listenerSpeakerSlots.TryRemove(playerId, out _);
                        Console.WriteLine($"UDP: Memory cleanup for {playerId} (inactive 24h)");
                    }
            
                    await Task.Delay(3600000); // Check once per hour
                }
                catch { }
            }
        }
        
        public void Stop()
        {
            isRunning = false;
            udpServer?.Close();
            udpServer?.Dispose();
            Console.WriteLine("UDP Voice Server stopped");
        }
        
        // Public status methods
        public int GetConnectedPlayerCount()
        {
            return authenticatedPlayers.Count;
        }
        
        public string[] GetConnectedPlayerIds()
        {
            return authenticatedPlayers.Keys.ToArray();
        }
    }
    
    // Helper classes
    public class PlayerPosition
    {
        public float X { get; set; }
        public float Y { get; set; }
        public int WorldId { get; set; }
    }
    
    public class VoicePlayerInfo
    {
        public string PlayerId { get; set; }
        public Client Client { get; set; }
        public float Distance { get; set; }
    }

    public class CachedAccount
    {
        public DbAccount Account { get; set; }
        public DateTime FetchedAt { get; set; }
    }

    public class CachedNearbyPlayers
    {
        public PlayerPosition SpeakerPosition { get; set; }
        public VoicePlayerInfo[] Players { get; set; }
        public DateTime FetchedAt { get; set; }
    }
}