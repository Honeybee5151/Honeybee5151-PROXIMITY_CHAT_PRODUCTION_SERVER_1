//8812938
using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using System;
using System.Collections.Generic;

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

        //8812938 — ban/unban

        [HttpPost("ban")]
        public IActionResult BanPlayer([FromBody] BanRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                _redis.BanAccount(request.AccountId, request.Reason ?? "Banned via admin dashboard", request.LiftTime ?? -1);

                // Also kick if online
                var msg = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    InstId = "admin-dashboard",
                    Content = new { Command = "kick", Parameter = request.AccountId.ToString() }
                });
                _redis.Multiplexer.GetSubscriber().Publish("Admin", msg);

                return Ok(new { success = true, message = $"Account {request.AccountId} banned and kicked" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("unban")]
        public IActionResult UnbanPlayer([FromBody] UnbanRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                _redis.UnbanAccount(request.AccountId);
                return Ok(new { success = true, message = $"Account {request.AccountId} unbanned" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — IP ban/unban

        [HttpPost("banip")]
        public IActionResult BanIp([FromBody] IpBanRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Ip))
                    return BadRequest(new { error = "IP address required" });

                _redis.BanIp(request.Ip.Trim(), request.Reason ?? "IP banned via admin dashboard");
                return Ok(new { success = true, message = $"IP {request.Ip} banned" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("unbanip")]
        public IActionResult UnbanIp([FromBody] IpUnbanRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Ip))
                    return BadRequest(new { error = "IP address required" });

                _redis.UnbanIp(request.Ip.Trim());
                return Ok(new { success = true, message = $"IP {request.Ip} unbanned" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — mute/unmute

        [HttpPost("mute")]
        public IActionResult MutePlayer([FromBody] MuteRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                var ip = _redis.GetAccountIp(request.AccountId);
                if (string.IsNullOrEmpty(ip))
                    return Ok(new { success = false, message = "Could not find IP for this account" });

                TimeSpan? duration = request.Minutes > 0 ? TimeSpan.FromMinutes(request.Minutes) : null;
                _redis.MuteIp(ip, duration);

                var durationText = duration.HasValue ? $"for {request.Minutes} minutes" : "permanently";
                return Ok(new { success = true, message = $"Account {request.AccountId} (IP: {ip}) muted {durationText}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("unmute")]
        public IActionResult UnmutePlayer([FromBody] UnmuteRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                var ip = _redis.GetAccountIp(request.AccountId);
                if (string.IsNullOrEmpty(ip))
                    return Ok(new { success = false, message = "Could not find IP for this account" });

                _redis.UnmuteIp(ip);
                return Ok(new { success = true, message = $"Account {request.AccountId} (IP: {ip}) unmuted" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — set rank

        [HttpPost("setrank")]
        public IActionResult SetRank([FromBody] SetRankRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                _redis.SetRank(request.AccountId, request.Rank);
                return Ok(new { success = true, message = $"Account {request.AccountId} rank set to {request.Rank}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — gift credits/fame

        [HttpPost("gift")]
        public IActionResult GiftCurrency([FromBody] GiftRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                var messages = new List<string>();

                if (request.Credits != 0)
                {
                    _redis.AddCredits(request.AccountId, request.Credits);
                    messages.Add($"{(request.Credits > 0 ? "+" : "")}{request.Credits} credits");
                }
                if (request.Fame != 0)
                {
                    _redis.AddFame(request.AccountId, request.Fame);
                    messages.Add($"{(request.Fame > 0 ? "+" : "")}{request.Fame} fame");
                }

                if (messages.Count == 0)
                    return BadRequest(new { error = "Specify credits and/or fame amount" });

                return Ok(new { success = true, message = $"Account {request.AccountId}: {string.Join(", ", messages)}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — boosts

        [HttpPost("lootboost")]
        public IActionResult SetLootBoost([FromBody] BoostRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                int seconds = request.Minutes * 60;
                int count = _redis.SetLootBoost(request.AccountId, seconds);

                return Ok(new { success = true, message = $"Loot boost ({request.Minutes}min) set on {count} alive character(s)" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("fameboost")]
        public IActionResult SetFameBoost([FromBody] BoostRequest request)
        {
            try
            {
                if (request.AccountId <= 0)
                    return BadRequest(new { error = "Valid account ID required" });

                int seconds = request.Minutes * 60;
                int count = _redis.SetXpBoost(request.AccountId, seconds);

                return Ok(new { success = true, message = $"Fame/XP boost ({request.Minutes}min) set on {count} alive character(s)" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — maintenance mode (block non-admin)

        [HttpPost("maintenance")]
        public IActionResult ToggleMaintenance([FromBody] MaintenanceRequest request)
        {
            try
            {
                var msg = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    InstId = "admin-dashboard",
                    Content = new { Command = "maintenance", Parameter = request.Enabled.ToString().ToLower() }
                });
                _redis.Multiplexer.GetSubscriber().Publish("Admin", msg);

                // Also store in Redis so WorldServer can check on startup
                _redis.Database.StringSet("admin:maintenance", request.Enabled.ToString().ToLower());

                return Ok(new { success = true, message = request.Enabled ? "Maintenance mode ON — non-admin players blocked" : "Maintenance mode OFF — all players allowed" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("maintenance")]
        public IActionResult GetMaintenanceStatus()
        {
            try
            {
                var val = _redis.Database.StringGet("admin:maintenance");
                var enabled = val == "true";
                return Ok(new { enabled });
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
    public class BanRequest { public int AccountId { get; set; } public string Reason { get; set; } public int? LiftTime { get; set; } }
    public class UnbanRequest { public int AccountId { get; set; } }
    public class IpBanRequest { public string Ip { get; set; } public string Reason { get; set; } }
    public class IpUnbanRequest { public string Ip { get; set; } }
    public class MuteRequest { public int AccountId { get; set; } public int Minutes { get; set; } }
    public class UnmuteRequest { public int AccountId { get; set; } }
    public class SetRankRequest { public int AccountId { get; set; } public int Rank { get; set; } }
    public class GiftRequest { public int AccountId { get; set; } public int Credits { get; set; } public int Fame { get; set; } }
    public class BoostRequest { public int AccountId { get; set; } public int Minutes { get; set; } }
    public class MaintenanceRequest { public bool Enabled { get; set; } }
}
