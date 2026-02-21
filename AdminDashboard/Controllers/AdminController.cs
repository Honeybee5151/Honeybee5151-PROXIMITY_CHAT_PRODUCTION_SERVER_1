using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using System;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly RedisService _redis;

        public AdminController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpPost("kick")]
        public IActionResult KickPlayer([FromBody] KickRequest request)
        {
            try
            {
                // Publish kick command to Redis pub/sub (WorldServer listens on "Admin" channel)
                var msg = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    InstId = "admin-dashboard",
                    Content = new { Command = "kick", Parameter = request.AccountId.ToString() }
                });
                _redis.Multiplexer.GetSubscriber().Publish("Admin", msg);

                return Ok(new { success = true, message = $"Kick command sent for account {request.AccountId}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("announce")]
        public IActionResult Announce([FromBody] AnnounceRequest request)
        {
            try
            {
                var msg = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    InstId = "admin-dashboard",
                    Content = new { Command = "announce", Parameter = request.Message }
                });
                _redis.Multiplexer.GetSubscriber().Publish("Admin", msg);

                return Ok(new { success = true, message = "Announcement sent" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("voice/restart")]
        public IActionResult RestartVoice()
        {
            try
            {
                var msg = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    InstId = "admin-dashboard",
                    Content = new { Command = "voice_restart", Parameter = "" }
                });
                _redis.Multiplexer.GetSubscriber().Publish("Admin", msg);

                return Ok(new { success = true, message = "Voice restart command sent" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("voice/testmode")]
        public IActionResult ToggleTestMode([FromBody] TestModeRequest request)
        {
            try
            {
                var msg = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    InstId = "admin-dashboard",
                    Content = new { Command = "voice_testmode", Parameter = request.Enabled.ToString().ToLower() }
                });
                _redis.Multiplexer.GetSubscriber().Publish("Admin", msg);

                return Ok(new { success = true, message = $"Voice test mode set to {request.Enabled}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class KickRequest { public int AccountId { get; set; } }
    public class AnnounceRequest { public string Message { get; set; } }
    public class TestModeRequest { public bool Enabled { get; set; } }
}
