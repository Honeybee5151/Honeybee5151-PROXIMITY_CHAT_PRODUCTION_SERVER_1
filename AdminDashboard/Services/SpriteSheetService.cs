using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AdminDashboard.Services
{
    public class SpriteSheetService
    {
        private const int COLUMNS = 16;
        private readonly GitHubService _github;

        public SpriteSheetService(GitHubService github)
        {
            _github = github;
        }

        /// <summary>Decode a data:image/png;base64,... URL to SKBitmap</summary>
        public static SKBitmap DecodeDataUrl(string dataUrl)
        {
            const string prefix = "data:image/png;base64,";
            var base64 = dataUrl.StartsWith(prefix) ? dataUrl.Substring(prefix.Length) : dataUrl;
            var bytes = Convert.FromBase64String(base64);
            var bitmap = SKBitmap.Decode(bytes);
            if (bitmap == null)
                throw new Exception("Failed to decode sprite PNG");
            return bitmap;
        }

        /// <summary>Load existing community sprite sheet + metadata from GitHub, or create empty</summary>
        public async Task<(SKBitmap Sheet, SheetMetadata Meta)> LoadSheet(int spriteSize)
        {
            var sheetName = spriteSize == 16 ? "communitySprites16x16" : "communitySprites8x8";
            var pngPath = $"Shared/resources/sprites/{sheetName}.png";
            var metaPath = $"Shared/resources/sprites/{sheetName}.meta.json";

            SKBitmap sheet;
            SheetMetadata meta;

            try
            {
                var pngBytes = await _github.FetchBinaryFile(pngPath);
                sheet = SKBitmap.Decode(pngBytes);
                if (sheet == null)
                    throw new Exception("Failed to decode existing sheet");

                var (metaJson, _) = await _github.FetchFile(metaPath);
                meta = JsonConvert.DeserializeObject<SheetMetadata>(metaJson) ?? new SheetMetadata();
            }
            catch
            {
                // First time: create empty sheet (1 row)
                var sheetWidth = COLUMNS * spriteSize;
                sheet = new SKBitmap(sheetWidth, spriteSize, SKColorType.Rgba8888, SKAlphaType.Premul);
                sheet.Erase(SKColors.Transparent);
                meta = new SheetMetadata { NextIndex = 0 };
            }

            return (sheet, meta);
        }

        /// <summary>
        /// Add sprites to the sheet. Returns assigned indices (one per sprite).
        /// Expands sheet height if needed.
        /// </summary>
        public (SKBitmap UpdatedSheet, int[] Indices) AddSprites(
            SKBitmap sheet, SheetMetadata meta, List<SKBitmap> sprites, int spriteSize)
        {
            var indices = new int[sprites.Count];
            var nextIdx = meta.NextIndex;

            // Calculate required rows after adding sprites
            var totalAfter = nextIdx + sprites.Count;
            var rowsNeeded = (totalAfter + COLUMNS - 1) / COLUMNS;
            var requiredHeight = rowsNeeded * spriteSize;

            // Expand sheet if needed
            if (requiredHeight > sheet.Height)
            {
                var expanded = new SKBitmap(sheet.Width, requiredHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
                expanded.Erase(SKColors.Transparent);

                using var canvas = new SKCanvas(expanded);
                canvas.DrawBitmap(sheet, 0, 0);
                sheet.Dispose();
                sheet = expanded;
            }

            // Draw each sprite at its assigned position
            using (var canvas = new SKCanvas(sheet))
            {
                for (int i = 0; i < sprites.Count; i++)
                {
                    var idx = nextIdx + i;
                    indices[i] = idx;

                    var col = idx % COLUMNS;
                    var row = idx / COLUMNS;
                    var x = col * spriteSize;
                    var y = row * spriteSize;

                    canvas.DrawBitmap(sprites[i], x, y);
                }
            }

            meta.NextIndex = nextIdx + sprites.Count;
            return (sheet, indices);
        }

        /// <summary>Encode SKBitmap to PNG bytes</summary>
        public static byte[] EncodePng(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    public class SheetMetadata
    {
        [JsonProperty("nextIndex")]
        public int NextIndex { get; set; }
    }
}
