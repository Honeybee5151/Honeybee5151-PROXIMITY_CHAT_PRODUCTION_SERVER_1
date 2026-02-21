using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/status")]
    public class StatusController : ControllerBase
    {
        private readonly RedisService _redis;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        public StatusController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview()
        {
            var result = new Dictionary<string, object>();

            // Redis status
            try
            {
                var redisStats = _redis.GetMemoryStats();
                result["redis"] = new
                {
                    status = "running",
                    stats = redisStats
                };
            }
            catch (Exception ex)
            {
                result["redis"] = new { status = "error", error = ex.Message };
            }

            // WorldServer status from Redis (admin:server_info hash)
            try
            {
                var serverInfo = _redis.Database.HashGetAll("admin:server_info");
                if (serverInfo.Length > 0)
                {
                    var info = new Dictionary<string, string>();
                    foreach (var entry in serverInfo)
                        info[entry.Name.ToString()] = entry.Value.ToString();
                    result["worldserver"] = new { status = "running", info };
                }
                else
                {
                    result["worldserver"] = new { status = "unknown", info = "No stats published yet. WorldServer may need AdminStatsPublisher." };
                }
            }
            catch (Exception ex)
            {
                result["worldserver"] = new { status = "error", error = ex.Message };
            }

            // AppServer status via HTTP health probe
            try
            {
                var isDocker = Environment.GetEnvironmentVariable("IS_DOCKER") != null;
                var appUrl = isDocker ? "http://appserver:8080" : "http://localhost:8888";
                var response = await _httpClient.GetAsync($"{appUrl}/app/init");
                result["appserver"] = new
                {
                    status = response.IsSuccessStatusCode ? "running" : "error",
                    statusCode = (int)response.StatusCode
                };
            }
            catch (Exception ex)
            {
                result["appserver"] = new { status = "unreachable", error = ex.Message };
            }

            return Ok(result);
        }

        [HttpGet("redis")]
        public IActionResult GetRedisStatus()
        {
            try
            {
                var info = _redis.GetInfo();
                return Ok(info);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
