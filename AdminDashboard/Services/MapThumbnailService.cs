using Newtonsoft.Json.Linq;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AdminDashboard.Services
{
    /// <summary>
    /// Generates top-down color thumbnails of a JM map for admin preview.
    /// Produces separate ground-only and objects-only PNG thumbnails.
    /// </summary>
    public class MapThumbnailService
    {
        private readonly Dictionary<string, uint> _groundColors = new();
        private readonly Dictionary<string, uint> _objectColors = new();
        private bool _loaded;

        public void LoadColors(string jsonPath)
        {
            if (_loaded) return;
            try
            {
                var json = File.ReadAllText(jsonPath);
                var root = JObject.Parse(json);

                if (root["ground"] is JObject grounds)
                {
                    foreach (var prop in grounds.Properties())
                    {
                        var hex = prop.Value.ToString().TrimStart('#');
                        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint color))
                            _groundColors[prop.Name] = 0xFF000000 | color;
                    }
                }

                if (root["objects"] is JObject objects)
                {
                    foreach (var prop in objects.Properties())
                    {
                        var hex = prop.Value.ToString().TrimStart('#');
                        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint color))
                            _objectColors[prop.Name] = 0xFF000000 | color;
                    }
                }

                _loaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapThumbnail] Failed to load colors: {ex.Message}");
            }
        }

        public class ThumbnailResult
        {
            public string GroundPng { get; set; }
            public string ObjectsPng { get; set; }
        }

        /// <summary>
        /// Generate separate ground and objects PNG thumbnails from JM map data.
        /// Returns base64-encoded PNG strings, or null on failure.
        /// </summary>
        public ThumbnailResult GenerateThumbnails(JToken mapJm, JObject customTiles)
        {
            try
            {
                var width = mapJm["width"]?.Value<int>() ?? 0;
                var height = mapJm["height"]?.Value<int>() ?? 0;
                var dict = mapJm["dict"] as JArray;
                var dataB64 = mapJm["data"]?.ToString();

                if (width <= 0 || height <= 0 || dict == null || string.IsNullOrEmpty(dataB64))
                    return null;

                // Decode: base64 → zlib inflate → big-endian int16 grid
                var compressed = Convert.FromBase64String(dataB64);
                byte[] inflated;
                using (var input = new MemoryStream(compressed))
                using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    zlib.CopyTo(output);
                    inflated = output.ToArray();
                }

                var totalTiles = width * height;
                var grid = new int[totalTiles];
                for (int i = 0; i < totalTiles; i++)
                {
                    var hi = inflated[i * 2];
                    var lo = inflated[i * 2 + 1];
                    grid[i] = (hi << 8) | lo;
                }

                // Build custom tile reverse map: customId → hex color
                var customColorMap = new Dictionary<string, uint>();
                if (customTiles != null)
                {
                    foreach (var prop in customTiles.Properties())
                    {
                        var hex = prop.Name.TrimStart('#');
                        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint color))
                            customColorMap[prop.Value.ToString()] = 0xFF000000 | color;
                    }
                }

                const int TILE_PX = 8;
                var bmpW = width * TILE_PX;
                var bmpH = height * TILE_PX;
                var bgColor = new SKColor(0xFF111111);

                // Pre-decode groundPixels for each dict entry
                var dictGroundPixels = new byte[dict.Count][];
                for (int d = 0; d < dict.Count; d++)
                {
                    var gpB64 = dict[d]?["groundPixels"]?.ToString();
                    if (!string.IsNullOrEmpty(gpB64))
                    {
                        try { dictGroundPixels[d] = Convert.FromBase64String(gpB64); }
                        catch { dictGroundPixels[d] = null; }
                    }
                }

                // Pre-decode objectPixels for each dict entry
                var dictObjPixels = new byte[dict.Count][];
                var dictObjSize = new int[dict.Count];
                for (int d = 0; d < dict.Count; d++)
                {
                    var objs = dict[d]?["objs"] as JArray;
                    if (objs != null && objs.Count > 0)
                    {
                        var opB64 = objs[0]?["objectPixels"]?.ToString();
                        var objSize = objs[0]?["objectSize"]?.Value<int>() ?? 8;
                        dictObjSize[d] = objSize;
                        if (!string.IsNullOrEmpty(opB64))
                        {
                            try { dictObjPixels[d] = Convert.FromBase64String(opB64); }
                            catch { dictObjPixels[d] = null; }
                        }
                    }
                }

                // ===== Ground thumbnail =====
                string groundPng;
                using (var groundBmp = new SKBitmap(bmpW, bmpH))
                {
                    void FillGround(int tx, int ty, SKColor color)
                    {
                        for (int py = 0; py < TILE_PX; py++)
                            for (int px = 0; px < TILE_PX; px++)
                                groundBmp.SetPixel(tx * TILE_PX + px, ty * TILE_PX + py, color);
                    }

                    for (int ty = 0; ty < height; ty++)
                    {
                        for (int tx = 0; tx < width; tx++)
                        {
                            var idx = grid[ty * width + tx];
                            if (idx < 0 || idx >= dict.Count) { FillGround(tx, ty, bgColor); continue; }

                            var entry = dict[idx];
                            var groundId = entry?["ground"]?.ToString();

                            if (string.IsNullOrEmpty(groundId) || groundId == "Empty")
                            {
                                FillGround(tx, ty, bgColor);
                                continue;
                            }

                            var pixels = dictGroundPixels[idx];
                            if (pixels != null && pixels.Length >= TILE_PX * TILE_PX * 3)
                            {
                                for (int py = 0; py < TILE_PX; py++)
                                    for (int px = 0; px < TILE_PX; px++)
                                    {
                                        int off = (py * TILE_PX + px) * 3;
                                        groundBmp.SetPixel(tx * TILE_PX + px, ty * TILE_PX + py,
                                            new SKColor(pixels[off], pixels[off + 1], pixels[off + 2]));
                                    }
                            }
                            else if (_groundColors.TryGetValue(groundId, out uint gc))
                                FillGround(tx, ty, new SKColor(gc));
                            else if (customColorMap.TryGetValue(groundId, out uint cc))
                                FillGround(tx, ty, new SKColor(cc));
                            else
                                FillGround(tx, ty, new SKColor(0xFF444444));
                        }
                    }

                    using var gImg = SKImage.FromBitmap(groundBmp);
                    using var gData = gImg.Encode(SKEncodedImageFormat.Png, 100);
                    groundPng = Convert.ToBase64String(gData.ToArray());
                }

                // ===== Objects thumbnail =====
                string objectsPng;
                using (var objBmp = new SKBitmap(bmpW, bmpH))
                {
                    // Fill with transparent-ish dark background
                    var objBg = new SKColor(0xFF1a1a1a);
                    for (int y = 0; y < bmpH; y++)
                        for (int x = 0; x < bmpW; x++)
                            objBmp.SetPixel(x, y, objBg);

                    for (int ty = 0; ty < height; ty++)
                    {
                        for (int tx = 0; tx < width; tx++)
                        {
                            var idx = grid[ty * width + tx];
                            if (idx < 0 || idx >= dict.Count) continue;

                            var entry = dict[idx];
                            var objs = entry?["objs"] as JArray;
                            if (objs == null || objs.Count == 0) continue;

                            var objId = objs[0]?["id"]?.ToString();
                            if (string.IsNullOrEmpty(objId)) continue;

                            var objClass = objs[0]?["objectClass"]?.ToString() ?? "";

                            // Use objectPixels if available (pixel-accurate custom object)
                            var objPixels = dictObjPixels[idx];
                            var objSize = dictObjSize[idx];
                            if (objPixels != null && objSize > 0)
                            {
                                // For multi-tile anchors (16x16, 32x32), render the full sprite
                                int renderPx = objSize; // pixels to render
                                int expectedBytes = objSize * objSize * 3;
                                if (objPixels.Length >= expectedBytes)
                                {
                                    for (int py = 0; py < renderPx && (ty * TILE_PX + py) < bmpH; py++)
                                        for (int px = 0; px < renderPx && (tx * TILE_PX + px) < bmpW; px++)
                                        {
                                            int off = (py * objSize + px) * 3;
                                            var r = objPixels[off];
                                            var g = objPixels[off + 1];
                                            var b = objPixels[off + 2];
                                            // Skip near-black pixels (0x2a2a2a = empty/transparent)
                                            if (r <= 0x2a && g <= 0x2a && b <= 0x2a) continue;
                                            objBmp.SetPixel(tx * TILE_PX + px, ty * TILE_PX + py,
                                                new SKColor(r, g, b));
                                        }
                                }
                            }
                            else if (objClass == "Blocker")
                            {
                                // Blocker tiles: show as subtle outline
                                for (int py = 0; py < TILE_PX; py++)
                                    for (int px = 0; px < TILE_PX; px++)
                                    {
                                        if (py == 0 || py == TILE_PX - 1 || px == 0 || px == TILE_PX - 1)
                                            objBmp.SetPixel(tx * TILE_PX + px, ty * TILE_PX + py, new SKColor(0xFF333333));
                                    }
                            }
                            else
                            {
                                // Standard object: use color map
                                SKColor color;
                                if (_objectColors.TryGetValue(objId, out uint oc))
                                    color = new SKColor(oc);
                                else
                                    color = new SKColor(0xFF666666);

                                for (int py = 0; py < TILE_PX; py++)
                                    for (int px = 0; px < TILE_PX; px++)
                                        objBmp.SetPixel(tx * TILE_PX + px, ty * TILE_PX + py, color);
                            }
                        }
                    }

                    using var oImg = SKImage.FromBitmap(objBmp);
                    using var oData = oImg.Encode(SKEncodedImageFormat.Png, 100);
                    objectsPng = Convert.ToBase64String(oData.ToArray());
                }

                return new ThumbnailResult { GroundPng = groundPng, ObjectsPng = objectsPng };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapThumbnail] Error generating thumbnails: {ex.Message}");
                return null;
            }
        }

        // Legacy single thumbnail (kept for backward compat if needed)
        public string GenerateThumbnail(JToken mapJm, JObject customTiles, int maxDim = 512)
        {
            var result = GenerateThumbnails(mapJm, customTiles);
            return result?.GroundPng;
        }
    }
}
