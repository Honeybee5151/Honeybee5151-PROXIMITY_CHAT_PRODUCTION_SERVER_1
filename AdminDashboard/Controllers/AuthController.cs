using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly RedisService _redis;

        public AuthController(RedisService redis)
        {
            _redis = redis;
        }

        [HttpPost("validate")]
        public IActionResult Validate([FromBody] TokenRequest request)
        {
            var adminToken = _redis.Database.StringGet("admin:token").ToString();

            if (!string.IsNullOrEmpty(adminToken) && request.Token == adminToken)
                return Ok(new { valid = true });

            return Unauthorized(new { valid = false });
        }
    }

    public class TokenRequest
    {
        public string Token { get; set; }
    }
}
