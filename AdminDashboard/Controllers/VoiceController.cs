using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using System;
using System.Collections.Generic;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/voice")]
    public class VoiceController : ControllerBase
    {
        private readonly RedisService _redis;

        public VoiceController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            try
            {
                var entries = _redis.Database.HashGetAll("admin:voice_stats");
                if (entries.Length == 0)
                    return Ok(new { status = "no_data", message = "WorldServer not publishing voice stats yet." });

                var stats = new Dictionary<string, string>();
                foreach (var entry in entries)
                    stats[entry.Name.ToString()] = entry.Value.ToString();

                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
