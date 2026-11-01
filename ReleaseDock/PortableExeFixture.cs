using System;
using System.IO;

namespace ReleaseDock;

internal static class PortableExeFixture
{
    // Small native PE with no version resources; fixtures are inspected/copied, never executed.
    internal static byte[] Bytes()
    {
        var bytes = new byte[1024];
        using var stream = new MemoryStream(bytes);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0x5A4D); stream.Position = 0x3c; writer.Write(0x80);
        stream.Position = 0x80; writer.Write(0x00004550); writer.Write((ushort)0x14c); writer.Write((ushort)1);
        stream.Position = 0x94; writer.Write((ushort)224); writer.Write((ushort)0x102);
        writer.Write((ushort)0x10b); stream.Position = 0xa8; writer.Write(0x1000);
        stream.Position = 0xb4; writer.Write(0x400000); writer.Write(0x1000); writer.Write(0x200);
        stream.Position = 0xd0; writer.Write(0x2000); writer.Write(0x200);
        stream.Position = 0xdc; writer.Write((ushort)3);
        stream.Position = 0xf4; writer.Write(16);
        stream.Position = 0x178; writer.Write(new byte[] { 46, 116, 101, 120, 116, 0, 0, 0 });
        writer.Write(1); writer.Write(0x1000); writer.Write(0x200); writer.Write(0x200);
        stream.Position = 0x19c; writer.Write(0x60000020);
        bytes[0x200] = 0xc3;
        return bytes;
    }
}
