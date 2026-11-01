namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace System
{
    internal readonly struct Index
    {
        private readonly int value;
        public Index(int value, bool fromEnd = false) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); this.value = fromEnd ? ~value : value; }
        public int Value => value < 0 ? ~value : value;
        public bool IsFromEnd => value < 0;
        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;
        public static implicit operator Index(int value) => new(value);
        public static Index Start => new(0);
        public static Index End => new(0, true);
    }
    internal readonly struct Range
    {
        public Index Start { get; }
        public Index End { get; }
        public Range(Index start, Index end) { Start = start; End = end; }
        public static Range StartAt(Index start) => new(start, Index.End);
        public static Range EndAt(Index end) => new(Index.Start, end);
        public static Range All => new(Index.Start, Index.End);
        public (int Offset, int Length) GetOffsetAndLength(int length) { var start = Start.GetOffset(length); var end = End.GetOffset(length); if ((uint)end > (uint)length || (uint)start > (uint)end) throw new ArgumentOutOfRangeException(); return (start, end - start); }
    }
}

namespace ReleaseDock
{
    internal static class Framework
    {
        public static int ProcessId { get { using var process = System.Diagnostics.Process.GetCurrentProcess(); return process.Id; } }
        public static string ProcessPath => System.Reflection.Assembly.GetEntryAssembly()!.Location;
        public static string? GetDirectoryName(string? path) => string.IsNullOrEmpty(path) ? null : System.IO.Path.GetDirectoryName(path);
        public static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");
        public static byte[] Hash(byte[] bytes) { using var hash = System.Security.Cryptography.SHA256.Create(); return hash.ComputeHash(bytes); }
        public static byte[] Hash(System.IO.Stream stream) { using var hash = System.Security.Cryptography.SHA256.Create(); return hash.ComputeHash(stream); }
        public static async Task<byte[]> HashAsync(System.IO.Stream stream, CancellationToken cancel)
        {
            using var hash = System.Security.Cryptography.SHA256.Create(); var buffer = new byte[81920]; int count;
            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancel)) > 0) { cancel.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); }
            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0); return hash.Hash!;
        }
        public static bool IsPathFullyQualified(string path) => !string.IsNullOrEmpty(path) && (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/') || path.Length >= 3 && (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)));
        public static string TrimEndingDirectorySeparator(string path) => path.Length > System.IO.Path.GetPathRoot(path).Length ? path.TrimEnd('\\', '/') : path;
        public static string GetRelativePath(string root, string path)
        {
            root = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar; path = System.IO.Path.GetFullPath(path);
            return new Uri(root).MakeRelativeUri(new Uri(path)).ToString() is var relative ? Uri.UnescapeDataString(relative).Replace('/', System.IO.Path.DirectorySeparatorChar) : path;
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string source, string destination, int flags);
        public static void Move(string source, string destination, bool overwrite)
        {
            if (!overwrite) { System.IO.File.Move(source, destination); return; }
            if (!MoveFileEx(source, destination, 1 | 8)) throw new System.IO.IOException("Could not replace " + destination, new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()));
        }
        public static void AddArgument(this System.Diagnostics.ProcessStartInfo start, string argument)
        {
            // Windows CommandLineToArgvW quoting, including embedded quotes/trailing backslashes.
            var text = new System.Text.StringBuilder("\""); var slashes = 0;
            foreach (var c in argument) { if (c == '\\') { slashes++; continue; } if (c == '"') text.Append('\\', slashes * 2 + 1); else text.Append('\\', slashes); text.Append(c); slashes = 0; }
            text.Append('\\', slashes * 2).Append('"');
            start.Arguments += (start.Arguments.Length == 0 ? "" : " ") + text;
        }
        public static async Task WaitForExitAsync(this System.Diagnostics.Process process, CancellationToken cancel = default)
        {
            while (!process.HasExited) await Task.Delay(50, cancel);
            cancel.ThrowIfCancellationRequested();
        }
        public static bool Contains(this string text, string value, StringComparison comparison) => text.IndexOf(value, comparison) >= 0;
        public static bool Contains(this string text, char value) => text.IndexOf(value) >= 0;
        public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;
        public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;
        public static string[] Split(this string text, char separator, StringSplitOptions options) => text.Split(new[] { separator }, options);
        public static string[] Split(this string text, char separator, int count) => text.Split(new[] { separator }, count);
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> values, IEqualityComparer<T>? comparer = null) => new(values, comparer);
        public static int Read(this System.IO.Stream stream, byte[] buffer) => stream.Read(buffer, 0, buffer.Length);
        public static Task<int> ReadAsync(this System.IO.Stream stream, byte[] buffer, CancellationToken cancel) => stream.ReadAsync(buffer, 0, buffer.Length, cancel);
        public static Task<string> ReadAsStringAsync(this System.Net.Http.HttpContent content, CancellationToken cancel) { cancel.ThrowIfCancellationRequested(); return content.ReadAsStringAsync(); }
        public static Task<System.IO.Stream> ReadAsStreamAsync(this System.Net.Http.HttpContent content, CancellationToken cancel) { cancel.ThrowIfCancellationRequested(); return content.ReadAsStreamAsync(); }
    }
    internal sealed class FolderPicker
    {
        public string Title { get; set; } = "Choose folder";
        public string FolderName { get; set; } = "";
        public bool? ShowDialog(System.Windows.Window? owner = null)
        {
            var dialog = (FolderDialogs.IFileOpenDialog)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"))!)!;
            FolderDialogs.IShellItem? selected = null;
            try
            {
                dialog.GetOptions(out var options); dialog.SetOptions(options | 0x20 | 0x40 | 0x800 | 0x8 | 0x02000000); dialog.SetTitle(Title);
                if (System.IO.Directory.Exists(FolderName))
                {
                    var id = typeof(FolderDialogs.IShellItem).GUID;
                    FolderDialogs.SHCreateItemFromParsingName(FolderName, IntPtr.Zero, ref id, out var initial);
                    try { dialog.SetFolder(initial); } finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(initial); }
                }
                var result = dialog.Show(owner == null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(owner).Handle);
                if (result == unchecked((int)0x800704c7)) return false;
                System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(result);
                dialog.GetResult(out selected); selected.GetDisplayName(0x80058000, out var name);
                try { FolderName = System.Runtime.InteropServices.Marshal.PtrToStringUni(name)!; } finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(name); }
                return true;
            }
            finally { if (selected != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(selected); System.Runtime.InteropServices.Marshal.ReleaseComObject(dialog); }
        }
    }
}
