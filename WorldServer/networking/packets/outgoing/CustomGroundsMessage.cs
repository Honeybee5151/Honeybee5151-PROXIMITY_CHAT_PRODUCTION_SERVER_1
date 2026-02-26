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
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms))
            {
                // Write count as big-endian int32
                bw.Write(System.Net.IPAddress.HostToNetworkOrder(entries.Count));

                foreach (var entry in entries)
                {
                    // Write type code as big-endian uint16
                    bw.Write((byte)(entry.TypeCode >> 8));
                    bw.Write((byte)(entry.TypeCode & 0xFF));

                    // Decode base64 groundPixels to raw RGB bytes (192 bytes for 8x8x3)
                    byte[] pixels;
                    try
                    {
                        pixels = Convert.FromBase64String(entry.GroundPixels ?? "");
                    }
                    catch
                    {
                        pixels = new byte[192]; // black fallback
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
            }

            var raw = ms.ToArray();
            var compressed = ZlibStream.CompressBuffer(raw);
            wtr.Write(compressed.Length);
            wtr.Write(compressed);
        }
    }
}
