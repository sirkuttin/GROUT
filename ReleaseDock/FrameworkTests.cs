using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ReleaseDock.Json;

namespace ReleaseDock;

internal static class FrameworkTests
{
    public static int Assertions { get; private set; }
    public static void Run()
    {
        Assertions = 0;
        void Check(bool ok, string message) { Assertions++; if (!ok) throw new Exception(message); }
        // Golden input in the System.Text.Json format written by GROUT 0.5.x.
        const string oldLibrary = """[{"Repo":"owner/app","Installed":"v1","Latest":"v2","AutoReconcile":true,"CheckedAt":"2026-11-01T09:13:00.1234567-07:00","Release":{"Tag":"v2","Page":"https://github.com/owner/app/releases/tag/v2","Notes":"Line one\n日本語 and \u0022quotes\u0022","Assets":[{"Name":"app.exe","Url":"https://github.com/owner/app/releases/download/v2/app.exe","Size":2147483648,"Digest":null,"OriginHost":"github.com"}]},"UnknownFutureField":42}]""";
        var apps = JsonSerializer.Deserialize<List<TrackedApp>>(oldLibrary)!; var app = apps.Single();
        Check(app.AutoReconcile && app.Installed == "v1" && app.Release!.Assets.Single().Size == 2147483648L, "Old library records, defaults and 64-bit sizes load");
        Check(app.CheckedAt!.Value.Offset == TimeSpan.FromHours(-7) && app.CheckedAt.Value.Ticks % TimeSpan.TicksPerSecond == 1234567, "ISO date offsets and tick precision survive");
        var saved = JsonSerializer.Serialize(apps); var loaded = JsonSerializer.Deserialize<List<TrackedApp>>(saved)!.Single();
        Check(loaded.Release!.Notes == app.Release!.Notes && loaded.CheckedAt == app.CheckedAt, "Unicode, escaping and date roundtrip");
        using var document = JsonDocument.Parse(saved);
        Check(!document.RootElement.EnumerateArray().First().TryGetProperty("AppName", out _) && !saved.Contains("LocalStatus"), "Runtime-only properties are not persisted");
        var settings = JsonSerializer.Deserialize<Preferences>("""{"CheckHours":6,"LastAttempt":"2026-11-01T16:00:00+00:00","SourceBackoffs":{"gitlab.com":"2026-11-01T17:00:00+00:00"}}""")!;
        Check(settings.NextCheck!.Value.Hour == 22 && settings.SourceBackoffs["gitlab.com"].Hour == 17 && settings.SelfUpdateChecks, "Old settings retain defaults, dates and source backoffs");
        Check(JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(settings))!.SourceBackoffs.Count == 1, "Settings dictionary roundtrip");
        var plan = JsonSerializer.Deserialize<SelfUpdatePlan>("""{"ParentPid":123,"ParentStart":639000000000000001,"Target":"C:\\Apps\\GROUT.exe","Package":"C:\\Temp\\update.exe","Hash":"abc","TestReport":""}""")!;
        Check(plan.ParentStart == 639000000000000001L && plan.OriginalTarget == "" && !plan.TestFailStartup, "Old updater plans retain exact ticks and optional-field defaults");
        var snapshot = new InstallationSnapshot("v1", true, "source", @"C:\Apps\a.exe", @"C:\Apps", "a.exe", "", true);
        Check(JsonSerializer.Deserialize<InstallationSnapshot>(JsonSerializer.Serialize(snapshot)) == snapshot, "Rollback snapshots roundtrip");
        foreach (var json in new[] { "{", "{\"CheckHours\":1.5}", "{\"CheckHours\":\"6\"}", "{\"Quiet\":\"true\"}" })
        {
            var rejected = false; try { JsonSerializer.Deserialize<Preferences>(json); } catch (JsonException) { rejected = true; }
            Check(rejected, "Invalid or wrongly typed data rejected");
        }
        using var fractional = JsonDocument.Parse("{\"size\":1.5}");
        var invalidSize = false; try { fractional.RootElement.GetProperty("size").GetInt64(); } catch (JsonException) { invalidSize = true; }
        Check(invalidSize, "Fractional asset sizes rejected");
        var start = new ProcessStartInfo(); start.AddArgument(@"C:\Apps\a folder\"); start.AddArgument("a\"b");
        Check(start.Arguments == "\"C:\\Apps\\a folder\\\\\" \"a\\\"b\"", "Windows argument escaping preserves trailing slashes and quotes");
        var folder = Path.GetTempPath().TrimEnd('\\');
        var dialog = (FolderDialogs.IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"))!)!;
        try
        {
            dialog.SetTitle("GROUT isolated folder picker test"); dialog.GetOptions(out var options); dialog.SetOptions(options | 0x20 | 0x40); dialog.GetOptions(out var updated);
            var id = typeof(FolderDialogs.IShellItem).GUID; FolderDialogs.SHCreateItemFromParsingName(folder, IntPtr.Zero, ref id, out var item);
            try { dialog.SetFolder(item); item.GetDisplayName(0x80058000, out var name); try { Check(Marshal.PtrToStringUni(name)!.TrimEnd('\\').Equals(folder, StringComparison.OrdinalIgnoreCase) && (updated & 0x20) != 0, "Modern native folder picker accepts filesystem location and folder options"); } finally { Marshal.FreeCoTaskMem(name); } }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }
}
