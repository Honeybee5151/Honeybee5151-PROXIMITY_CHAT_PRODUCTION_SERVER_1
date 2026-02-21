using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using System;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/redis")]
    public class RedisController : ControllerBase
    {
        private readonly RedisService _redis;

        public RedisController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpGet("info")]
        public IActionResult GetInfo()
        {
            try
            {
                var stats = _redis.GetMemoryStats();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("keys")]
        public IActionResult GetKeys([FromQuery] string pattern = "*", [FromQuery] int count = 50, [FromQuery] long cursor = 0)
        {
            try
            {
                // Safety: cap at 50 keys per request
                if (count > 50) count = 50;
                if (count < 1) count = 1;

                var (keys, nextCursor) = _redis.ScanKeys(pattern, count, cursor);

                return Ok(new
                {
                    keys,
                    nextCursor,
                    count = keys.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("key/{*key}")]
        public IActionResult GetKey(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return BadRequest(new { error = "Key is required" });

                var (type, value) = _redis.GetKeyValue(key);
                var ttl = _redis.GetKeyTtl(key);

                return Ok(new
                {
                    key,
                    type,
                    value,
                    ttl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
