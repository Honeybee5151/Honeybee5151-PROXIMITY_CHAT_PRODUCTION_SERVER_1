using Microsoft.AspNetCore.Mvc;
using System;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        [HttpPost("validate")]
        public IActionResult Validate([FromBody] TokenRequest request)
        {
            var adminToken = Environment.GetEnvironmentVariable("ADMIN_TOKEN") ?? "";

            if (string.IsNullOrEmpty(adminToken) || request.Token == adminToken)
                return Ok(new { valid = true });

            return Unauthorized(new { valid = false });
        }
    }

    public class TokenRequest
    {
        public string Token { get; set; }
    }
}
