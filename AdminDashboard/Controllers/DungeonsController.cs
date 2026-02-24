using Microsoft.AspNetCore.Mvc;
using AdminDashboard.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;

namespace AdminDashboard.Controllers
{
    [ApiController]
    [Route("api/dungeons")]
    public class DungeonsController : ControllerBase
    {
        private readonly SupabaseService _supabase;
        private readonly GitHubService _github;
        private static readonly MapThumbnailService _mapThumb = new();

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

        [HttpGet("preview/{id}")]
        public async Task<IActionResult> Preview(string id)
        {
            try
            {
                if (!_supabase.IsConfigured)
                    return StatusCode(500, new { error = "Supabase not configured" });

                var dungeon = await _supabase.GetDungeon(id);
                if (dungeon == null)
                    return NotFound(new { error = "Dungeon not found" });

                var mobs = (dungeon["mobs"] ?? dungeon["bosses"]) as JArray;
                var items = dungeon["items"] as JArray;
                var mapJm = dungeon["map_jm"];
                var customTiles = dungeon["custom_tiles"] as JObject;

                // Build mob preview list
                var mobList = new List<object>();
                if (mobs != null)
                {
                    foreach (var mob in mobs)
                    {
                        var xml = mob["xml"]?.ToString() ?? "";
                        var name = Regex.Match(xml, @"id=""([^""]+)""").Groups[1].Value;
                        mobList.Add(new
                        {
                            name = string.IsNullOrEmpty(name) ? "Unknown Mob" : name,
                            xml,
                            spriteBase = (mob["spriteBase"] ?? mob["sprite"])?.ToString(),
                            spriteAttack = mob["spriteAttack"]?.ToString(),
                            spriteSize = mob["spriteSize"]?.Value<int>() ?? 8,
                            projectileSprites = mob["projectileSprites"] as JObject,
                        });
                    }
                }

                // Build item preview list
                var itemList = new List<object>();
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var xml = item["xml"]?.ToString() ?? "";
                        var name = Regex.Match(xml, @"id=""([^""]+)""").Groups[1].Value;
                        itemList.Add(new
                        {
                            name = string.IsNullOrEmpty(name) ? "Unknown Item" : name,
                            xml,
                            sprite = item["sprite"]?.ToString(),
                        });
                    }
                }

                // Map info + thumbnail
                object mapInfo = null;
                string mapThumbnail = null;
                if (mapJm != null && mapJm.Type != JTokenType.Null)
                {
                    var dictCount = (mapJm["dict"] as JArray)?.Count ?? 0;
                    mapInfo = new
                    {
                        width = mapJm["width"]?.Value<int>() ?? 0,
                        height = mapJm["height"]?.Value<int>() ?? 0,
                        dictEntries = dictCount,
                    };

                    // Generate visual thumbnail
                    var colorsPath = Path.Combine(
                        AppContext.BaseDirectory, "wwwroot", "data", "sprite-colors.json");
                    _mapThumb.LoadColors(colorsPath);
                    mapThumbnail = _mapThumb.GenerateThumbnail(mapJm, customTiles);
                }

                // Custom tiles
                var tileList = new List<object>();
                if (customTiles != null)
                {
                    foreach (var prop in customTiles.Properties())
                        tileList.Add(new { hex = prop.Name, id = prop.Value.ToString() });
                }

                return Ok(new
                {
                    id,
                    title = dungeon["title"]?.ToString(),
                    description = dungeon["description"]?.ToString(),
                    mobs = mobList,
                    items = itemList,
                    map = mapInfo,
                    mapThumbnail,
                    customTiles = tileList,
                });
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
                var binaryFiles = new List<(string Path, byte[] Content)>();

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

                // 4. Generate sprite sheets + write mob/item XMLs
                var mobs = (dungeon["mobs"] ?? dungeon["bosses"]) as JArray;
                var items = dungeon["items"] as JArray;
                var hasMobs = mobs != null && mobs.Count > 0;
                var hasItems = items != null && items.Count > 0;

                if (hasMobs || hasItems)
                {
                    // 4a. Build sprite sheets and assign indices
                    var spriteService = new SpriteSheetService(_github);

                    // Collect sprites grouped by size: (mobIndex, "base"|"attack"|"item", dataUrl, spriteSize)
                    var spriteEntries = new List<(int entityIdx, string frame, string dataUrl, int size, bool isMob)>();

                    if (hasMobs)
                    {
                        for (int i = 0; i < mobs!.Count; i++)
                        {
                            var mob = mobs[i];
                            var size = mob["spriteSize"]?.Value<int>() ?? 8;
                            var baseUrl = (mob["spriteBase"] ?? mob["sprite"])?.ToString();
                            var attackUrl = mob["spriteAttack"]?.ToString();
                            if (!string.IsNullOrEmpty(baseUrl))
                                spriteEntries.Add((i, "base", baseUrl, size, true));
                            if (!string.IsNullOrEmpty(attackUrl))
                                spriteEntries.Add((i, "attack", attackUrl, size, true));

                            // Collect projectile sprites (always 8x8)
                            var projSprites = mob["projectileSprites"] as JObject;
                            if (projSprites != null)
                            {
                                foreach (var prop in projSprites.Properties())
                                {
                                    var dataUrl = prop.Value?.ToString();
                                    if (!string.IsNullOrEmpty(dataUrl))
                                        spriteEntries.Add((i, $"proj_{prop.Name}", dataUrl, 8, true));
                                }
                            }
                        }
                    }
                    if (hasItems)
                    {
                        for (int i = 0; i < items!.Count; i++)
                        {
                            var spriteUrl = items[i]["sprite"]?.ToString();
                            if (!string.IsNullOrEmpty(spriteUrl))
                                spriteEntries.Add((i, "item", spriteUrl, 8, false));
                        }
                    }

                    // Process each sprite size group
                    // mobSpriteIndices[mobIdx] = (baseIndex, attackIndex), itemSpriteIndices[itemIdx] = index
                    var mobSpriteIndices = new Dictionary<int, (int baseIdx, int attackIdx)>();
                    var itemSpriteIndices = new Dictionary<int, int>();
                    var projSpriteIndices = new Dictionary<int, Dictionary<string, int>>(); // mobIdx → {projId → sheetIndex}

                    foreach (var sizeGroup in spriteEntries.GroupBy(e => e.size))
                    {
                        var spriteSize = sizeGroup.Key;
                        var entries = sizeGroup.ToList();
                        var sheetName = spriteSize == 16 ? "communitySprites16x16" : "communitySprites8x8";

                        // Load existing sheet
                        var (sheet, meta) = await spriteService.LoadSheet(spriteSize);

                        // Decode all sprites in this size group
                        var bitmaps = new List<SKBitmap>();
                        try
                        {
                            foreach (var entry in entries)
                                bitmaps.Add(SpriteSheetService.DecodeDataUrl(entry.dataUrl));

                            // Pack into sheet (may replace sheet reference if expanded)
                            var (updatedSheet, indices) = spriteService.AddSprites(sheet, meta, bitmaps, spriteSize);
                            sheet = updatedSheet; // AddSprites disposes old sheet internally if expanded

                            // Map indices back to entities
                            for (int i = 0; i < entries.Count; i++)
                            {
                                var e = entries[i];
                                if (e.isMob)
                                {
                                    if (e.frame.StartsWith("proj_"))
                                    {
                                        var projId = e.frame.Substring(5); // strip "proj_" prefix
                                        if (!projSpriteIndices.ContainsKey(e.entityIdx))
                                            projSpriteIndices[e.entityIdx] = new Dictionary<string, int>();
                                        projSpriteIndices[e.entityIdx][projId] = indices[i];
                                    }
                                    else if (e.frame == "base")
                                    {
                                        var attackIdx = -1;
                                        // Check if next entry is the attack frame for same mob
                                        if (i + 1 < entries.Count && entries[i + 1].entityIdx == e.entityIdx && entries[i + 1].frame == "attack")
                                            attackIdx = indices[i + 1];
                                        mobSpriteIndices[e.entityIdx] = (indices[i], attackIdx);
                                    }
                                    // attack frame index already captured above
                                }
                                else
                                {
                                    itemSpriteIndices[e.entityIdx] = indices[i];
                                }
                            }

                            // Add sheet PNG + metadata to commit
                            var pngBytes = SpriteSheetService.EncodePng(sheet);
                            binaryFiles.Add(($"Shared/resources/sprites/{sheetName}.png", pngBytes));
                            files.Add(($"Shared/resources/sprites/{sheetName}.meta.json",
                                JsonConvert.SerializeObject(meta, Newtonsoft.Json.Formatting.Indented)));
                        }
                        finally
                        {
                            // Always dispose all bitmaps, even on error
                            foreach (var bmp in bitmaps) bmp.Dispose();
                            sheet.Dispose();
                        }
                    }

                    // 4b. Generate custom projectile definitions + rewrite mob ObjectIds
                    if (projSpriteIndices.Count > 0)
                    {
                        var (projXml, _) = await _github.FetchFile("Shared/resources/xml/custom/CustomProj.xml");
                        var projNextType = FindNextTypeCode(projXml, 0x4970);
                        var newProjEntries = "";

                        for (int i = 0; i < mobs!.Count; i++)
                        {
                            if (!projSpriteIndices.TryGetValue(i, out var projMap)) continue;

                            var rawXml = mobs[i]["xml"]?.ToString() ?? "";
                            var mobName = Regex.Match(rawXml, @"id=""([^""]+)""").Groups[1].Value;

                            foreach (var kvp in projMap)
                            {
                                var projId = kvp.Key;
                                var sheetIdx = kvp.Value;
                                var projName = $"{mobName} Proj {projId}";

                                newProjEntries += $"\t<Object type=\"0x{projNextType:x4}\" id=\"{EscapeXml(projName)}\">\n";
                                newProjEntries += $"\t\t<Class>Projectile</Class>\n";
                                newProjEntries += $"\t\t<Texture>\n";
                                newProjEntries += $"\t\t\t<File>communitySprites8x8</File>\n";
                                newProjEntries += $"\t\t\t<Index>{sheetIdx}</Index>\n";
                                newProjEntries += $"\t\t</Texture>\n";
                                newProjEntries += $"\t\t<AngleCorrection>1</AngleCorrection>\n";
                                newProjEntries += $"\t</Object>\n";
                                projNextType++;

                                // Rewrite the mob XML's ObjectId for this projectile slot
                                rawXml = Regex.Replace(
                                    rawXml,
                                    $@"(<Projectile\s+id=""{Regex.Escape(projId)}""[^>]*>[\s\S]*?<ObjectId>)[^<]+(</ObjectId>)",
                                    $"$1{EscapeXml(projName)}$2"
                                );
                            }

                            // Write updated XML back so mob XML writing picks up new ObjectIds
                            mobs[i]["xml"] = rawXml;
                        }

                        if (!string.IsNullOrEmpty(newProjEntries))
                        {
                            var updatedProjXml = projXml.Replace("</Objects>", newProjEntries + "</Objects>");
                            files.Add(("Shared/resources/xml/custom/CustomProj.xml", updatedProjXml));
                        }
                    }

                    // 4c. Write mob XMLs to CustomObjects.xml (with sprite texture refs)
                    var objectsXml = (await _github.FetchFile("Shared/resources/xml/custom/CustomObjects.xml")).Content;
                    var itemsXml = (await _github.FetchFile("Shared/resources/xml/custom/CustomItems.xml")).Content;
                    var nextType = Math.Max(FindNextTypeCode(objectsXml, 0x5000), FindNextTypeCode(itemsXml, 0x5000));

                    if (hasMobs)
                    {
                        var newMobEntries = "";
                        for (int i = 0; i < mobs!.Count; i++)
                        {
                            var rawXml = mobs[i]["xml"]?.ToString();
                            if (string.IsNullOrEmpty(rawXml)) continue;

                            // Extract individual <Object>...</Object> blocks (strip <Objects> wrapper if present)
                            var objectBlocks = Regex.Matches(rawXml, @"<Object\b[^>]*>.*?</Object>", RegexOptions.Singleline);
                            var blocks = objectBlocks.Count > 0
                                ? objectBlocks.Cast<Match>().Select(m => m.Value).ToList()
                                : new List<string> { rawXml };

                            foreach (var block in blocks)
                            {
                                var xml = block;

                                // Skip type codes already in use
                                while (Regex.IsMatch(objectsXml + itemsXml + newMobEntries, $@"type=""0x{nextType:x4}"""))
                                    nextType++;

                                xml = InjectTypeCode(xml, nextType);
                                nextType++;

                                // Ensure <Enemy/> tag is present (required for server to spawn mob)
                                if (!xml.Contains("<Enemy/>") && !xml.Contains("<Enemy />"))
                                    xml = Regex.Replace(xml, @"(<Object[^>]*>)", "$1\n\t<Enemy/>");

                                // Auto-inject <Quest/> for boss mobs (triggers boss health bar in client)
                                var isBoss = mobs[i]["isBoss"]?.Value<bool>() ?? false;
                                if (isBoss && !xml.Contains("<Quest/>") && !xml.Contains("<Quest />"))
                                    xml = Regex.Replace(xml, @"(<Object[^>]*>)", "$1\n\t<Quest/>");

                                // Inject sprite texture reference
                                // Use AnimatedTexture for mobs with both base + attack sprites (sequential indices)
                                if (mobSpriteIndices.TryGetValue(i, out var sprIdx))
                                {
                                    var size = mobs[i]["spriteSize"]?.Value<int>() ?? 8;
                                    var sheetName = size == 16 ? "communitySprites16x16" : "communitySprites8x8";
                                    bool hasAttack = sprIdx.attackIdx >= 0;
                                    xml = InjectSpriteTexture(xml, sheetName, sprIdx.baseIdx, isMob: hasAttack);
                                }

                                newMobEntries += "\t" + xml.Trim() + "\n";
                            }
                        }

                        if (!string.IsNullOrEmpty(newMobEntries))
                        {
                            var updatedObjectsXml = objectsXml.Replace("</Objects>", newMobEntries + "</Objects>");
                            files.Add(("Shared/resources/xml/custom/CustomObjects.xml", updatedObjectsXml));
                        }
                    }

                    // 4c. Write item XMLs to CustomItems.xml (with sprite texture refs)
                    if (hasItems)
                    {
                        var newItemEntries = "";
                        for (int i = 0; i < items!.Count; i++)
                        {
                            var rawXml = items[i]["xml"]?.ToString();
                            if (string.IsNullOrEmpty(rawXml)) continue;

                            // Extract individual <Object> blocks (strip <Objects> wrapper if present)
                            var objectBlocks = Regex.Matches(rawXml, @"<Object\b[^>]*>.*?</Object>", RegexOptions.Singleline);
                            var blocks = objectBlocks.Count > 0
                                ? objectBlocks.Cast<Match>().Select(m => m.Value).ToList()
                                : new List<string> { rawXml };

                            foreach (var block in blocks)
                            {
                                var xml = block;

                                // Check existing type codes to find next available
                                while (Regex.IsMatch(objectsXml + itemsXml + newItemEntries, $@"type=""0x{nextType:x4}"""))
                                    nextType++;

                                xml = InjectTypeCode(xml, nextType);
                                nextType++;

                                // Inject sprite texture reference
                                if (itemSpriteIndices.TryGetValue(i, out var sprIdx))
                                    xml = InjectSpriteTexture(xml, "communitySprites8x8", sprIdx, isMob: false);

                                newItemEntries += "\t" + xml.Trim() + "\n";
                            }
                        }

                        if (!string.IsNullOrEmpty(newItemEntries))
                        {
                            var updatedItemsXml = itemsXml.Replace("</Objects>", newItemEntries + "</Objects>");
                            files.Add(("Shared/resources/xml/custom/CustomItems.xml", updatedItemsXml));
                        }
                    }
                }

                // 5. Add World entry to Dungeons.xml
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

                // 5b. Append to community-dungeons.txt (used by /dungeon command in-game)
                try
                {
                    var (communityList, _) = await _github.FetchFile("Shared/resources/worlds/community-dungeons.txt");
                    if (communityList == null) communityList = "";
                    var trimmed = communityList.TrimEnd();
                    var updated = string.IsNullOrEmpty(trimmed) ? safeTitle : trimmed + "\n" + safeTitle;
                    files.Add(("Shared/resources/worlds/community-dungeons.txt", updated));
                }
                catch
                {
                    // File doesn't exist yet — create it
                    files.Add(("Shared/resources/worlds/community-dungeons.txt", safeTitle));
                }

                // 5c. Build behavior + loot JSON for community mobs (used by JsonBehaviorLoader on server)
                if (hasMobs)
                {
                    var behaviorDict = new JObject();
                    for (int i = 0; i < mobs!.Count; i++)
                    {
                        var mob = mobs[i];
                        var behavior = mob["behavior"] as JObject;
                        if (behavior == null || behavior.Count == 0) continue;

                        // Extract mob name(s) from XML
                        var rawXml = mob["xml"]?.ToString() ?? "";
                        var nameMatches = Regex.Matches(rawXml, @"<Object\b[^>]*\bid=""([^""]+)""");
                        if (nameMatches.Count == 0)
                        {
                            var nameMatch = Regex.Match(rawXml, @"id=""([^""]+)""");
                            if (nameMatch.Success)
                                behaviorDict[nameMatch.Groups[1].Value] = behavior.DeepClone();
                        }
                        else
                        {
                            foreach (Match nm in nameMatches)
                                behaviorDict[nm.Groups[1].Value] = behavior.DeepClone();
                        }
                    }

                    // Inject loot from items' dropFrom/dropRate into mob behavior entries
                    if (hasItems)
                    {
                        foreach (var item in items!)
                        {
                            var dropFrom = item["dropFrom"]?.ToString();
                            if (string.IsNullOrEmpty(dropFrom)) continue;

                            var dropRate = item["dropRate"]?.Value<double>() ?? 0.1;
                            // Extract item name from XML id attribute
                            var itemXml = item["xml"]?.ToString() ?? "";
                            var itemNameMatch = Regex.Match(itemXml, @"id=""([^""]+)""");
                            if (!itemNameMatch.Success) continue;
                            var itemName = itemNameMatch.Groups[1].Value;

                            // Find or create the mob entry in behaviorDict and add loot
                            var mobKey = behaviorDict.Properties()
                                .FirstOrDefault(p => p.Name.Equals(dropFrom, StringComparison.OrdinalIgnoreCase))?.Name;

                            if (mobKey == null)
                            {
                                // Mob has no behavior defined — create a default entry with just Wander
                                mobKey = dropFrom;
                                behaviorDict[mobKey] = new JObject
                                {
                                    ["states"] = new JObject
                                    {
                                        ["idle"] = new JObject
                                        {
                                            ["behaviors"] = new JArray { new JObject { ["type"] = "Wander", ["speed"] = 0.4 } },
                                            ["transitions"] = new JArray()
                                        }
                                    },
                                    ["initialState"] = "idle"
                                };
                            }

                            var mobDef = behaviorDict[mobKey] as JObject;
                            if (mobDef != null)
                            {
                                var lootArr = mobDef["loot"] as JArray;
                                if (lootArr == null)
                                {
                                    lootArr = new JArray();
                                    mobDef["loot"] = lootArr;
                                }
                                lootArr.Add(new JObject
                                {
                                    ["item"] = itemName,
                                    ["probability"] = dropRate
                                });
                            }
                        }
                    }

                    if (behaviorDict.Count > 0)
                    {
                        files.Add(($"Shared/resources/behaviors/community/{safeTitle}.json",
                            behaviorDict.ToString(Newtonsoft.Json.Formatting.Indented)));
                    }
                }

                // 6. Atomic commit to GitHub (text + binary files)
                await _github.CommitFiles(files, $"Add community dungeon: {safeTitle}", binaryFiles);

                // 7. Update status in Supabase
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

        /// <summary>Find the next available type code. Default start: 0xF000 for grounds, 0x5000 for objects</summary>
        private static int FindNextTypeCode(string xml, int defaultStart = 0xF000)
        {
            var matches = Regex.Matches(xml, @"type=""0x([0-9a-fA-F]+)""");
            int max = defaultStart - 1;
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out int val))
                    if (val > max) max = val;
            }
            return max + 1;
        }

        /// <summary>Inject or replace the type="0xNNNN" attribute in an Object XML tag</summary>
        private static string InjectTypeCode(string xml, int typeCode)
        {
            var typeHex = $"0x{typeCode:x4}";
            // If there's already a type attribute, replace it
            if (Regex.IsMatch(xml, @"<Object\s[^>]*type=""[^""]*"""))
                return Regex.Replace(xml, @"(<Object\s[^>]*)type=""[^""]*""", $"$1type=\"{typeHex}\"");
            // Otherwise inject type after <Object
            return Regex.Replace(xml, @"<Object(\s)", $"<Object type=\"{typeHex}\"$1");
        }

        /// <summary>Inject or replace sprite texture reference in an Object XML</summary>
        private static string InjectSpriteTexture(string xml, string sheetName, int index, bool isMob)
        {
            var tag = isMob ? "AnimatedTexture" : "Texture";
            var textureXml = $"<{tag}>\n\t\t<File>{sheetName}</File>\n\t\t<Index>{index}</Index>\n\t</{tag}>";

            // Remove existing top-level AnimatedTexture/Texture (direct children of <Object>)
            xml = Regex.Replace(xml, @"<AnimatedTexture>.*?</AnimatedTexture>", "", RegexOptions.Singleline);
            xml = Regex.Replace(xml, @"<Texture>\s*<File>.*?</Texture>", "", RegexOptions.Singleline);

            // Inject after the opening <Object ...> tag
            // Negative lookahead (?![a-zA-Z]) prevents matching <ObjectId>, <Objects>, etc.
            xml = Regex.Replace(xml, @"(<Object(?![a-zA-Z])(?:\s[^>]*)?>)", $"$1\n\t{textureXml}");
            return xml;
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
