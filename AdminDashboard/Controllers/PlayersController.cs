using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly RedisService _redis;

        public PlayersController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpGet("online")]
        public IActionResult GetOnlinePlayers([FromQuery] string search = null)
        {
            try
            {
                var entries = _redis.Database.HashGetAll("admin:online_players");
                if (entries.Length == 0)
                    return Ok(new { count = 0, players = Array.Empty<object>(), message = "WorldServer not publishing player data yet." });

                var players = new List<Dictionary<string, object>>();
                foreach (var entry in entries)
                {
                    try
                    {
                        var player = JsonConvert.DeserializeObject<Dictionary<string, object>>(entry.Value.ToString());
                        if (player != null)
                            players.Add(player);
                    }
                    catch { /* skip malformed entries */ }
                }

                // Filter by search term
                if (!string.IsNullOrEmpty(search))
                {
                    var searchLower = search.ToLower();
                    players = players.Where(p =>
                        (p.TryGetValue("name", out var name) && name?.ToString().ToLower().Contains(searchLower) == true) ||
                        (p.TryGetValue("accountId", out var id) && id?.ToString().Contains(search) == true)
                    ).ToList();
                }

                return Ok(new { count = players.Count, players });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
