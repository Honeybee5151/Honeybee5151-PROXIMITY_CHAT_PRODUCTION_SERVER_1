//8812938
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

        //8812938 — write/edit/delete endpoints

        [HttpPut("key/{*key}")]
        public IActionResult SetKey(string key, [FromBody] SetKeyRequest req)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return BadRequest(new { error = "Key is required" });

                _redis.StringSet(key, req.Value ?? "");
                return Ok(new { message = "OK" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("key/{*key}")]
        public IActionResult DeleteKey(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return BadRequest(new { error = "Key is required" });

                var deleted = _redis.DeleteKey(key);
                return Ok(new { deleted, message = deleted ? "Key deleted" : "Key not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("hash/{*key}")]
        public IActionResult SetHashField(string key, [FromBody] HashFieldRequest req)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(req.Field))
                    return BadRequest(new { error = "Key and field are required" });

                _redis.HashSet(key, req.Field, req.Value ?? "");
                return Ok(new { message = "OK" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("hash/{*key}")]
        public IActionResult DeleteHashField(string key, [FromQuery] string field)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(field))
                    return BadRequest(new { error = "Key and field are required" });

                var deleted = _redis.HashDelete(key, field);
                return Ok(new { deleted, message = deleted ? "Field deleted" : "Field not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("list/{*key}")]
        public IActionResult SetListItem(string key, [FromBody] ListItemRequest req)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return BadRequest(new { error = "Key is required" });

                if (req.Index.HasValue)
                    _redis.ListSet(key, req.Index.Value, req.Value ?? "");
                else
                    _redis.ListPush(key, req.Value ?? "");
                return Ok(new { message = "OK" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("list/{*key}")]
        public IActionResult DeleteListItem(string key, [FromQuery] string value)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || value == null)
                    return BadRequest(new { error = "Key and value are required" });

                var removed = _redis.ListRemove(key, value);
                return Ok(new { removed, message = removed > 0 ? "Item removed" : "Item not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("set/{*key}")]
        public IActionResult AddSetMember(string key, [FromBody] SetMemberRequest req)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return BadRequest(new { error = "Key is required" });

                _redis.SetAdd(key, req.Value ?? "");
                return Ok(new { message = "OK" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("set/{*key}")]
        public IActionResult RemoveSetMember(string key, [FromQuery] string value)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || value == null)
                    return BadRequest(new { error = "Key and value are required" });

                var removed = _redis.SetRemove(key, value);
                return Ok(new { removed, message = removed ? "Member removed" : "Member not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPut("zset/{*key}")]
        public IActionResult AddSortedSetMember(string key, [FromBody] SortedSetRequest req)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    return BadRequest(new { error = "Key is required" });

                _redis.SortedSetAdd(key, req.Member ?? "", req.Score);
                return Ok(new { message = "OK" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpDelete("zset/{*key}")]
        public IActionResult RemoveSortedSetMember(string key, [FromQuery] string member)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || member == null)
                    return BadRequest(new { error = "Key and member are required" });

                var removed = _redis.SortedSetRemove(key, member);
                return Ok(new { removed, message = removed ? "Member removed" : "Member not found" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class SetKeyRequest { public string Value { get; set; } }
    public class HashFieldRequest { public string Field { get; set; } public string Value { get; set; } }
    public class ListItemRequest { public int? Index { get; set; } public string Value { get; set; } }
    public class SetMemberRequest { public string Value { get; set; } }
    public class SortedSetRequest { public string Member { get; set; } public double Score { get; set; } }
}
