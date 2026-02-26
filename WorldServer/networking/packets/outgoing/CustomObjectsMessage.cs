using System;
using System.Collections.Generic;
using System.IO;
using Ionic.Zlib;
using NLog;
using Shared;
using Shared.resources;

namespace WorldServer.networking.packets.outgoing
{
    public class CustomObjectsMessage : OutgoingMessage
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public List<CustomObjectEntry> Entries { get; set; }

        public override MessageId MessageId => MessageId.CUSTOM_OBJECTS;

        public override void Write(NetworkWriter wtr)
        {
            var entries = Entries ?? new List<CustomObjectEntry>();
            Log.Info($"CustomObjectsMessage.Write: {entries.Count} entries");

            // Binary format: int32 count + (uint16 typeCode + byte[192] pixels + byte classFlag) per entry
            byte[] compressed;
            using (var ms = new MemoryStream())
            using (var bw = new NetworkWriter(ms))
            {
                bw.Write(entries.Count); // big-endian int32

                foreach (var entry in entries)
                {
                    bw.Write(entry.TypeCode); // big-endian uint16

                    // Decode base64 objectPixels to raw RGB bytes (192 bytes for 8x8x3)
                    byte[] pixels;
                    try
                    {
                        pixels = Convert.FromBase64String(entry.ObjectPixels ?? "");
                    }
                    catch
                    {
                        pixels = new byte[192];
                    }

                    // Ensure exactly 192 bytes
                    if (pixels.Length >= 192)
                        bw.Write(pixels, 0, 192);
                    else
                    {
                        bw.Write(pixels);
                        bw.Write(new byte[192 - pixels.Length]);
                    }

                    // Object class flag: 0=Wall, 1=DestructibleWall, 2=Decoration
                    byte classFlag = 0;
                    if (entry.ObjectClass == "DestructibleWall") classFlag = 1;
                    else if (entry.ObjectClass == "Decoration") classFlag = 2;
                    bw.Write(classFlag);
                }

                bw.Flush();
                compressed = ZlibStream.CompressBuffer(ms.ToArray());
            }
            Log.Info($"CustomObjectsMessage.Write: compressed={compressed.Length} bytes");
            wtr.Write(compressed.Length);
            wtr.Write(compressed);
        }
    }
}
