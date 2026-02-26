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
            var x = Convert(data, File.ReadAllText(from), out _);

            File.WriteAllBytes(to, x);
        }

        public static byte[] Convert(XmlData data, string json, out List<CustomGroundEntry> customGrounds)
        {
            var obj = JsonConvert.DeserializeObject<json_dat>(json);
            var dat = ZlibStream.UncompressBuffer(obj.data);
            var tileDict = new Dictionary<short, TerrainTile>();

            var customGroundMap = new Dictionary<string, ushort>();
            ushort nextCustomCode = 0x8000;
            customGrounds = new List<CustomGroundEntry>();

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

                tileDict[(short)i] = new TerrainTile()
                {
                    TileId = tileId,
                    TileObj = o.objs?[0].id,
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
        }
    }
}
