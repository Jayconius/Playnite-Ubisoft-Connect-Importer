using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UbisoftConnectImporter
{
    // Minimal protobuf wire reader for the legacy Ubisoft cache collection. It has no
    // dependency on protobuf-net, which keeps the extension self-contained.
    internal static class ProtobufReader
    {
        public static IEnumerable<CacheProduct> ReadLegacyProducts(byte[] data)
        {
            foreach (var field in ReadFields(data, 0, data.Length))
            {
                if (field.Number != 1 || field.Wire != 2) continue;
                uint id = 0, install = 0;
                string info = null;
                foreach (var item in ReadFields(data, field.Offset, field.Length))
                {
                    if (item.Number == 1 && item.Wire == 0) id = (uint)item.Varint;
                    else if (item.Number == 2 && item.Wire == 0) install = (uint)item.Varint;
                    else if (item.Number == 3 && item.Wire == 2) info = Encoding.UTF8.GetString(data, item.Offset, item.Length);
                }
                if (!string.IsNullOrWhiteSpace(info))
                    yield return new CacheProduct { UplayId = id, InstallId = install, GameInfo = info };
            }
        }

        private sealed class Field
        {
            public int Number, Wire, Offset, Length;
            public ulong Varint;
        }

        private static IEnumerable<Field> ReadFields(byte[] data, int start, int length)
        {
            var end = checked(start + length);
            var pos = start;
            while (pos < end)
            {
                var tag = ReadVarint(data, ref pos, end);
                var number = (int)(tag >> 3);
                var wire = (int)(tag & 7);
                if (number == 0) yield break;
                var field = new Field { Number = number, Wire = wire };
                switch (wire)
                {
                    case 0: field.Varint = ReadVarint(data, ref pos, end); break;
                    case 1: field.Offset = pos; field.Length = 8; pos += 8; break;
                    case 2:
                        var size = ReadVarint(data, ref pos, end);
                        if (size > int.MaxValue || pos + (long)size > end) yield break;
                        field.Offset = pos; field.Length = (int)size; pos += (int)size; break;
                    case 5: field.Offset = pos; field.Length = 4; pos += 4; break;
                    default: yield break;
                }
                if (pos > end) yield break;
                yield return field;
            }
        }

        private static ulong ReadVarint(byte[] data, ref int pos, int end)
        {
            ulong value = 0;
            var shift = 0;
            while (pos < end && shift < 64)
            {
                var b = data[pos++];
                value |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
            }
            throw new InvalidDataException("Invalid protobuf varint.");
        }
    }
}
