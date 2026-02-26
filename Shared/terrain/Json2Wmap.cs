using TKRShared;
using Ionic.Zlib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using Shared.resources;

namespace Shared.terrain
{
    internal class Json2Wmap
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

            // Custom objects: dedup by pixel content, assign 0x9000+ type codes
            var customObjPixelsMap = new Dictionary<string, string>(); // objectPixels base64 → objectId
            var customObjMap = new Dictionary<string, ushort>(); // objectPixels base64 → typeCode
            ushort nextCustomObjCode = 0x9000;
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
                        customGrounds.Add(new CustomGroundEntry
                        {
                            TypeCode = tileId,
                            GroundId = o.ground,
                            GroundPixels = o.groundPixels
                        });
                    }
                }
                else
                    tileId = data.IdToTileType[o.ground];

                // Handle custom object pixels
                string tileObjId = null;
                if (o.objs != null && o.objs.Length > 0 && !string.IsNullOrEmpty(o.objs[0].objectPixels))
                {
                    var pixelsKey = o.objs[0].objectPixels;
                    if (!customObjMap.TryGetValue(pixelsKey, out _))
                    {
                        var objId = $"cobj_{nextCustomObjCode:x4}";
                        customObjMap[pixelsKey] = nextCustomObjCode;
                        customObjPixelsMap[pixelsKey] = objId;
                        customObjects.Add(new CustomObjectEntry
                        {
                            TypeCode = nextCustomObjCode,
                            ObjectId = objId,
                            ObjectPixels = pixelsKey,
                            ObjectClass = o.objs[0].objectClass ?? "Wall"
                        });
                        nextCustomObjCode++;
                    }
                    tileObjId = customObjPixelsMap[pixelsKey];
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
        }
    }
}
