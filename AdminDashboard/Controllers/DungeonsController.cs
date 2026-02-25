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
using System.Xml.Linq;

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
                    mob_count = (d["mobs"] ?? d["bosses"]) is JArray mobs ? mobs.Count : 0,
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

                // 1b. Inject mob placements + spawn region into JM if missing
                InjectMobsAndSpawn(mapJm, dungeon);

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

                // Track item renames so loot injection in behavior JSON uses the correct name
                var itemRenames = new Dictionary<string, string>(); // oldName -> newName

                // mobSpriteIndices needs to be accessible in behavior injection below
                var mobSpriteIndices = new Dictionary<int, List<int>>();

                if (hasMobs || hasItems)
                {
                    // 4a. Build sprite sheets and assign indices
                    var spriteService = new SpriteSheetService(_github);

                    // Collect sprites grouped by size: (mobIndex, frame, dataUrl, spriteSize)
                    // frame = "mob_0", "mob_1", ... for mob sprites; "proj_*" for projectiles; "item" for items
                    var spriteEntries = new List<(int entityIdx, string frame, string dataUrl, int size, bool isMob)>();

                    if (hasMobs)
                    {
                        for (int i = 0; i < mobs!.Count; i++)
                        {
                            var mob = mobs[i];
                            var size = mob["spriteSize"]?.Value<int>() ?? 8;

                            // New: read sprites array (N named sprites)
                            var spritesArr = mob["sprites"] as JArray;
                            if (spritesArr != null && spritesArr.Count > 0)
                            {
                                for (int j = 0; j < spritesArr.Count; j++)
                                {
                                    var spr = spritesArr[j] as JObject;
                                    var dataUrl = spr?["data"]?.ToString();
                                    if (!string.IsNullOrEmpty(dataUrl))
                                        spriteEntries.Add((i, $"mob_{j}", dataUrl, size, true));
                                }
                            }
                            else
                            {
                                // Legacy: read spriteBase/spriteAttack
                                var baseUrl = (mob["spriteBase"] ?? mob["sprite"])?.ToString();
                                var attackUrl = mob["spriteAttack"]?.ToString();
                                if (!string.IsNullOrEmpty(baseUrl))
                                    spriteEntries.Add((i, "mob_0", baseUrl, size, true));
                                if (!string.IsNullOrEmpty(attackUrl))
                                    spriteEntries.Add((i, "mob_1", attackUrl, size, true));
                            }

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

                    // Per-dungeon indices (start at 0, memory-only on client via CUSTOM_DUNGEON_ASSETS)
                    // mobSpriteIndices[mobIdx] = [spriteIdx0, spriteIdx1, ...] (index 0=base, 1+=AltTextures)
                    var pdMobSpriteIndices = new Dictionary<int, List<int>>();
                    var pdItemSpriteIndices = new Dictionary<int, int>();
                    var pdProjSpriteIndices = new Dictionary<int, Dictionary<string, int>>();
                    // Per-dungeon sheet PNGs: sheetName → base64 PNG
                    var perDungeonSheets = new Dictionary<string, (string base64Png, int tileW, int tileH)>();

                    foreach (var sizeGroup in spriteEntries.GroupBy(e => e.size))
                    {
                        var spriteSize = sizeGroup.Key;
                        var entries = sizeGroup.ToList();
                        var pdSheetName = $"dungeon_{request.DungeonId}_{spriteSize}x{spriteSize}";

                        // Decode all sprites in this size group
                        var bitmaps = new List<SKBitmap>();
                        try
                        {
                            foreach (var entry in entries)
                                bitmaps.Add(SpriteSheetService.DecodeDataUrl(entry.dataUrl));

                            // Create per-dungeon sheet (fresh, indices start at 0) — memory-only on client
                            var pdMeta = new SheetMetadata { NextIndex = 0 };
                            var pdSheetWidth = 16 * spriteSize; // 16 columns
                            var pdSheet = new SKBitmap(pdSheetWidth, spriteSize, SKColorType.Rgba8888, SKAlphaType.Premul);
                            pdSheet.Erase(SKColors.Transparent);
                            var (pdUpdatedSheet, pdIndices) = spriteService.AddSprites(pdSheet, pdMeta, bitmaps, spriteSize);
                            pdSheet = pdUpdatedSheet;

                            // Map indices back to entities (per-dungeon only)
                            for (int i = 0; i < entries.Count; i++)
                            {
                                var e = entries[i];
                                if (e.isMob)
                                {
                                    if (e.frame.StartsWith("proj_"))
                                    {
                                        var projId = e.frame.Substring(5);
                                        if (!pdProjSpriteIndices.ContainsKey(e.entityIdx))
                                            pdProjSpriteIndices[e.entityIdx] = new Dictionary<string, int>();
                                        pdProjSpriteIndices[e.entityIdx][projId] = pdIndices[i];
                                    }
                                    else if (e.frame.StartsWith("mob_"))
                                    {
                                        // Parse sprite slot index from "mob_0", "mob_1", etc.
                                        var slotIdx = int.Parse(e.frame.Substring(4));

                                        if (!mobSpriteIndices.ContainsKey(e.entityIdx))
                                            mobSpriteIndices[e.entityIdx] = new List<int>();
                                        while (mobSpriteIndices[e.entityIdx].Count <= slotIdx)
                                            mobSpriteIndices[e.entityIdx].Add(-1);
                                        mobSpriteIndices[e.entityIdx][slotIdx] = pdIndices[i];

                                        if (!pdMobSpriteIndices.ContainsKey(e.entityIdx))
                                            pdMobSpriteIndices[e.entityIdx] = new List<int>();
                                        while (pdMobSpriteIndices[e.entityIdx].Count <= slotIdx)
                                            pdMobSpriteIndices[e.entityIdx].Add(-1);
                                        pdMobSpriteIndices[e.entityIdx][slotIdx] = pdIndices[i];
                                    }
                                }
                                else
                                {
                                    pdItemSpriteIndices[e.entityIdx] = pdIndices[i];
                                }
                            }

                            // Store per-dungeon sheet as base64 for DungeonAssets XML
                            var pdPngBytes = SpriteSheetService.EncodePng(pdSheet);
                            perDungeonSheets[pdSheetName] = (Convert.ToBase64String(pdPngBytes), spriteSize, spriteSize);
                            pdSheet.Dispose();
                        }
                        finally
                        {
                            foreach (var bmp in bitmaps) bmp.Dispose();
                        }
                    }

                    // 4b. Generate custom projectile definitions + rewrite mob ObjectIds
                    if (pdProjSpriteIndices.Count > 0)
                    {
                        var (projXml, _) = await _github.FetchFile("Shared/resources/xml/custom/CustomProj.xml");
                        var projNextType = FindNextTypeCode(projXml, 0x4970);
                        var newProjEntries = "";

                        for (int i = 0; i < mobs!.Count; i++)
                        {
                            if (!pdProjSpriteIndices.TryGetValue(i, out var projMap)) continue;

                            var rawXml = mobs[i]["xml"]?.ToString() ?? "";
                            var mobName = Regex.Match(rawXml, @"id=""([^""]+)""").Groups[1].Value;

                            foreach (var kvp in projMap)
                            {
                                var projId = kvp.Key;
                                var projName = $"{mobName} Proj {projId}";

                                // Placeholder texture in CustomProj.xml — per-dungeon sheet overrides at runtime
                                newProjEntries += $"\t<Object type=\"0x{projNextType:x4}\" id=\"{EscapeXml(projName)}\">\n";
                                newProjEntries += $"\t\t<Class>Projectile</Class>\n";
                                newProjEntries += $"\t\t<Texture>\n";
                                newProjEntries += $"\t\t\t<File>lofiObj5</File>\n";
                                newProjEntries += $"\t\t\t<Index>0</Index>\n";
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

                    // Load prod reserved names to prevent name collisions (prod names overwrite custom in IdToObjectType)
                    var reservedNames = new HashSet<string>();
                    try
                    {
                        var (reservedContent, _) = await _github.FetchFile("Shared/resources/reserved_names.txt");
                        foreach (var line in reservedContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            reservedNames.Add(line.Trim());
                    }
                    catch { /* reserved_names.txt missing — skip check */ }

                    // Collect processed XML blocks for DungeonAssets (step 4d needs type codes)
                    var processedMobBlocks = new List<(int mobIdx, string xml)>();
                    var processedItemBlocks = new List<(int itemIdx, string xml)>();

                    if (hasMobs)
                    {
                        // Collect existing mob names to avoid duplicates
                        var existingMobNames = new HashSet<string>();
                        foreach (Match m in Regex.Matches(objectsXml, @"id=""([^""]+)"""))
                            existingMobNames.Add(m.Groups[1].Value);

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

                                // Skip mobs with duplicate names (already exist in CustomObjects.xml)
                                var nameMatch = Regex.Match(xml, @"id=""([^""]+)""");
                                if (nameMatch.Success && existingMobNames.Contains(nameMatch.Groups[1].Value))
                                    continue;
                                // Auto-rename mobs that collide with prod names
                                if (nameMatch.Success && reservedNames.Contains(nameMatch.Groups[1].Value))
                                {
                                    var newName = $"{safeTitle} {nameMatch.Groups[1].Value}";
                                    xml = xml.Replace($"id=\"{nameMatch.Groups[1].Value}\"", $"id=\"{newName}\"");
                                    nameMatch = Regex.Match(xml, @"id=""([^""]+)""");
                                }
                                if (nameMatch.Success)
                                    existingMobNames.Add(nameMatch.Groups[1].Value);

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

                                // Inject placeholder texture in CustomObjects.xml (per-dungeon sheet overrides at runtime)
                                if (mobSpriteIndices.TryGetValue(i, out var sprIndices))
                                {
                                    xml = InjectSpriteTexture(xml, "chars8x8rEncounters", new List<int> { 0 }, isMob: true);
                                }

                                // Save processed block (with type code + texture) for DungeonAssets
                                processedMobBlocks.Add((i, xml.Trim()));

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
                        // Collect existing item names to avoid duplicates
                        var existingItemNames = new HashSet<string>();
                        foreach (Match m in Regex.Matches(itemsXml, @"id=""([^""]+)"""))
                            existingItemNames.Add(m.Groups[1].Value);

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

                                // Skip items with duplicate names (already exist in CustomItems.xml)
                                var nameMatch = Regex.Match(xml, @"id=""([^""]+)""");
                                if (nameMatch.Success && existingItemNames.Contains(nameMatch.Groups[1].Value))
                                    continue;
                                // Auto-rename items that collide with prod names
                                if (nameMatch.Success && reservedNames.Contains(nameMatch.Groups[1].Value))
                                {
                                    var oldName = nameMatch.Groups[1].Value;
                                    var newName = $"{safeTitle} {oldName}";
                                    xml = xml.Replace($"id=\"{oldName}\"", $"id=\"{newName}\"");
                                    itemRenames[oldName] = newName;
                                    nameMatch = Regex.Match(xml, @"id=""([^""]+)""");
                                }
                                if (nameMatch.Success)
                                    existingItemNames.Add(nameMatch.Groups[1].Value);

                                // Check existing type codes to find next available
                                while (Regex.IsMatch(objectsXml + itemsXml + newItemEntries, $@"type=""0x{nextType:x4}"""))
                                    nextType++;

                                xml = InjectTypeCode(xml, nextType);
                                nextType++;

                                // Inject placeholder texture in CustomItems.xml (per-dungeon sheet overrides at runtime)
                                if (pdItemSpriteIndices.TryGetValue(i, out var pdSprIdx2))
                                    xml = InjectSpriteTexture(xml, "lofiObj5", 0, isMob: false);

                                // Save processed block (with type code + texture) for DungeonAssets
                                processedItemBlocks.Add((i, xml.Trim()));

                                newItemEntries += "\t" + xml.Trim() + "\n";
                            }
                        }

                        if (!string.IsNullOrEmpty(newItemEntries))
                        {
                            var updatedItemsXml = itemsXml.Replace("</Objects>", newItemEntries + "</Objects>");
                            files.Add(("Shared/resources/xml/custom/CustomItems.xml", updatedItemsXml));
                        }
                    }

                    // 4d. Build per-dungeon DungeonAssets XML (sent via CUSTOM_DUNGEON_ASSETS packet)
                    // Uses processedMobBlocks/processedItemBlocks which already have correct type codes
                    if (perDungeonSheets.Count > 0)
                    {
                        var assetsXml = "<DungeonAssets>\n<SpriteSheets>\n";
                        foreach (var kvp in perDungeonSheets)
                            assetsXml += $"<Sheet name=\"{kvp.Key}\" tileW=\"{kvp.Value.tileW}\" tileH=\"{kvp.Value.tileH}\">{kvp.Value.base64Png}</Sheet>\n";
                        assetsXml += "</SpriteSheets>\n<Objects>\n";

                        // Emit mob entries: swap shared sheet texture → per-dungeon sheet texture
                        // Use isMob: false (Texture, not AnimatedTexture) because per-dungeon sheets
                        // are registered in AssetLibrary on the client, not AnimatedChars
                        foreach (var (mobIdx, processedXml) in processedMobBlocks)
                        {
                            var xml = processedXml;
                            if (pdMobSpriteIndices.TryGetValue(mobIdx, out var pdSprIndices))
                            {
                                var size = mobs![mobIdx]["spriteSize"]?.Value<int>() ?? 8;
                                var pdSheetName = $"dungeon_{request.DungeonId}_{size}x{size}";
                                xml = InjectSpriteTexture(xml, pdSheetName, pdSprIndices, isMob: false);
                            }
                            assetsXml += xml + "\n";
                        }

                        // Emit projectile entries with per-dungeon sheet refs
                        // Use type codes from CustomProj.xml entries written in step 4b
                        if (pdProjSpriteIndices.Count > 0)
                        {
                            // Fetch the proj XML we just built to get the real type codes
                            var projFileEntry = files.FirstOrDefault(f => f.Path.Contains("CustomProj.xml"));
                            var projXmlContent = projFileEntry.Content ?? "";

                            var pdProjSheetName = $"dungeon_{request.DungeonId}_8x8"; // projectiles always 8x8
                            for (int i = 0; i < mobs!.Count; i++)
                            {
                                if (!pdProjSpriteIndices.TryGetValue(i, out var pdProjMap)) continue;
                                var rawXml = mobs[i]["xml"]?.ToString() ?? "";
                                var mobName = Regex.Match(rawXml, @"id=""([^""]+)""").Groups[1].Value;

                                foreach (var kvp in pdProjMap)
                                {
                                    var projName = $"{mobName} Proj {kvp.Key}";
                                    // Find the real type code from CustomProj.xml
                                    var typeMatch = Regex.Match(projXmlContent,
                                        $@"type=""(0x[0-9a-fA-F]+)""\s+id=""{Regex.Escape(EscapeXml(projName))}""");
                                    var typeCode = typeMatch.Success ? typeMatch.Groups[1].Value : "0x0000";

                                    assetsXml += $"<Object type=\"{typeCode}\" id=\"{EscapeXml(projName)}\">\n";
                                    assetsXml += $"\t<Class>Projectile</Class>\n";
                                    assetsXml += $"\t<Texture>\n\t\t<File>{pdProjSheetName}</File>\n\t\t<Index>{kvp.Value}</Index>\n\t</Texture>\n";
                                    assetsXml += $"\t<AngleCorrection>1</AngleCorrection>\n";
                                    assetsXml += $"</Object>\n";
                                }
                            }
                        }

                        // Emit item entries: swap shared sheet texture → per-dungeon sheet texture
                        foreach (var (itemIdx, processedXml) in processedItemBlocks)
                        {
                            var xml = processedXml;
                            if (pdItemSpriteIndices.TryGetValue(itemIdx, out var pdItemIdx))
                            {
                                var pdItemSheetName = $"dungeon_{request.DungeonId}_8x8"; // items always 8x8
                                xml = InjectSpriteTexture(xml, pdItemSheetName, pdItemIdx, isMob: false);
                            }
                            assetsXml += xml + "\n";
                        }

                        assetsXml += "</Objects>\n</DungeonAssets>";
                        files.Add(($"Shared/resources/xml/dungeon_assets/{safeTitle}.xml", assetsXml));
                    }
                }

                // 5. Add World entry to Dungeons.xml
                var (dungeonsXml, _) = await _github.FetchFile("Shared/resources/xml/Dungeons.xml");

                // Check for duplicate
                if (dungeonsXml.Contains($"id=\"{EscapeXml(safeTitle)}\""))
                    return BadRequest(new { error = $"A dungeon named '{safeTitle}' already exists in Dungeons.xml" });

                var width = mapJm["width"]?.Value<int>() ?? 256;
                var height = mapJm["height"]?.Value<int>() ?? 256;

                // Build starting equipment element from Supabase data
                var startingEquipStr = "";
                var startEquipArr = dungeon["starting_equipment"] as JArray;
                if (startEquipArr != null && startEquipArr.Count > 0)
                {
                    var equipNames = string.Join(",", startEquipArr.Select(e => e.ToString()));
                    startingEquipStr = $"\t\t<StartingEquipment>{EscapeXml(equipNames)}</StartingEquipment>\n";
                }

                var worldEntry = $"\t<World id=\"{EscapeXml(safeTitle)}\">\n" +
                    $"\t\t<Width>{width}</Width>\n" +
                    $"\t\t<Height>{height}</Height>\n" +
                    $"\t\t<MapJM>Dungeons/{safeTitle}.jm</MapJM>\n" +
                    $"\t\t<VisibilityType>1</VisibilityType>\n" +
                    $"\t\t<Dungeon/>\n" +
                    $"\t\t<CommunityDungeon/>\n" +
                    startingEquipStr +
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

                    // Auto-inject SetAltTexture for mobs with multiple sprites
                    // Only auto-inject for simple 2-sprite mobs (base+attack) with no manual SetAltTexture.
                    // Mobs with 3+ sprites must define their own SetAltTexture in behavior states.
                    for (int i = 0; i < mobs!.Count; i++)
                    {
                        if (!mobSpriteIndices.TryGetValue(i, out var sprIndices) || sprIndices.Count < 2) continue;

                        var rawXml = mobs[i]["xml"]?.ToString() ?? "";
                        var nameMatch = Regex.Match(rawXml, @"id=""([^""]+)""");
                        if (!nameMatch.Success) continue;
                        var mobName = nameMatch.Groups[1].Value;

                        var mobDef = behaviorDict[mobName] as JObject;
                        if (mobDef == null) continue;

                        var states = mobDef["states"] as JObject;
                        if (states == null) continue;

                        // Check if ANY state already has a manual SetAltTexture
                        bool hasManualAlt = states.Properties().Any(p =>
                        {
                            var behaviorsArr = (p.Value as JObject)?["behaviors"] as JArray;
                            return behaviorsArr?.Any(b => b["type"]?.ToString() == "SetAltTexture") ?? false;
                        });

                        // Skip auto-injection if user has manual SetAltTexture or 3+ sprites
                        if (hasManualAlt || sprIndices.Count > 2) continue;

                        // Simple 2-sprite mob: auto-inject shoot state cycling + idle reset
                        foreach (var prop in states.Properties())
                        {
                            var stateData = prop.Value as JObject;
                            var behaviorsArr = stateData?["behaviors"] as JArray;
                            if (behaviorsArr == null) continue;

                            bool hasShoot = behaviorsArr.Any(b => b["type"]?.ToString() == "Shoot");
                            if (hasShoot)
                            {
                                behaviorsArr.Add(new JObject
                                {
                                    ["type"] = "SetAltTexture",
                                    ["minValue"] = 0,
                                    ["maxValue"] = 1,
                                    ["coolDown"] = 500,
                                    ["loop"] = true
                                });
                            }
                            else
                            {
                                behaviorsArr.Add(new JObject
                                {
                                    ["type"] = "SetAltTexture",
                                    ["minValue"] = 0
                                });
                            }
                        }
                    }

                    // Inject loot from items' dropFromMobs/dropRate into mob behavior entries
                    if (hasItems)
                    {
                        // Collect all mob names for "__all__" expansion
                        var allMobNames = new List<string>();
                        for (int i = 0; i < mobs!.Count; i++)
                        {
                            var rawXml = mobs[i]["xml"]?.ToString() ?? "";
                            var nameMatch = Regex.Match(rawXml, @"id=""([^""]+)""");
                            if (nameMatch.Success) allMobNames.Add(nameMatch.Groups[1].Value);
                        }

                        foreach (var item in items!)
                        {
                            var dropRate = item["dropRate"]?.Value<double>() ?? 0.1;
                            // Extract item name from XML id attribute
                            var itemXml = item["xml"]?.ToString() ?? "";
                            var itemNameMatch = Regex.Match(itemXml, @"id=""([^""]+)""");
                            if (!itemNameMatch.Success) continue;
                            var itemName = itemNameMatch.Groups[1].Value;
                            // Use renamed name if item was auto-renamed due to prod collision
                            if (itemRenames.TryGetValue(itemName, out var renamedName))
                                itemName = renamedName;

                            // Resolve target mob names: dropFromMobs (new) or dropFrom (legacy)
                            var targetMobs = new List<string>();
                            var dropFromMobs = item["dropFromMobs"] as JArray;
                            if (dropFromMobs != null && dropFromMobs.Count > 0)
                            {
                                if (dropFromMobs.Any(t => t.ToString() == "__all__"))
                                    targetMobs.AddRange(allMobNames);
                                else
                                    targetMobs.AddRange(dropFromMobs.Select(t => t.ToString()));
                            }
                            else
                            {
                                // Legacy: single dropFrom field
                                var dropFrom = item["dropFrom"]?.ToString();
                                if (!string.IsNullOrEmpty(dropFrom))
                                    targetMobs.Add(dropFrom);
                            }

                            if (targetMobs.Count == 0) continue;

                            var lootEntry = new JObject
                            {
                                ["item"] = itemName,
                                ["probability"] = dropRate
                            };

                            foreach (var mobName in targetMobs)
                            {
                                // Find or create the mob entry in behaviorDict and add loot
                                var mobKey = behaviorDict.Properties()
                                    .FirstOrDefault(p => p.Name.Equals(mobName, StringComparison.OrdinalIgnoreCase))?.Name;

                                if (mobKey == null)
                                {
                                    // Mob has no behavior defined — create a default entry with just Wander
                                    mobKey = mobName;
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
                                    lootArr.Add(lootEntry.DeepClone());
                                }
                            }
                        }
                    }

                    if (behaviorDict.Count > 0)
                    {
                        files.Add(($"Shared/resources/behaviors/community/{safeTitle}.json",
                            behaviorDict.ToString(Newtonsoft.Json.Formatting.Indented)));
                    }
                }

                // 6. Validate all XML files before committing (prevents malformed XML like dungeon-6 glitch)
                foreach (var (path, content) in files)
                {
                    if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        // Strip BOM and leading whitespace that GitHub API may return
                        var cleanContent = content.TrimStart('\uFEFF', '\u200B', '\0').TrimStart();
                        XDocument.Parse(cleanContent);
                    }
                    catch (System.Xml.XmlException xmlEx)
                    {
                        Console.WriteLine($"[DungeonsController] MALFORMED XML in {path}: {xmlEx.Message}");
                        return StatusCode(500, new { error = $"Malformed XML in {Path.GetFileName(path)}: {xmlEx.Message}" });
                    }
                }

                // 7. Atomic commit to GitHub (text + binary files)
                await _github.CommitFiles(files, $"Add community dungeon: {safeTitle}", binaryFiles);

                // 8. Update status in Supabase
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

                await _supabase.DeleteDungeon(request.DungeonId);
                return Ok(new { success = true, message = "Dungeon rejected and deleted" });
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

        /// <summary>Inject sprite texture with N AltTexture blocks (index 0 = base, 1+ = AltTexture IDs)</summary>
        private static string InjectSpriteTexture(string xml, string sheetName, List<int> spriteIndices, bool isMob)
        {
            if (spriteIndices == null || spriteIndices.Count == 0) return xml;

            var tag = isMob ? "AnimatedTexture" : "Texture";

            // Remove existing texture and AltTexture blocks
            xml = Regex.Replace(xml, @"<AnimatedTexture>.*?</AnimatedTexture>", "", RegexOptions.Singleline);
            xml = Regex.Replace(xml, @"<Texture>\s*<File>.*?</Texture>", "", RegexOptions.Singleline);
            xml = Regex.Replace(xml, @"<AltTexture[^>]*>.*?</AltTexture>", "", RegexOptions.Singleline);
            // Safety: clean up any Texture blocks inside <ObjectId> tags
            xml = Regex.Replace(xml, @"(<ObjectId>)\s*<(?:Animated)?Texture>.*?</(?:Animated)?Texture>\s*", "$1", RegexOptions.Singleline);

            // Base texture (index 0)
            var textureXml = $"<{tag}>\n\t\t<File>{sheetName}</File>\n\t\t<Index>{spriteIndices[0]}</Index>\n\t</{tag}>";

            // AltTexture blocks for indices 1..N
            var altBlocks = "";
            for (int i = 1; i < spriteIndices.Count; i++)
            {
                if (spriteIndices[i] < 0) continue;
                altBlocks += $"\n\t<AltTexture id=\"{i}\">\n\t\t<{tag}>\n\t\t\t<File>{sheetName}</File>\n\t\t\t<Index>{spriteIndices[i]}</Index>\n\t\t</{tag}>\n\t</AltTexture>";
            }

            // Inject after the opening <Object ...> tag
            xml = Regex.Replace(xml, @"(<Object(?![a-zA-Z])(?:\s[^>]*)?>)", $"$1\n\t{textureXml}{altBlocks}");
            return xml;
        }

        /// <summary>Backward compat overload for single base + optional attack index</summary>
        private static string InjectSpriteTexture(string xml, string sheetName, int index, bool isMob, int attackIndex = -1)
        {
            var indices = new List<int> { index };
            if (attackIndex >= 0) indices.Add(attackIndex);
            return InjectSpriteTexture(xml, sheetName, indices, isMob);
        }

        private static string EscapeXml(string s) => s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

        /// <summary>
        /// Inject mob placements and spawn region into JM dict if the editor didn't place them.
        /// The game server reads mobs from JM objs entries — without this, mobs won't spawn.
        /// </summary>
        private static void InjectMobsAndSpawn(JToken mapJm, JObject dungeon)
        {
            var dict = mapJm["dict"] as JArray;
            var dataB64 = mapJm["data"]?.ToString();
            var width = mapJm["width"]?.Value<int>() ?? 0;
            var height = mapJm["height"]?.Value<int>() ?? 0;
            if (dict == null || width == 0 || height == 0 || string.IsNullOrEmpty(dataB64)) return;

            var mobs = (dungeon["mobs"] ?? dungeon["bosses"]) as JArray;
            if (mobs == null || mobs.Count == 0) return;

            // Build set of valid mob names from submitted XML
            var validMobNames = new HashSet<string>();
            var mobNamesList = new List<(string name, bool isBoss)>();
            foreach (var mob in mobs)
            {
                var xml = mob["xml"]?.ToString() ?? "";
                var nameMatch = Regex.Match(xml, @"id=""([^""]+)""");
                if (!nameMatch.Success) continue;
                var name = nameMatch.Groups[1].Value;
                validMobNames.Add(name);
                var isBoss = mob["isBoss"]?.Value<bool>() ?? xml.Contains("<Quest/>");
                mobNamesList.Add((name, isBoss));
            }

            // Check if JM already has mob placements
            bool hasExistingMobs = false;
            foreach (var entry in dict)
            {
                if (entry["objs"] is JArray objs && objs.Count > 0)
                {
                    hasExistingMobs = true;
                    break;
                }
            }

            if (hasExistingMobs)
            {
                // Fix invalid mob names (e.g. "Unknown") in existing placements
                foreach (var entry in dict)
                {
                    if (entry["objs"] is JArray objs && objs.Count > 0)
                    {
                        var id = objs[0]?["id"]?.ToString();
                        if (!string.IsNullOrEmpty(id) && !validMobNames.Contains(id) && mobNamesList.Count > 0)
                        {
                            // Replace with first valid mob name
                            objs[0]["id"] = mobNamesList[0].name;
                        }
                    }
                }
                return;
            }

            // Decode grid to find non-empty ground tiles
            byte[] inflated;
            try
            {
                var compressed = Convert.FromBase64String(dataB64);
                using var input = new System.IO.MemoryStream(compressed);
                using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
                using var output = new System.IO.MemoryStream();
                zlib.CopyTo(output);
                inflated = output.ToArray();
            }
            catch { return; }

            var totalTiles = width * height;
            var groundTiles = new List<(int x, int y, int dictIdx)>();
            for (int i = 0; i < totalTiles && i * 2 + 1 < inflated.Length; i++)
            {
                int idx = (inflated[i * 2] << 8) | inflated[i * 2 + 1];
                if (idx < 0 || idx >= dict.Count) continue;
                var ground = dict[idx]?["ground"]?.ToString();
                if (!string.IsNullOrEmpty(ground) && ground != "Empty")
                    groundTiles.Add((i % width, i / width, idx));
            }

            if (groundTiles.Count == 0) return;

            // Reuse mob names already extracted above
            if (mobNamesList.Count == 0) return;

            var rng = new Random(42); // deterministic for reproducibility

            // We need new dict entries for tiles with mobs (ground + objs together)
            // Strategy: pick random ground tiles, create new dict entries with same ground + objs
            var gridBytes = new short[totalTiles];
            for (int i = 0; i < totalTiles && i * 2 + 1 < inflated.Length; i++)
                gridBytes[i] = (short)((inflated[i * 2] << 8) | inflated[i * 2 + 1]);

            // Shuffle ground tiles for random placement
            var shuffled = groundTiles.OrderBy(_ => rng.Next()).ToList();
            int placeIdx = 0;

            // Place each mob: boss once, minions 2-4 times
            foreach (var (mobName, isBoss) in mobNamesList)
            {
                int count = isBoss ? 1 : Math.Min(2 + rng.Next(3), shuffled.Count / Math.Max(mobNamesList.Count, 1));
                count = Math.Max(1, count);

                for (int c = 0; c < count && placeIdx < shuffled.Count; c++)
                {
                    var (tx, ty, origDictIdx) = shuffled[placeIdx++];
                    var origEntry = dict[origDictIdx] as JObject;
                    if (origEntry == null) continue;

                    // Create new dict entry: same ground (+ groundPixels if present) + objs
                    var newEntry = new JObject();
                    if (origEntry["ground"] != null)
                        newEntry["ground"] = origEntry["ground"].DeepClone();
                    if (origEntry["groundPixels"] != null)
                        newEntry["groundPixels"] = origEntry["groundPixels"].DeepClone();
                    newEntry["objs"] = new JArray(new JObject { ["id"] = mobName });

                    // Add new dict entry and update grid
                    int newDictIdx = dict.Count;
                    dict.Add(newEntry);
                    int tileOff = ty * width + tx;
                    gridBytes[tileOff] = (short)newDictIdx;
                }
            }

            // Inject spawn region at first ground tile (player start)
            bool hasSpawn = false;
            foreach (var entry in dict)
            {
                if (entry["regions"] is JArray regions)
                    foreach (var r in regions)
                        if (r["id"]?.ToString() == "Spawn") { hasSpawn = true; break; }
                if (hasSpawn) break;
            }

            if (!hasSpawn && groundTiles.Count > 0)
            {
                // Place spawn at first ground tile (top-left area)
                var (sx, sy, spawnOrigIdx) = groundTiles[0];
                var spawnOrig = dict[spawnOrigIdx] as JObject;
                var spawnEntry = new JObject();
                if (spawnOrig?["ground"] != null)
                    spawnEntry["ground"] = spawnOrig["ground"].DeepClone();
                if (spawnOrig?["groundPixels"] != null)
                    spawnEntry["groundPixels"] = spawnOrig["groundPixels"].DeepClone();
                spawnEntry["regions"] = new JArray(new JObject { ["id"] = "Spawn" });

                int spawnDictIdx = dict.Count;
                dict.Add(spawnEntry);
                gridBytes[sy * width + sx] = (short)spawnDictIdx;
            }

            // Re-encode grid → zlib → base64 and update mapJm
            var rawBytes = new byte[totalTiles * 2];
            for (int i = 0; i < totalTiles; i++)
            {
                rawBytes[i * 2] = (byte)(gridBytes[i] >> 8);
                rawBytes[i * 2 + 1] = (byte)(gridBytes[i] & 0xFF);
            }

            using var compressOut = new System.IO.MemoryStream();
            using (var zlibOut = new System.IO.Compression.ZLibStream(compressOut, System.IO.Compression.CompressionLevel.Optimal))
            {
                zlibOut.Write(rawBytes, 0, rawBytes.Length);
            }
            mapJm["data"] = Convert.ToBase64String(compressOut.ToArray());
        }
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
