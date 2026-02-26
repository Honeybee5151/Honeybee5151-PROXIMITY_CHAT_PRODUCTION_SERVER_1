using System;
using System.Collections.Generic;
using System.IO;
using Ionic.Zlib;
using Shared;
using Shared.resources;

namespace WorldServer.networking.packets.outgoing
{
    public class CustomGroundsMessage : OutgoingMessage
    {
        public List<CustomGroundEntry> Entries { get; set; }

        public override MessageId MessageId => MessageId.CUSTOM_GROUNDS;

        public override void Write(NetworkWriter wtr)
        {
            var entries = Entries ?? new List<CustomGroundEntry>();

            // Binary format: int32 count + (uint16 typeCode + byte[192] pixels) per entry
            // Use NetworkWriter for consistent big-endian encoding
            var ms = new MemoryStream();
            var bw = new NetworkWriter(ms);

            bw.Write(entries.Count); // big-endian int32

            foreach (var entry in entries)
            {
                bw.Write(entry.TypeCode); // big-endian uint16

                // Decode base64 groundPixels to raw RGB bytes (192 bytes for 8x8x3)
                byte[] pixels;
                try
                {
                    pixels = Convert.FromBase64String(entry.GroundPixels ?? "");
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
            }

            bw.Flush();
            var raw = ms.ToArray();
            var compressed = ZlibStream.CompressBuffer(raw);
            wtr.Write(compressed.Length);
            wtr.Write(compressed);
        }
    }
}
