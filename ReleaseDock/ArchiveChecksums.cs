using System.IO;

namespace ReleaseDock;

internal static class ArchiveChecksums
{
    // Read CRCs from the same central-directory order used by ZipArchive.
    // Framework's ZipArchiveEntry does not expose CRC32 publicly.
    internal static uint[] Read(string path, int count)
    {
        using var file = File.OpenRead(path); using var reader = new BinaryReader(file);
        var start = Math.Max(0, file.Length - 65557); file.Position = start; var tail = reader.ReadBytes((int)(file.Length - start));
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
            if (BitConverter.ToUInt32(tail, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == tail.Length) { end = i; break; }
        if (end < 0) throw new IOException("ZIP end record is missing.");
        if (BitConverter.ToUInt16(tail, end + 4) != 0 || BitConverter.ToUInt16(tail, end + 6) != 0) throw new IOException("Split ZIP archives are not supported.");
        long offset = BitConverter.ToUInt32(tail, end + 16);
        if (offset == uint.MaxValue || BitConverter.ToUInt16(tail, end + 10) == ushort.MaxValue)
        {
            file.Position = start + end - 20;
            if (file.Position < 0 || reader.ReadUInt32() != 0x07064b50 || reader.ReadUInt32() != 0) throw new IOException("Invalid ZIP64 locator.");
            var record = reader.ReadInt64(); if (reader.ReadUInt32() != 1 || record < 0 || record > file.Length - 56) throw new IOException("Invalid ZIP64 record.");
            file.Position = record;
            if (reader.ReadUInt32() != 0x06064b50) throw new IOException("Invalid ZIP64 record.");
            file.Position = record + 16;
            if (reader.ReadUInt32() != 0 || reader.ReadUInt32() != 0) throw new IOException("Split ZIP archives are not supported.");
            file.Position = record + 32; if (reader.ReadInt64() != count) throw new IOException("ZIP entry count mismatch.");
            file.Position = record + 48; offset = reader.ReadInt64();
        }
        else if (BitConverter.ToUInt16(tail, end + 10) != count) throw new IOException("ZIP entry count mismatch.");
        if (offset < 0 || offset > file.Length - 4) throw new IOException("Invalid ZIP directory.");
        file.Position = offset; var checksums = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var record = file.Position;
            if (record > file.Length - 46 || reader.ReadUInt32() != 0x02014b50) throw new IOException("Invalid ZIP directory entry.");
            file.Position = record + 16; checksums[i] = reader.ReadUInt32();
            file.Position = record + 28;
            var length = reader.ReadUInt16() + reader.ReadUInt16() + reader.ReadUInt16();
            if (record + 46 + length > file.Length) throw new IOException("Truncated ZIP directory entry.");
            file.Position = record + 46 + length;
        }
        return checksums;
    }
}
