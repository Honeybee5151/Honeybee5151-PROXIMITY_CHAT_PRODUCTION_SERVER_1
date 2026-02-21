using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private readonly RedisService _redis;

        public LogsController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpGet("worldserver")]
        public IActionResult GetWorldServerLogs([FromQuery] int lines = 100, [FromQuery] string filter = null)
        {
            return GetLogs("admin:logs:worldserver", lines, filter);
        }

        [HttpGet("appserver")]
        public IActionResult GetAppServerLogs([FromQuery] int lines = 100, [FromQuery] string filter = null)
        {
            return GetLogs("admin:logs:appserver", lines, filter);
        }

        private IActionResult GetLogs(string key, int lines, string filter)
        {
            try
            {
                if (lines > 1000) lines = 1000;
                if (lines < 1) lines = 1;

                var entries = _redis.Database.ListRange(key, 0, lines - 1);
                var logLines = entries.Select(e => e.ToString()).ToList();

                if (!string.IsNullOrEmpty(filter))
                {
                    var filterLower = filter.ToLower();
                    logLines = logLines.Where(l => l.ToLower().Contains(filterLower)).ToList();
                }

                return Ok(new { count = logLines.Count, lines = logLines });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
