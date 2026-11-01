using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
namespace ReleaseDock;

public sealed record SelfUpdatePlan(int ParentPid, long ParentStart, string Target, string Package, string Hash, string TestReport = "", string OriginalTarget = "", string OriginalHash = "", bool TestFailStartup = false);
public static class SelfUpdateHandoff
{
    public static string WorkRoot => Path.Combine(AppPaths.Data, "self-update");
    public static string ValidatePlanPath(string planFile)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(planFile))!;
        if (!folder.StartsWith(Path.GetFullPath(WorkRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(planFile) != "plan.json") throw new IOException("Invalid self-update workspace.");
        FileUpdates.NoLinks(folder);
        return folder;
    }
    public static void Verify(string package, string hash)
    {
        using var file = File.OpenRead(package);
        if (file.Length < 2 || file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new IOException("Update is not a Windows executable.");
        file.Position = 0;
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Self-update checksum mismatch.");
    }
    public static void Replace(string package, string target, string hash, Action launch)
    {
        Verify(package, hash); FileUpdates.NoLinks(target);
        var next = target + ".grout-next-" + Guid.NewGuid().ToString("N");
        var backup = target + ".grout-rollback-" + Guid.NewGuid().ToString("N");
        var replaced = false;
        try
        {
            File.Copy(package, next); File.Replace(next, target, backup); replaced = true;
            launch();
        }
        catch
        {
            if (replaced && File.Exists(backup)) File.Move(backup, target, true);
            throw;
        }
        finally { if (File.Exists(next)) File.Delete(next); }
        try { if (File.Exists(backup)) File.Delete(backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public static async Task<Process> Start(string package, string hash, string folder, string testReport = "", bool testFailStartup = false)
    {
        using var parent = Process.GetCurrentProcess();
        var plan = new SelfUpdatePlan(parent.Id, parent.StartTime.ToUniversalTime().Ticks, Environment.ProcessPath!, package, hash, testReport, TestFailStartup: testFailStartup);
        Verify(package, hash);
        var planFile = Path.Combine(folder, "plan.json");
        File.WriteAllText(planFile, JsonSerializer.Serialize(plan)); ValidatePlanPath(planFile);
        var start = new ProcessStartInfo(package) { UseShellExecute = true, WorkingDirectory = folder };
        start.ArgumentList.Add("--apply-self-update"); start.ArgumentList.Add("--plan"); start.ArgumentList.Add(planFile);
        var helper = Process.Start(start) ?? throw new IOException("Update helper could not start.");
        for (var i = 0; i < 150; i++)
        {
            if (File.Exists(Path.Combine(folder, "ready"))) return helper;
            if (helper.HasExited) break;
            await Task.Delay(100);
        }
        try { helper.Kill(); } catch { }
        helper.Dispose();
        var error = Path.Combine(folder, "error.txt");
        throw new IOException(File.Exists(error) ? File.ReadAllText(error) : "Update helper did not become ready. GROUT is still running unchanged.");
    }
    public static async Task<int> Apply(string planFile)
    {
        string? folder = null;
        SelfUpdatePlan? plan = null;
        var parentExited = false;
        try
        {
            folder = ValidatePlanPath(planFile);
            plan = JsonSerializer.Deserialize<SelfUpdatePlan>(File.ReadAllText(planFile)) ?? throw new IOException("Invalid update plan.");
            if (!Path.GetFullPath(plan.Package).Equals(Path.Combine(folder, "update.exe"), StringComparison.OrdinalIgnoreCase) || !Path.GetFullPath(plan.Package).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected update helper location.");
            if (!Path.IsPathFullyQualified(plan.Target) || !plan.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid update target.");
            using var parent = Process.GetProcessById(plan.ParentPid);
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStart || !string.Equals(parent.MainModule?.FileName, plan.Target, StringComparison.OrdinalIgnoreCase)) throw new IOException("The running GROUT instance does not match the update target.");
            Verify(plan.Package, plan.Hash); FileUpdates.NoLinks(plan.Target);
            var oldTarget = plan.Target;
            var destination = SelfUpdateMigration.Destination(oldTarget);
            SelfUpdateMigration.CheckDestination(oldTarget, destination);
            if (!destination.Equals(oldTarget, StringComparison.OrdinalIgnoreCase))
            {
                string oldHash; using (var file = File.OpenRead(oldTarget)) oldHash = Convert.ToHexString(SHA256.HashData(file));
                plan = plan with { Target = destination, OriginalTarget = oldTarget, OriginalHash = oldHash };
                File.WriteAllText(planFile, JsonSerializer.Serialize(plan));
            }
            var probe = plan.Target + ".grout-probe-" + Guid.NewGuid().ToString("N");
            using (File.Create(probe)) { } File.Delete(probe);
            File.WriteAllText(Path.Combine(folder, "ready"), "ready");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            await parent.WaitForExitAsync(timeout.Token);
            parentExited = true;
            await Task.Delay(500);
            var start = new ProcessStartInfo(plan.Target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(plan.Target)! };
            start.ArgumentList.Add("--finish-self-update"); start.ArgumentList.Add("--plan"); start.ArgumentList.Add(planFile);
            start.ArgumentList.Add("--helper-pid"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            SelfUpdateMigration.Install(plan.Package, oldTarget, plan.Target, plan.Hash, () =>
            {
                using var restarted = Process.Start(start) ?? throw new IOException("Updated GROUT could not restart.");
                WaitForStarted(restarted, folder, plan.Hash);
            });
            File.WriteAllText(Path.Combine(folder, "complete"), "complete"); return 0;
        }
        catch (Exception ex)
        {
            if (folder != null) File.WriteAllText(Path.Combine(folder, "error.txt"), ex.Message);
            if (parentExited && plan != null && plan.TestReport.Length == 0)
            {
                System.Windows.MessageBox.Show("GROUT could not complete its update.\n\n" + ex.Message + "\n\nOpen GROUT again from:\n" + (plan.OriginalTarget.Length > 0 ? plan.OriginalTarget : plan.Target) + "\n\nRecovery details:\n" + folder, "GROUT update needs attention");
            }
            return 1;
        }
    }
    public static SelfUpdatePlan Finish(string planFile, int helperPid)
    {
        var folder = ValidatePlanPath(planFile);
        var plan = JsonSerializer.Deserialize<SelfUpdatePlan>(File.ReadAllText(planFile)) ?? throw new IOException("Invalid update plan.");
        if (!string.Equals(plan.Target, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected self-update restart target.");
        return plan;
    }
    public static void WaitForStarted(Process restarted, string folder, string expectedHash)
    {
        for (var attempt = 0; attempt < 600; attempt++)
        {
            var ack = Path.Combine(folder, "started");
            if (File.Exists(ack) && File.ReadAllText(ack) == expectedHash) return;
            if (restarted.HasExited) throw new IOException("Updated GROUT exited before completing startup. The previous EXE will be restored.");
            Thread.Sleep(100);
        }
        try { restarted.Kill(); restarted.WaitForExit(5000); } catch { }
        throw new IOException("Updated GROUT did not complete startup. The previous EXE will be restored.");
    }
    public static void ConfirmStarted(string planFile, int helperPid)
    {
        var folder = ValidatePlanPath(planFile);
        var plan = Finish(planFile, helperPid);
        var ack = Path.Combine(folder, "started"); File.WriteAllText(ack + ".tmp", plan.Hash); File.Move(ack + ".tmp", ack);
        _ = Task.Run(async () =>
        {
            try { using var helper = Process.GetProcessById(helperPid); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); await helper.WaitForExitAsync(timeout.Token); } catch { }
            if (File.Exists(Path.Combine(folder, "complete")))
            {
                var referencesUpdated = true;
                if (plan.TestReport.Length == 0)
                {
                    try { await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => SelfUpdateMigration.UpdateReferences(plan)); }
                    catch (Exception ex)
                    {
                        referencesUpdated = false;
                        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => System.Windows.MessageBox.Show("GROUT updated, but some startup or shortcut links need updating manually. The old EXE is kept for now.\n\n" + ex.Message, "GROUT shortcut update")));
                    }
                }
                if (referencesUpdated) { try { SelfUpdateMigration.RemoveOriginal(plan); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                StorageCleanup.DeleteFolder(folder, WorkRoot);
            }
        });
    }
}
