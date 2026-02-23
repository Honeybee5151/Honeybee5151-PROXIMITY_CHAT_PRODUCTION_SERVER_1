using Newtonsoft.Json.Linq;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AdminDashboard.Services
{
    /// <summary>
    /// Generates a top-down color thumbnail of a JM map for admin preview.
    /// Each tile = 1 pixel. Ground tiles use sprite-colors.json mapping,
    /// custom tiles use their hex color, objects rendered as semi-transparent overlay.
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

        /// <summary>
        /// Generate a PNG thumbnail from JM map data.
        /// Returns base64-encoded PNG string, or null on failure.
        /// </summary>
        public string GenerateThumbnail(JToken mapJm, JObject customTiles, int maxDim = 512)
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

                // Create bitmap — scale each tile to TILE_PX pixels so the thumbnail is visible
                const int TILE_PX = 8;
                var bmpW = width * TILE_PX;
                var bmpH = height * TILE_PX;
                using var bitmap = new SKBitmap(bmpW, bmpH);
                var bgColor = new SKColor(0xFF111111); // dark background for empty

                // Helper to fill a tile-sized block
                void FillTile(int tx, int ty, SKColor color)
                {
                    for (int py = 0; py < TILE_PX; py++)
                        for (int px = 0; px < TILE_PX; px++)
                            bitmap.SetPixel(tx * TILE_PX + px, ty * TILE_PX + py, color);
                }

                // Pass 1: Ground layer
                for (int ty = 0; ty < height; ty++)
                {
                    for (int tx = 0; tx < width; tx++)
                    {
                        var idx = grid[ty * width + tx];
                        if (idx < 0 || idx >= dict.Count)
                        {
                            FillTile(tx, ty, bgColor);
                            continue;
                        }

                        var entry = dict[idx];
                        var groundId = entry?["ground"]?.ToString();

                        if (string.IsNullOrEmpty(groundId) || groundId == "Empty")
                        {
                            FillTile(tx, ty, bgColor);
                            continue;
                        }

                        // Try standard color, then custom color
                        if (_groundColors.TryGetValue(groundId, out uint gc))
                            FillTile(tx, ty, new SKColor(gc));
                        else if (customColorMap.TryGetValue(groundId, out uint cc))
                            FillTile(tx, ty, new SKColor(cc));
                        else
                            FillTile(tx, ty, new SKColor(0xFF444444)); // unknown tile = grey
                    }
                }

                // Pass 2: Object overlay (blend into existing tile block)
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

                        // Get blend color
                        SKColor blendColor;
                        float objWeight = 0.6f;
                        if (_objectColors.TryGetValue(objId, out uint oc))
                            blendColor = new SKColor(oc);
                        else
                        {
                            blendColor = new SKColor(0, 0, 0);
                            objWeight = 0.3f; // unknown = slight darken
                        }

                        for (int py = 0; py < TILE_PX; py++)
                        {
                            for (int px = 0; px < TILE_PX; px++)
                            {
                                var ex = bitmap.GetPixel(tx * TILE_PX + px, ty * TILE_PX + py);
                                var r = (byte)(blendColor.Red * objWeight + ex.Red * (1 - objWeight));
                                var g = (byte)(blendColor.Green * objWeight + ex.Green * (1 - objWeight));
                                var b = (byte)(blendColor.Blue * objWeight + ex.Blue * (1 - objWeight));
                                bitmap.SetPixel(tx * TILE_PX + px, ty * TILE_PX + py, new SKColor(r, g, b));
                            }
                        }
                    }
                }

                // Encode to PNG
                using var image = SKImage.FromBitmap(bitmap);
                using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
                return Convert.ToBase64String(pngData.ToArray());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MapThumbnail] Error generating thumbnail: {ex.Message}");
                return null;
            }
        }
    }
}
