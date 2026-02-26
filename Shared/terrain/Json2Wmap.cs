using TKRShared;
using Ionic.Zlib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using Shared.resources;

namespace Shared.terrain
{
    public class Json2Wmap
    {
        public static void Convert(XmlData data, string from, string to)
        {
            var x = Convert(data, File.ReadAllText(from), out _, out _);

            File.WriteAllBytes(to, x);
        }

        public static byte[] Convert(XmlData data, string json, out List<CustomGroundEntry> customGrounds, out List<CustomObjectEntry> customObjects)
        {
            var obj = JsonConvert.DeserializeObject<json_dat>(json);
            var dat = ZlibStream.UncompressBuffer(obj.data);
            var tileDict = new Dictionary<short, TerrainTile>();

            var customGroundMap = new Dictionary<string, ushort>();
            ushort nextCustomCode = 0x8000;
            customGrounds = new List<CustomGroundEntry>();

            // Custom objects: dedup by (pixels + class), type codes from global allocator
            var customObjPixelsMap = new Dictionary<string, string>(); // (pixels|class) → objectId
            var customObjMap = new Dictionary<string, ushort>(); // (pixels|class) → typeCode
            customObjects = new List<CustomObjectEntry>();

            for (var i = 0; i < obj.dict.Length; i++)
            {
                var o = obj.dict[i];

                ushort tileId;
                if (o.ground == null)
                    tileId = 0xff;
                else if (o.ground.StartsWith("custom_"))
                {
                    if (!customGroundMap.TryGetValue(o.ground, out tileId))
                    {
                        tileId = nextCustomCode++;
                        customGroundMap[o.ground] = tileId;
                        // Decode base64 pixels once at load time
                        byte[] decodedGndPixels;
                        try { decodedGndPixels = System.Convert.FromBase64String(o.groundPixels ?? ""); }
                        catch { decodedGndPixels = new byte[192]; }
                        if (decodedGndPixels.Length < 192)
                        {
                            var padded = new byte[192];
                            Buffer.BlockCopy(decodedGndPixels, 0, padded, 0, decodedGndPixels.Length);
                            decodedGndPixels = padded;
                        }
                        customGrounds.Add(new CustomGroundEntry
                        {
                            TypeCode = tileId,
                            GroundId = o.ground,
                            GroundPixels = o.groundPixels,
                            DecodedPixels = decodedGndPixels
                        });
                    }
                }
                else
                    tileId = data.IdToTileType[o.ground];

                // Handle custom object pixels
                string tileObjId = null;
                if (o.objs != null && o.objs.Length > 0 && !string.IsNullOrEmpty(o.objs[0].objectPixels))
                {
                    var objClass = o.objs[0].objectClass ?? "Object";
                    byte spriteSize = (byte)(o.objs[0].objectSize > 0 ? o.objs[0].objectSize : 8);
                    int expectedBytes = spriteSize * spriteSize * 3;
                    var dedupKey = o.objs[0].objectPixels + "|" + objClass;
                    if (!customObjMap.TryGetValue(dedupKey, out _))
                    {
                        var typeCode = data.AllocateCustomObjTypeCode();
                        var objId = $"cobj_{typeCode:x4}";
                        customObjMap[dedupKey] = typeCode;
                        customObjPixelsMap[dedupKey] = objId;
                        byte[] decodedPixels;
                        try { decodedPixels = System.Convert.FromBase64String(o.objs[0].objectPixels ?? ""); }
                        catch { decodedPixels = new byte[expectedBytes]; }
                        if (decodedPixels.Length < expectedBytes)
                        {
                            var padded = new byte[expectedBytes];
                            Buffer.BlockCopy(decodedPixels, 0, padded, 0, decodedPixels.Length);
                            decodedPixels = padded;
                        }
                        customObjects.Add(new CustomObjectEntry
                        {
                            TypeCode = typeCode,
                            ObjectId = objId,
                            ObjectPixels = o.objs[0].objectPixels,
                            ObjectClass = objClass,
                            SpriteSize = spriteSize,
                            DecodedPixels = decodedPixels
                        });
                    }
                    tileObjId = customObjPixelsMap[dedupKey];
                }
                else if (o.objs != null && o.objs.Length > 0 && o.objs[0].objectClass == "Blocker")
                {
                    // Invisible blocker for multi-tile objects (no pixel data)
                    var dedupKey = "blocker";
                    if (!customObjMap.TryGetValue(dedupKey, out _))
                    {
                        var typeCode = data.AllocateCustomObjTypeCode();
                        var objId = $"cobj_{typeCode:x4}";
                        customObjMap[dedupKey] = typeCode;
                        customObjPixelsMap[dedupKey] = objId;
                        customObjects.Add(new CustomObjectEntry
                        {
                            TypeCode = typeCode,
                            ObjectId = objId,
                            ObjectClass = "Blocker",
                            SpriteSize = 0, // no sprite
                            DecodedPixels = null
                        });
                    }
                    tileObjId = customObjPixelsMap[dedupKey];
                }
                else
                {
                    tileObjId = o.objs?[0].id;
                }

                tileDict[(short)i] = new TerrainTile()
                {
                    TileId = tileId,
                    TileObj = tileObjId,
                    Name = o.objs == null ? "" : o.objs[0].name ?? "",
                    Terrain = TerrainType.None,
                    Region = o.regions == null ? TileRegion.None : (TileRegion)Enum.Parse(typeof(TileRegion), o.regions[0].id.Replace(' ', '_'))
                };
            }

            var tiles = new TerrainTile[obj.width, obj.height];

            using (var rdr = new NetworkReader(new MemoryStream(dat)))
                for (var y = 0; y < obj.height; y++)
                    for (var x = 0; x < obj.width; x++)
                        tiles[x, y] = tileDict[rdr.ReadInt16()];

            return WorldMapExporter.Export(tiles);
        }

        private struct json_dat
        {
            public byte[] data;
            public loc[] dict;
            public int height;
            public int width;
        }

        private struct loc
        {
            public string ground;
            public string groundPixels;
            public obj[] objs;
            public obj[] regions;
        }

        private struct obj
        {
            public string id;
            public string name;
            public string objectPixels;
            public string objectClass;
            public int objectSize;
        }
    }
}
