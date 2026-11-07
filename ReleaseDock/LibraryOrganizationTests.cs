using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ReleaseDock;

public static class LibraryOrganizationTests
{
    public static int Assertions { get; private set; }
    private static void Check(bool value, string reason) { Assertions++; if (!value) throw new Exception(reason); }
    private static IEnumerable<T> Find<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) { if (child is T value) yield return value; foreach (var descendant in Find<T>(child)) yield return descendant; }
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("releases/latest") ? """{"tag_name":"v2","assets":[]}""" : "{}") });
    }
    public static async Task Run()
    {
        Assertions = 0;
        var branch = new TrackedApp { IsGroup = true, CustomName = "D2R", Expanded = false };
        var installedParent = new TrackedApp { Repo = "test/parent", ParentId = branch.Id, Installed = "1.0", Latest = "1.0", Expanded = false };
        var available = new TrackedApp { Repo = "test/plugin", ParentId = installedParent.Id, Installed = "1.0", Latest = "2.0", Release = new("2.0", "", "", []) };
        var current = new TrackedApp { Repo = "test/current", ParentId = installedParent.Id, Installed = "1.0", Latest = "1.0" };
        var summaryNodes = new[] { branch, installedParent, available, current };
        LibraryTree.Prepare(summaryNodes, false, "");
        Check(branch.LibraryStatus == "1 update" && installedParent.LibraryStatus == "1 update" && branch.BranchItemCount == 3, "Collapsed parents summarize all nested tracked items without counting organizers");
        Check(installedParent.DisplayStatus == "Up to date", "Selected parent details keep their own status rather than claiming a child's update");
        available.Latest = "1.0"; available.Release = new("1.0", "", "", []); LibraryTree.Prepare(summaryNodes, false, "");
        Check(branch.LibraryStatus == "All up to date", "A fully current collapsed branch has a positive summary");
        branch.Expanded = true; LibraryTree.Prepare(summaryNodes, false, "");
        Check(branch.LibraryStatus.Length == 0, "Expanded organizers hide the branch summary");
        branch.Expanded = false;
        installedParent.Latest = available.Latest = "2.0"; installedParent.Release = available.Release = new("2.0", "", "", []); LibraryTree.Prepare(summaryNodes, false, "");
        Check(branch.LibraryStatus == "2 updates", "Branch count includes a tracked parent's own update and its descendant update once each");
        current.Error = "Update check failed"; LibraryTree.Prepare(summaryNodes, true, "nothing matches");
        Check(branch.LibraryStatus == "2 updates · 1 issue" && branch.LibraryStatusColor == "#F0D495", "Hidden errors remain visible in summaries independently of search/update filtering");
        installedParent.Latest = available.Latest = "1.0"; installedParent.Release = available.Release = new("1.0", "", "", []); LibraryTree.Prepare(summaryNodes, false, "");
        Check(branch.LibraryStatus == "1 issue", "Failed checks are not incorrectly summarized as all up to date");
        var old = Json.JsonSerializer.Deserialize<TrackedApp>("""{"Repo":"https://d2rloader.net/hub/plugins/remote-stash","Installed":"2.3.4"}""")!;
        Check(old.IsMod && old.TargetLabel.Length == 0 && old.Installed == "2.3.4", "Existing D2R entries gain mod context without inventing a target or changing version");
        var first = new TrackedApp { Repo = "test/mod", ItemKind = "Mod", TargetName = "D2R Reimagined", AppFolder = @"D:\Games\Reimagined\stash", Installed = "v1", Latest = "v2", Release = new("v2", "", "", []) };
        var second = new TrackedApp { Repo = first.Repo, ItemKind = "Mod", TargetName = "D2R Vanilla", AppFolder = @"D:\Games\Vanilla\stash", Installed = "v2", Latest = "v2" };
        Check(!LibraryOrganization.SameInstallation(first, second), "Same mod source can have independent destinations");
        Check(LibraryOrganization.ModDestination(first.AppFolder) == first.AppFolder, "Mod ZIP uses the exact selected destination without adding a nested app folder");
        try { LibraryOrganization.ModDestination("relative-folder"); throw new Exception("Relative mod destination accepted"); } catch (InvalidOperationException) { Assertions++; }
        second.AppFolder = first.AppFolder + "\\";
        Check(LibraryOrganization.SameInstallation(first, second), "Renaming the target does not evade duplicate destination detection");
        Check(LibraryOrganization.Matches(first, "Updates", "Mods", "D2R Reimagined", "stash") == false && LibraryOrganization.Matches(first, "Updates", "Mods", "D2R Reimagined", "mod"), "Update, type, target and source search filters combine");
        Check(!LibraryOrganization.Matches(second, "Updates", "Mods", "", ""), "Current installation excluded from Updates even when another installation needs it");
        var main = new MainWindow(true);
        try
        {
            main.PreviewModLibrary();
            Check(!Find<Button>(main).Any(b => (b.Content as string ?? "").Contains("Browse D2RLoader")), "D2RLoader catalog discovery is hidden from the main public interface");
            var list = (ListBox)main.FindName("Library"); var all = list.Items.Cast<TrackedApp>().ToList();
            var group = all.Single(n => n.IsGroup); var parent = all.Single(n => n.AppName == "Reimagined");
            Check(all.Count == 5 && parent.ParentId == group.Id && all.Count(n => n.ParentId == parent.Id) == 2, "User-managed tree has an updatable Reimagined parent and its two mods");
            Check(!LibraryTree.CanParent(group, parent, all) && !LibraryTree.CanParent(parent, parent, all), "Tree rejects self-parenting and descendant cycles");
            parent.Expanded = false; LibraryTree.Prepare(all, false, ""); Check(!all.First(n => n.ParentId == parent.Id).TreeVisible, "Collapsed parent hides children without changing their paths");
            parent.Expanded = true;
            var updates = (CheckBox)main.FindName("UpdatesOnlyFilter"); updates.IsChecked = true; updates.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Check(list.Items.Count == 4 && list.Items.Cast<TrackedApp>().Any(n => n.AppName == "Reimagined") && !list.Items.Cast<TrackedApp>().Any(n => n.AppName == "Auto Deposit"), "Updates-only includes actionable leaves and necessary ancestors");
            Check(LibraryTree.OverlapsOtherInstallation(parent, @"D:\Games\D2R", all), "Parent folder removal recognizes tracked child installations");
            var path = all.First(n => n.ParentId == parent.Id).AppFolder; LibraryTree.RemoveKeepingChildren(all, parent);
            Check(all.Where(n => n.IsMod).All(n => n.ParentId == group.Id) && all.Any(n => n.AppFolder == path), "Removing parent promotes children without moving installed files");
        }
        finally { main.Close(); }
        using var client = new ReleaseClient(new Handler());
        var defaultPreview = new SetupWindow(client, [], preview: true, previewStage: "mod", trackedApps: [first], parentId: first.Id, childDefaultFolder: @"D:\Games\Default");
        try { Check(Find<TextBox>(defaultPreview).Any(t => t.Text == @"D:\Games\Default") && Find<TextBlock>(defaultPreview).Any(t => t.Text.Contains("Add to: ") && t.Text.Contains(first.AppName)), "Adding a new child shows parent context and uses the parent folder only as a visible default"); }
        finally { defaultPreview.Close(); }

        var wizard = new SetupWindow(client, [first.Repo], trackedApps: [first], mod: true, parentId: first.Id, childDefaultFolder: @"D:\Games\Default");
        try
        {
            Check(!Find<Button>(wizard).Any(b => Equals(b.Content, "Browse D2RLoader…")), "Provider browser removed from general Add screen");
            Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Project link").Text = first.Repo;
            Find<Button>(wizard).Single(b => Equals(b.Content, "Next →")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && !Find<Button>(wizard).Any(b => Equals(b.Content, "Add mod")); i++) await Task.Delay(20);
            Check(wizard.ExistingRepo.Length == 0 && wizard.Result.IsMod && wizard.Result.ParentId == first.Id && Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "App folder").Text.Length == 0, "Adding another mod installation reaches target setup rather than opening first source entry");
            wizard.ShowActivated = false; wizard.WindowStartupLocation = WindowStartupLocation.Manual; wizard.Left = -20000; wizard.Top = -20000;
            _ = wizard.Dispatcher.InvokeAsync(() =>
            {
                Find<TextBox>(wizard).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "App folder").Text = @"D:\Games\Vanilla\stash";
                Find<Button>(wizard).Single(b => Equals(b.Content, "Add mod")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; timeout.Tick += (_, _) => { timeout.Stop(); wizard.Close(); }; timeout.Start();
            try { Check(wizard.ShowDialog() == true && wizard.Result.ParentId == first.Id && wizard.Result.AppFolder == @"D:\Games\Vanilla\stash", "Real Add mod flow saves a second source instance with its chosen target and destination"); }
            finally { timeout.Stop(); }
        }
        finally { wizard.Close(); }
        var root = Path.Combine(Path.GetTempPath(), "GROUT-library-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            second.AppFolder = @"D:\Games\Vanilla\stash";
            var store = new Store(Path.Combine(root, "apps.json")); store.Save([first, second]); var restored = store.Load();
            Check(restored.Count == 2 && restored[0].TargetName != restored[1].TargetName && restored[0].Installed != restored[1].Installed, "Independent source installations and target names survive restart");
        }
        finally { Directory.Delete(root, true); }
    }
}
