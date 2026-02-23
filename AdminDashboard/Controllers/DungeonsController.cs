using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/dungeons")]
    public class DungeonsController : ControllerBase
    {
        private readonly SupabaseService _supabase;
        private readonly GitHubService _github;

        public DungeonsController(SupabaseService supabase, GitHubService github)
        {
            _supabase = supabase;
            _github = github;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPending()
        {
            try
            {
                if (!_supabase.IsConfigured)
                    return StatusCode(500, new { error = "Supabase not configured" });

                var dungeons = await _supabase.GetDungeons("pending");
                // Return lightweight list (no full map data)
                var list = dungeons.Select(d => new
                {
                    id = d["id"]?.ToString(),
                    title = d["title"]?.ToString(),
                    description = d["description"]?.ToString(),
                    created_at = d["created_at"]?.ToString(),
                    has_map = d["map_jm"] != null && d["map_jm"].Type != JTokenType.Null,
                    has_xml = d["dungeon_xml"] != null && d["dungeon_xml"].Type != JTokenType.Null,
                    has_custom_tiles = d["custom_tiles"] != null && d["custom_tiles"].Type != JTokenType.Null,
                    boss_count = d["bosses"] is JArray bosses ? bosses.Count : 0,
                    item_count = d["items"] is JArray items ? items.Count : 0,
                }).ToList();

                return Ok(new { dungeons = list });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("approve")]
        public async Task<IActionResult> Approve([FromBody] ApproveRequest request)
        {
            try
            {
                if (!_supabase.IsConfigured || !_github.IsConfigured)
                    return StatusCode(500, new { error = "Services not configured" });

                // 1. Fetch dungeon from Supabase
                var dungeon = await _supabase.GetDungeon(request.DungeonId);
                if (dungeon == null)
                    return NotFound(new { error = "Dungeon not found" });

                var title = dungeon["title"]?.ToString() ?? "Untitled";
                var mapJm = dungeon["map_jm"];
                var customTiles = dungeon["custom_tiles"] as JObject;

                if (mapJm == null || mapJm.Type == JTokenType.Null)
                    return BadRequest(new { error = "Dungeon has no map data" });

                // Sanitize title for filename (keep alphanumeric, spaces, dashes)
                var safeTitle = Regex.Replace(title, @"[^\w\s\-']", "").Trim();
                if (string.IsNullOrEmpty(safeTitle)) safeTitle = $"dungeon_{request.DungeonId}";

                // Block path traversal attempts
                if (safeTitle.Contains("..") || safeTitle.Contains('/') || safeTitle.Contains('\\'))
                    return BadRequest(new { error = "Invalid dungeon title" });
                if (safeTitle.Length > 100)
                    safeTitle = safeTitle.Substring(0, 100).Trim();

                var files = new List<(string Path, string Content)>();

                // 2. Write .jm map file
                var jmContent = mapJm.ToString(Newtonsoft.Json.Formatting.None);
                files.Add(($"Shared/resources/worlds/Dungeons/{safeTitle}.jm", jmContent));

                // 3. Generate custom Ground XML entries if needed
                if (customTiles != null && customTiles.Count > 0)
                {
                    var customGroundIds = FindCustomGroundIds(mapJm);
                    if (customGroundIds.Count > 0)
                    {
                        // Build reverse map: customId → hex
                        var idToHex = new Dictionary<string, string>();
                        foreach (var prop in customTiles.Properties())
                            idToHex[prop.Value.ToString()] = prop.Name;

                        // Fetch existing CustomGrounds.xml to find next available type code
                        var (existingXml, _) = await _github.FetchFile("Shared/resources/xml/custom/CustomGrounds.xml");
                        var nextType = FindNextTypeCode(existingXml);

                        var newEntries = "";
                        foreach (var customId in customGroundIds)
                        {
                            if (!idToHex.TryGetValue(customId, out var hex)) continue;
                            newEntries += $"\t<Ground type=\"0x{nextType:x4}\" id=\"{EscapeXml(customId)}\">\n";
                            newEntries += $"\t\t<Texture>\n\t\t\t<File>lofiEnvironment2</File>\n\t\t\t<Index>0x0b</Index>\n\t\t</Texture>\n";
                            newEntries += $"\t\t<Color>0x{hex}</Color>\n";
                            newEntries += $"\t</Ground>\n";
                            nextType++;
                        }

                        var updatedXml = existingXml.Replace("</GroundTypes>", newEntries + "</GroundTypes>");
                        files.Add(("Shared/resources/xml/custom/CustomGrounds.xml", updatedXml));
                    }
                }

                // 4. Add World entry to Dungeons.xml
                var (dungeonsXml, _) = await _github.FetchFile("Shared/resources/xml/Dungeons.xml");

                // Check for duplicate
                if (dungeonsXml.Contains($"id=\"{EscapeXml(safeTitle)}\""))
                    return BadRequest(new { error = $"A dungeon named '{safeTitle}' already exists in Dungeons.xml" });

                var width = mapJm["width"]?.Value<int>() ?? 256;
                var height = mapJm["height"]?.Value<int>() ?? 256;

                var worldEntry = $"\t<World id=\"{EscapeXml(safeTitle)}\">\n" +
                    $"\t\t<Width>{width}</Width>\n" +
                    $"\t\t<Height>{height}</Height>\n" +
                    $"\t\t<MapJM>Dungeons/{safeTitle}.jm</MapJM>\n" +
                    $"\t\t<VisibilityType>1</VisibilityType>\n" +
                    $"\t\t<Dungeon/>\n" +
                    $"\t</World>\n";

                var updatedDungeonsXml = dungeonsXml.Replace("</Worlds>", worldEntry + "</Worlds>");
                files.Add(("Shared/resources/xml/Dungeons.xml", updatedDungeonsXml));

                // 5. Atomic commit to GitHub
                await _github.CommitFiles(files, $"Add community dungeon: {safeTitle}");

                // 6. Update status in Supabase
                await _supabase.UpdateStatus(request.DungeonId, "approved");

                return Ok(new { success = true, message = $"Dungeon '{safeTitle}' approved and pushed to server repo" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DungeonsController] Approve error: {ex}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("reject")]
        public async Task<IActionResult> Reject([FromBody] RejectRequest request)
        {
            try
            {
                if (!_supabase.IsConfigured)
                    return StatusCode(500, new { error = "Supabase not configured" });

                await _supabase.UpdateStatus(request.DungeonId, "rejected");
                return Ok(new { success = true, message = "Dungeon rejected" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Scan JM dict for ground IDs starting with "custom_"</summary>
        private static List<string> FindCustomGroundIds(JToken mapJm)
        {
            var ids = new HashSet<string>();
            var dict = mapJm["dict"] as JArray;
            if (dict == null) return ids.ToList();

            foreach (var entry in dict)
            {
                var ground = entry["ground"]?.ToString();
                if (ground != null && ground.StartsWith("custom_"))
                    ids.Add(ground);
            }
            return ids.ToList();
        }

        /// <summary>Find the next available type code in CustomGrounds.xml (starts at 0xF000)</summary>
        private static int FindNextTypeCode(string xml)
        {
            var matches = Regex.Matches(xml, @"type=""0x([0-9a-fA-F]+)""");
            int max = 0xEFFF; // so first will be 0xF000
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int val))
                    if (val > max) max = val;
            }
            return max + 1;
        }

        private static string EscapeXml(string s) => s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    public class ApproveRequest
    {
        public string DungeonId { get; set; }
    }

    public class RejectRequest
    {
        public string DungeonId { get; set; }
    }
}
