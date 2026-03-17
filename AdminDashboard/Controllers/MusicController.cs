// Music proxy controller — serves external music URLs to Flash client
// Unauthenticated endpoint so Flash Sound.load() can fetch audio directly
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
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

        // Cache for resolved MP3 URLs (page url -> direct mp3 url)
        private static readonly ConcurrentDictionary<string, string> _resolvedUrlCache = new();

        static MusicController()
        {
            // FMA requires a browser-like User-Agent
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        /// <summary>
        /// Extracts a direct MP3 URL from a FMA track page using data-track-info JSON.
        /// Returns null if not found.
        /// </summary>
        private async Task<string?> ResolveFmaPageToMp3(string pageUrl)
        {
            if (_resolvedUrlCache.TryGetValue(pageUrl, out var cachedMp3))
            {
                Console.WriteLine($"[Music] Using cached resolved URL: {cachedMp3}");
                return cachedMp3;
            }

            Console.WriteLine($"[Music] Scraping FMA page for MP3: {pageUrl}");

            string html;
            try
            {
                html = await _httpClient.GetStringAsync(pageUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Music] Failed to fetch FMA page: {ex.Message}");
                return null;
            }

            // Extract data-track-info JSON attribute from the page
            var match = Regex.Match(html, @"data-track-info=""([^""]+)""");
            if (!match.Success)
            {
                Console.WriteLine($"[Music] Could not find data-track-info on page: {pageUrl}");
                return null;
            }

            // Decode HTML entities (&quot; -> ")
            var json = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);

            try
            {
                var trackInfo = System.Text.Json.JsonDocument.Parse(json);

                // Prefer fileUrl (direct CDN mp3), fall back to downloadUrl
                if (trackInfo.RootElement.TryGetProperty("fileUrl", out var fileUrl))
                {
                    var mp3 = fileUrl.GetString();
                    Console.WriteLine($"[Music] Resolved fileUrl: {mp3}");
                    _resolvedUrlCache[pageUrl] = mp3!;
                    return mp3;
                }

                if (trackInfo.RootElement.TryGetProperty("downloadUrl", out var downloadUrl))
                {
                    var dl = downloadUrl.GetString();
                    Console.WriteLine($"[Music] Resolved downloadUrl: {dl}");
                    _resolvedUrlCache[pageUrl] = dl!;
                    return dl;
                }

                Console.WriteLine($"[Music] data-track-info had no fileUrl or downloadUrl: {json}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Music] Failed to parse track JSON: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Proxy endpoint: fetches audio from external URL and streams to client.
        /// Flash client calls: http://server:8889/api/music/proxy?url=encoded_url
        /// Supports both direct audio URLs and FMA track page URLs.
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

            // Add CORS headers early so Flash always gets them
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Access-Control-Allow-Methods"] = "GET";

            try
            {
                // Check audio cache first (keyed on original url)
                if (_cache.TryGetValue(url, out var cached) &&
                    DateTime.UtcNow - cached.CachedAt < CacheDuration)
                {
                    Console.WriteLine($"[Music] Serving cached audio: {url} ({cached.Data.Length} bytes)");
                    return File(cached.Data, cached.ContentType);
                }

                // Determine the actual download URL
                string downloadUrl = url;

                bool looksLikePageUrl = url.Contains("/music/") &&
                                        !url.EndsWith(".mp3") &&
                                        !url.EndsWith(".ogg") &&
                                        !url.EndsWith(".flac") &&
                                        !url.Contains("files.freemusicarchive.org");

                if (looksLikePageUrl)
                {
                    var resolved = await ResolveFmaPageToMp3(url);
                    if (resolved == null)
                    {
                        return StatusCode(502,
                            "Could not find a playable audio link on the FMA page. " +
                            "Try using a direct files.freemusicarchive.org MP3 URL instead.");
                    }
                    downloadUrl = resolved;
                }

                Console.WriteLine($"[Music] Fetching audio: {downloadUrl}");

                var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Music] HTTP {response.StatusCode} fetching: {downloadUrl}");
                    return StatusCode((int)response.StatusCode, $"Failed to fetch music (HTTP {response.StatusCode})");
                }

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
                var data = await response.Content.ReadAsByteArrayAsync();

                Console.WriteLine($"[Music] Fetched: {downloadUrl} ({data.Length} bytes, {contentType})");

                // Validate it's actually audio
                if (contentType.Contains("text/html") || data.Length < 1000)
                {
                    Console.WriteLine($"[Music] WARNING: Got non-audio response for {downloadUrl} " +
                                      $"(contentType={contentType}, size={data.Length})");

                    // Clear bad resolved URL from cache so next request retries scraping
                    _resolvedUrlCache.TryRemove(url, out _);

                    return StatusCode(502, "Music source returned non-audio content. The page structure may have changed.");
                }

                // Cache the audio bytes
                _cache[url] = (data, contentType, DateTime.UtcNow);

                return File(data, contentType);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"[Music] Timeout fetching: {url}");
                return StatusCode(504, "Music fetch timed out");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Music] Error proxying {url}: {ex.Message}");
                return StatusCode(502, $"Music proxy error: {ex.Message}");
            }
        }
    }
}