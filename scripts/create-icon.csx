#r "nuget: System.Drawing.Common, 10.0.0"
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

const string Help = "Usage: dotnet script scripts/create-icon.csx -- [--output FOLDER]\nGenerate GROUT.svg, GROUT.png and a multi-size GROUT.ico.\n--output defaults to ReleaseDock/Assets relative to the current directory.\n-h, --help shows help. Unknown or duplicate flags fail.";
try
{
    var output = "ReleaseDock/Assets"; var set = false;
    for (var i = 0; i < Args.Count; i++)
    {
        if (Args[i] is "--help" or "-h") { Console.WriteLine(Help); return; }
        if (Args[i] != "--output" || set || i + 1 >= Args.Count || Args[i + 1].StartsWith("--")) throw new ArgumentException("Unknown or incomplete option: " + Args[i]);
        output = Args[++i]; set = true;
    }
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output, "GROUT.svg"), """
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
  <rect x="1" y="1" width="62" height="62" rx="14" fill="#142033" stroke="#294256" stroke-width="1.5"/>
  <path d="M44 19H28C21.4 19 17 23.4 17 30V36C17 42.6 21.4 47 28 47H36C42.6 47 47 42.6 47 36V32H34" fill="none" stroke="#76DCC6" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>
</svg>
""" + "\n");
    var frames = new List<byte[]>(); var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
    foreach (var size in sizes)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent); graphics.SmoothingMode = SmoothingMode.AntiAlias; graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.ScaleTransform(size / 64f, size / 64f);
            using var tile = new GraphicsPath();
            tile.AddArc(1, 1, 28, 28, 180, 90); tile.AddArc(35, 1, 28, 28, 270, 90); tile.AddArc(35, 35, 28, 28, 0, 90); tile.AddArc(1, 35, 28, 28, 90, 90); tile.CloseFigure();
            using var background = new SolidBrush(Color.FromArgb(20, 32, 51)); using var border = new Pen(Color.FromArgb(41, 66, 86), 1.5f);
            graphics.FillPath(background, tile); graphics.DrawPath(border, tile);
            using var mark = new GraphicsPath(); mark.AddLine(44, 19, 28, 19); mark.AddBezier(28, 19, 21.4f, 19, 17, 23.4f, 17, 30); mark.AddLine(17, 30, 17, 36); mark.AddBezier(17, 36, 17, 42.6f, 21.4f, 47, 28, 47); mark.AddLine(28, 47, 36, 47); mark.AddBezier(36, 47, 42.6f, 47, 47, 42.6f, 47, 36); mark.AddLine(47, 36, 47, 32); mark.AddLine(47, 32, 34, 32);
            using var ink = new Pen(Color.FromArgb(118, 220, 198), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; graphics.DrawPath(ink, mark);
        }
        using var bytes = new MemoryStream(); bitmap.Save(bytes, ImageFormat.Png); frames.Add(bytes.ToArray());
        if (size == 256) File.WriteAllBytes(Path.Combine(output, "GROUT.png"), bytes.ToArray());
    }
    using var icon = File.Create(Path.Combine(output, "GROUT.ico")); using var writer = new BinaryWriter(icon);
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++) { writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)frames[i].Length); writer.Write((uint)offset); offset += frames[i].Length; }
    foreach (var frame in frames) writer.Write(frame);
    Console.WriteLine("Generated icon assets in " + Path.GetFullPath(output));
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message + "\n" + Help); Environment.ExitCode = 1; }
