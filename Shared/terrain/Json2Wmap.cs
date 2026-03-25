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
                    // Include all properties in dedup key so variants get separate type codes
                    var bp = o.blendPriority ?? -1;
                    var spd = o.speed ?? 1.0f;
                    var minDmg = (o.damage != null && o.damage.Length >= 1) ? o.damage[0] : 0;
                    var maxDmg = (o.damage != null && o.damage.Length >= 2) ? o.damage[1] : 0;
                    var sink = o.sink == true;
                    var animType = 0;
                    var animDx = 0f;
                    var animDy = 0f;
                    if (o.animate != null)
                    {
                        animType = o.animate.type == "Wave" ? 1 : o.animate.type == "Flow" ? 2 : 0;
                        animDx = o.animate.dx;
                        animDy = o.animate.dy;
                    }
                    var push = o.push == true;
                    var slideAmt = o.slide ?? 0f;
                    var groundKey = o.ground
                        + (o.blocked == true ? "|blocked" : "")
                        + (bp != -1 ? $"|bp{bp}" : "")
                        + (spd != 1.0f ? $"|spd{spd}" : "")
                        + (minDmg > 0 || maxDmg > 0 ? $"|dmg{minDmg}-{maxDmg}" : "")
                        + (sink ? "|sink" : "")
                        + (animType != 0 ? $"|anim{animType}_{animDx}_{animDy}" : "")
                        + (push ? "|push" : "")
                        + (slideAmt != 0 ? $"|slide{slideAmt}" : "");
                    if (!customGroundMap.TryGetValue(groundKey, out tileId))
                    {
                        tileId = data.AllocateCustomGroundTypeCode();
                        customGroundMap[groundKey] = tileId;
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
                            DecodedPixels = decodedGndPixels,
                            NoWalk = o.blocked == true,
                            BlendPriority = bp,
                            Speed = spd,
                            MinDamage = minDmg,
                            MaxDamage = maxDmg,
                            Sink = sink,
                            AnimateType = animType,
                            AnimateDx = animDx,
                            AnimateDy = animDy,
                            Push = push,
                            SlideAmount = slideAmt
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
                    var hasAnimFrames = o.objs[0].objectAnimFrames != null && o.objs[0].objectAnimFrames.Length > 0;
                    var dedupKey = o.objs[0].objectPixels + "|" + objClass + (hasAnimFrames ? "|ANIM" : "");
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
                        // Decode animation frames if present
                        List<byte[]> animFrames = null;
                        if (o.objs[0].objectAnimFrames != null && o.objs[0].objectAnimFrames.Length > 0)
                        {
                            animFrames = new List<byte[]>();
                            foreach (var frameB64 in o.objs[0].objectAnimFrames)
                            {
                                byte[] framePixels;
                                try { framePixels = System.Convert.FromBase64String(frameB64 ?? ""); }
                                catch { framePixels = new byte[expectedBytes]; }
                                if (framePixels.Length < expectedBytes)
                                {
                                    var padded = new byte[expectedBytes];
                                    Buffer.BlockCopy(framePixels, 0, padded, 0, framePixels.Length);
                                    framePixels = padded;
                                }
                                animFrames.Add(framePixels);
                            }
                        }
                        var hasAnim = animFrames != null && animFrames.Count > 0;
                        Console.WriteLine($"[Json2Wmap] Custom obj {objId}: size={spriteSize}, class={objClass}, animFrames={animFrames?.Count ?? 0}, rawField={(o.objs[0].objectAnimFrames != null ? o.objs[0].objectAnimFrames.Length.ToString() : "null")}");
                        customObjects.Add(new CustomObjectEntry
                        {
                            TypeCode = typeCode,
                            ObjectId = objId,
                            ObjectPixels = o.objs[0].objectPixels,
                            ObjectClass = objClass,
                            SpriteSize = spriteSize,
                            DecodedPixels = decodedPixels,
                            AnimFrames = animFrames
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
                    tileObjId = (o.objs != null && o.objs.Length > 0) ? o.objs[0].id : null;
                }

                tileDict[(short)i] = new TerrainTile()
                {
                    TileId = tileId,
                    TileObj = tileObjId,
                    Name = (o.objs == null || o.objs.Length == 0) ? "" : o.objs[0].name ?? "",
                    Terrain = TerrainType.None,
                    Region = o.regions == null ? TileRegion.None : (TileRegion)Enum.Parse(typeof(TileRegion), o.regions[0].id.Replace(' ', '_'))
                };
            }

            // Register TileDesc for ALL custom ground tiles (required for server tile lookups)
            foreach (var cg in customGrounds)
            {
                var xml = $"<Ground type=\"0x{cg.TypeCode:X4}\" id=\"{cg.GroundId}\">" +
                    "<Texture><File>lofiEnvironment2</File><Index>0x0b</Index></Texture>" +
                    (cg.NoWalk ? "<NoWalk/>" : "") +
                    (cg.BlendPriority != -1 ? $"<BlendPriority>{cg.BlendPriority}</BlendPriority>" : "") +
                    (cg.Speed != 1.0f ? $"<Speed>{cg.Speed}</Speed>" : "") +
                    (cg.MinDamage > 0 ? $"<MinDamage>{cg.MinDamage}</MinDamage>" : "") +
                    (cg.MaxDamage > 0 ? $"<MaxDamage>{cg.MaxDamage}</MaxDamage>" : "") +
                    (cg.Sink ? "<Sink/>" : "") +
                    (cg.AnimateType != 0 ? $"<Animate dx=\"{cg.AnimateDx}\" dy=\"{cg.AnimateDy}\">{(cg.AnimateType == 1 ? "Wave" : "Flow")}</Animate>" : "") +
                    (cg.Push ? "<Push/>" : "") +
                    (cg.SlideAmount != 0 ? $"<SlideAmount>{cg.SlideAmount}</SlideAmount>" : "") +
                    "</Ground>";
                data.Tiles[cg.TypeCode] = new TileDesc(cg.TypeCode, System.Xml.Linq.XElement.Parse(xml));
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

        private class json_animate
        {
            public string type;
            public float dx;
            public float dy;
        }

        private struct loc
        {
            public string ground;
            public string groundPixels;
            public bool? blocked;
            public int? blendPriority;
            public float? speed;
            public int[] damage;
            public bool? sink;
            public json_animate animate;
            public bool? push;
            public float? slide;
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
            public string[] objectAnimFrames;
        }
    }
}
