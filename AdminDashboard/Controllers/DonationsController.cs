using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/donations")]
    public class DonationsController : ControllerBase
    {
        private readonly RedisService _redis;
        private readonly SupabaseService _supabase;

        private static readonly Dictionary<string, int> RankMap = new()
        {
            { "mute", 1 },       // Mute ($1)
            { "whisperer", 2 },  // Whisperer ($10)
            { "chatter", 3 },    // Chatter ($100)
        };

        private static readonly Dictionary<string, string> RankNames = new()
        {
            { "mute", "Mute" },
            { "whisperer", "Whisperer" },
            { "chatter", "Chatter" },
        };

        public DonationsController(RedisService redis, SupabaseService supabase)
        {
            _redis = redis;
            _supabase = supabase;
        }

        [HttpPost("grant-rank")]
        public IActionResult GrantRank([FromBody] GrantRankRequest request)
        {
            try
            {
                // Verify webhook secret
                var expectedSecret = Environment.GetEnvironmentVariable("WEBHOOK_SECRET") ?? "";
                var providedSecret = Request.Headers["X-Webhook-Secret"].ToString();

                if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
                    return Unauthorized(new { error = "Invalid webhook secret" });

                if (string.IsNullOrWhiteSpace(request.GameName))
                    return BadRequest(new { error = "gameName is required" });

                if (string.IsNullOrWhiteSpace(request.RankId) || !RankMap.ContainsKey(request.RankId))
                    return BadRequest(new { error = $"Invalid rankId: {request.RankId}" });

                // Resolve player name to account ID
                var accountId = _redis.ResolveAccountId(request.GameName);
                if (accountId == null)
                    return NotFound(new { error = $"Player '{request.GameName}' not found in game" });

                // Read current rank
                var currentRankStr = _redis.HashGet($"account.{accountId}", "rank");
                var currentRank = 0;
                if (!string.IsNullOrEmpty(currentRankStr))
                    int.TryParse(currentRankStr, out currentRank);

                var newRank = RankMap[request.RankId];

                // Anti-grief: only upgrade, never downgrade
                if (newRank <= currentRank)
                {
                    return Ok(new
                    {
                        success = true,
                        upgraded = false,
                        message = $"Player already has rank {currentRank} (>= {newRank}), no change",
                        previousRank = currentRank,
                        newRank = currentRank,
                    });
                }

                // Set the new rank
                _redis.HashSet($"account.{accountId}", "rank", newRank.ToString());

                // Set notification flag in Redis (7 day TTL)
                var rankName = RankNames.GetValueOrDefault(request.RankId, request.RankId);
                SetRankNotification(accountId, request.RankId, rankName);

                Console.WriteLine($"[Donations] Granted rank {newRank} ({request.RankId}) to '{request.GameName}' (account {accountId}), was {currentRank}");

                return Ok(new
                {
                    success = true,
                    upgraded = true,
                    previousRank = currentRank,
                    newRank,
                    accountId,
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Donations] Error granting rank: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("check-pending")]
        public async Task<IActionResult> CheckPending([FromQuery] string gameName)
        {
            try
            {
                // Verify webhook secret
                var expectedSecret = Environment.GetEnvironmentVariable("WEBHOOK_SECRET") ?? "";
                var providedSecret = Request.Headers["X-Webhook-Secret"].ToString();

                if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
                    return Unauthorized(new { error = "Invalid webhook secret" });

                if (string.IsNullOrWhiteSpace(gameName))
                    return BadRequest(new { error = "gameName is required" });

                if (!_supabase.IsConfigured)
                    return Ok(new { granted = Array.Empty<object>(), message = "Supabase not configured" });

                var pending = await _supabase.GetPendingPurchases(gameName);
                if (pending.Count == 0)
                    return Ok(new { granted = Array.Empty<object>() });

                // Resolve player
                var accountId = _redis.ResolveAccountId(gameName);
                if (accountId == null)
                    return Ok(new { granted = Array.Empty<object>(), message = "Player not found in Redis" });

                // Read current rank
                var currentRankStr = _redis.HashGet($"account.{accountId}", "rank");
                var currentRank = 0;
                if (!string.IsNullOrEmpty(currentRankStr))
                    int.TryParse(currentRankStr, out currentRank);

                var granted = new List<object>();
                var highestNewRank = currentRank;
                string highestRankId = null;

                foreach (var purchase in pending)
                {
                    var rankId = purchase["rank_id"]?.ToString();
                    var sessionId = purchase["stripe_session_id"]?.ToString();

                    if (string.IsNullOrEmpty(rankId) || !RankMap.ContainsKey(rankId))
                    {
                        // Mark invalid purchases as granted to not retry
                        if (!string.IsNullOrEmpty(sessionId))
                            await _supabase.MarkPurchaseGranted(sessionId);
                        continue;
                    }

                    var rankNum = RankMap[rankId];
                    if (rankNum > highestNewRank)
                    {
                        highestNewRank = rankNum;
                        highestRankId = rankId;
                    }

                    granted.Add(new { rankId, rankName = RankNames.GetValueOrDefault(rankId, rankId) });

                    // Mark as granted in Supabase
                    if (!string.IsNullOrEmpty(sessionId))
                        await _supabase.MarkPurchaseGranted(sessionId);
                }

                // Apply highest rank if upgrade
                if (highestNewRank > currentRank && highestRankId != null)
                {
                    _redis.HashSet($"account.{accountId}", "rank", highestNewRank.ToString());
                    var rankName = RankNames.GetValueOrDefault(highestRankId, highestRankId);
                    SetRankNotification(accountId, highestRankId, rankName);
                    Console.WriteLine($"[Donations] Recovery: granted rank {highestNewRank} ({highestRankId}) to '{gameName}' (account {accountId}), was {currentRank}");
                }

                return Ok(new { granted });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Donations] Error checking pending: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private void SetRankNotification(string accountId, string rankId, string rankName)
        {
            try
            {
                var key = $"rank_notification:{accountId}";
                var value = JsonConvert.SerializeObject(new { rankId, rankName });
                _redis.StringSet(key, value);
                // Set 7 day TTL via raw command
                _redis.KeyExpire(key, TimeSpan.FromDays(7));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Donations] Failed to set notification: {ex.Message}");
            }
        }
    }

    public class GrantRankRequest
    {
        public string GameName { get; set; }
        public string RankId { get; set; }
    }
}
