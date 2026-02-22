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

        // Helper to publish admin command via Redis pub/sub
        private void PublishAdmin(string command, string parameter = "")
        {
            var msg = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                InstId = "admin-dashboard",
                Content = new { Command = command, Parameter = parameter }
            });
            _redis.Multiplexer.GetSubscriber().Publish("Admin", msg);
        }

        //8812938 — server announcement

        [HttpPost("announce")]
        public IActionResult Announce([FromBody] AnnounceRequest request)
        {
            try
            {
                PublishAdmin("announce", request.Message);
                return Ok(new { success = true, message = "Announcement sent" });
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
                PublishAdmin("maintenance", request.Enabled.ToString().ToLower());
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

        //8812938 — server-wide loot event boost

        [HttpPost("lootevent")]
        public IActionResult SetLootEvent([FromBody] EventBoostRequest request)
        {
            try
            {
                // Value is a percentage like 0.25 = 25%, 0.5 = 50%, 0 = off
                PublishAdmin("loot_event", request.Percent.ToString());
                return Ok(new { success = true, message = request.Percent > 0 ? $"Loot event set to {Math.Round(request.Percent * 100)}%" : "Loot event disabled" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — server-wide XP/fame event boost

        [HttpPost("expevent")]
        public IActionResult SetExpEvent([FromBody] EventBoostRequest request)
        {
            try
            {
                PublishAdmin("exp_event", request.Percent.ToString());
                return Ok(new { success = true, message = request.Percent > 0 ? $"XP/Fame event set to {Math.Round(request.Percent * 100)}%" : "XP/Fame event disabled" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — server-wide gift credits/fame to all online players

        [HttpPost("giftall")]
        public IActionResult GiftAll([FromBody] GiftAllRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ItemName))
                    return BadRequest(new { error = "Item name is required" });

                // Send item name via pub/sub — WorldServer looks it up in game data
                PublishAdmin("gift_all", request.ItemName.Trim());

                return Ok(new { success = true, message = $"Gifting '{request.ItemName.Trim()}' to all online players" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — kick player

        [HttpPost("kick")]
        public IActionResult KickPlayer([FromBody] KickRequest request)
        {
            try
            {
                PublishAdmin("kick", request.AccountId.ToString());
                return Ok(new { success = true, message = $"Kick command sent for account {request.AccountId}" });
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
                PublishAdmin("kick", request.AccountId.ToString());

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

        //8812938 — voice system

        [HttpPost("voice/restart")]
        public IActionResult RestartVoice()
        {
            try
            {
                PublishAdmin("voice_restart");
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
                PublishAdmin("voice_testmode", request.Enabled.ToString().ToLower());
                return Ok(new { success = true, message = $"Voice test mode set to {request.Enabled}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — admin command line (arbitrary pub/sub command)

        [HttpPost("command")]
        public IActionResult SendCommand([FromBody] CommandRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Command))
                    return BadRequest(new { error = "Command is required" });

                PublishAdmin(request.Command.Trim(), request.Parameter?.Trim() ?? "");
                var display = string.IsNullOrWhiteSpace(request.Parameter)
                    ? request.Command.Trim()
                    : $"{request.Command.Trim()} {request.Parameter.Trim()}";
                return Ok(new { success = true, message = $"Command sent: {display}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //8812938 — raw Redis command execution

        [HttpPost("redis/execute")]
        public IActionResult ExecuteRedis([FromBody] RedisExecuteRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Command))
                    return BadRequest(new { error = "Command is required" });

                var result = _redis.ExecuteCommand(request.Command.Trim());
                return Ok(new { success = true, result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class AnnounceRequest { public string Message { get; set; } }
    public class MaintenanceRequest { public bool Enabled { get; set; } }
    public class EventBoostRequest { public double Percent { get; set; } }
    public class GiftAllRequest { public string ItemName { get; set; } }
    public class KickRequest { public int AccountId { get; set; } }
    public class BanRequest { public int AccountId { get; set; } public string Reason { get; set; } public int? LiftTime { get; set; } }
    public class UnbanRequest { public int AccountId { get; set; } }
    public class IpBanRequest { public string Ip { get; set; } public string Reason { get; set; } }
    public class IpUnbanRequest { public string Ip { get; set; } }
    public class MuteRequest { public int AccountId { get; set; } public int Minutes { get; set; } }
    public class UnmuteRequest { public int AccountId { get; set; } }
    public class TestModeRequest { public bool Enabled { get; set; } }
    public class CommandRequest { public string Command { get; set; } public string Parameter { get; set; } }
    public class RedisExecuteRequest { public string Command { get; set; } }
}
