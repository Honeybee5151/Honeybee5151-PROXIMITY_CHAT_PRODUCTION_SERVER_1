using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using System;
using System.Collections.Generic;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/donations")]
    public class DonationsController : ControllerBase
    {
        private readonly RedisService _redis;

        private static readonly Dictionary<string, int> RankMap = new()
        {
            { "mute", 1 },       // Mute ($1)
            { "whisperer", 2 },  // Whisperer ($10)
            { "chatter", 3 },    // Chatter ($100)
        };

        public DonationsController(RedisService redis)
        {
            _redis = redis;
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
    }

    public class GrantRankRequest
    {
        public string GameName { get; set; }
        public string RankId { get; set; }
    }
}
