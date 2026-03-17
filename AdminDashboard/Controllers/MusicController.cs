// Music proxy controller — serves external music URLs to Flash client
// Unauthenticated endpoint so Flash Sound.load() can fetch audio directly
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/music")]
    public class MusicController : ControllerBase
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Simple in-memory cache: url -> (bytes, contentType, cachedAt)
        private static readonly ConcurrentDictionary<string, (byte[] Data, string ContentType, DateTime CachedAt)> _cache = new();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(4);

        static MusicController()
        {
            // FMA requires a browser-like User-Agent
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        /// <summary>
        /// Proxy endpoint: fetches audio from external URL and streams to client.
        /// Flash client calls: http://server:8889/api/music/proxy?url=encoded_url
        /// </summary>
        [HttpGet("proxy")]
        public async Task<IActionResult> Proxy([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url))
                return BadRequest("Missing url parameter");

            // Only allow known music domains
            if (!url.StartsWith("https://freemusicarchive.org/") &&
                !url.StartsWith("https://files.freemusicarchive.org/") &&
                !url.StartsWith("http://freemusicarchive.org/"))
            {
                return BadRequest("Only Free Music Archive URLs are supported");
            }

            try
            {
                // Check cache first
                if (_cache.TryGetValue(url, out var cached) &&
                    DateTime.UtcNow - cached.CachedAt < CacheDuration)
                {
                    Console.WriteLine($"[Music] Serving cached: {url} ({cached.Data.Length} bytes)");
                    return File(cached.Data, cached.ContentType);
                }

                // FMA page URL -> download URL transformation
                // Page: https://freemusicarchive.org/music/artist/album/track/
                // Download: https://freemusicarchive.org/music/download/artist/album/track/
                var downloadUrl = url;
                if (url.Contains("/music/") && !url.Contains("/music/download/"))
                {
                    downloadUrl = url.Replace("/music/", "/music/download/");
                }

                Console.WriteLine($"[Music] Fetching: {downloadUrl}");

                var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Music] Failed to fetch ({response.StatusCode}): {downloadUrl}");
                    return StatusCode((int)response.StatusCode, "Failed to fetch music");
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
                var data = await response.Content.ReadAsByteArrayAsync();

                Console.WriteLine($"[Music] Fetched: {downloadUrl} ({data.Length} bytes, {contentType})");

                // Validate it's actually audio (not an HTML page)
                if (contentType.Contains("text/html") || data.Length < 1000)
                {
                    Console.WriteLine($"[Music] WARNING: Got HTML instead of audio for {downloadUrl}");
                    return StatusCode(502, "Music source returned non-audio content");
                }

                // Cache the result
                _cache[url] = (data, contentType, DateTime.UtcNow);

                // Serve with CORS headers for Flash
                Response.Headers["Access-Control-Allow-Origin"] = "*";
                return File(data, contentType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Music] Error proxying {url}: {ex.Message}");
                return StatusCode(502, $"Music proxy error: {ex.Message}");
            }
        }
    }
}
