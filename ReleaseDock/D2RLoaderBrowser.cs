using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReleaseDock;

internal static class D2RLoaderBrowser
{
    internal static string? Show(Window owner, ReleaseClient client, Action<Window>? ready = null)
    {
        var window = Ui.Window("Browse D2RLoader", 760, 700); window.Owner = owner;
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); grid.Children.Add(header); header.Children.Add(Ui.Text("D2RLoader mods and patches", 24));
        var searchRow = new Grid(); searchRow.ColumnDefinitions.Add(new()); searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.Children.Add(searchRow);
        var search = Ui.Input(); search.MaxLength = 200; search.ToolTip = "Search mods and patches"; System.Windows.Automation.AutomationProperties.SetName(search, "Search D2RLoader"); searchRow.Children.Add(search);
        var list = new ListBox { Background = new SolidColorBrush(Color.FromRgb(20, 32, 51)), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 12) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); Grid.SetRow(list, 1); grid.Children.Add(list);
        var selectedBrush = new SolidColorBrush(Color.FromRgb(38, 126, 112)); list.Resources[SystemColors.HighlightBrushKey] = selectedBrush; list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selectedBrush;
        var footer = new StackPanel(); Grid.SetRow(footer, 2); grid.Children.Add(footer); var status = Ui.Text("Loading…", muted: true); footer.Children.Add(status);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var page = 1; var pages = 1; var loading = false; var closed = false; string? selected = null;
        var query = ""; Button? searchButton = null;
        using var cancel = new CancellationTokenSource();
        Button? previous = null, next = null, choose = null;
        async Task Load(int number)
        {
            if (loading || closed) return;
            loading = true; previous!.IsEnabled = next!.IsEnabled = choose!.IsEnabled = searchButton!.IsEnabled = false; status.Text = query.Length > 0 ? "Searching D2RLoader…" : "Loading D2RLoader mods…";
            try
            {
                var result = await client.D2RLoaderCatalog(number, cancel.Token, query); if (closed) return;
                list.Items.Clear(); page = result.Page; pages = result.PageCount;
                foreach (var item in result.Items)
                {
                    var panel = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
                    panel.Children.Add(new TextBlock { Text = item.Name + " · " + item.Type, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    panel.Children.Add(new TextBlock { Text = item.Summary, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                    list.Items.Add(new ListBoxItem { Content = panel, Tag = "https://d2rloader.net/hub/plugins/" + item.Slug });
                }
                status.Text = "Page " + page + " of " + pages + (result.Items.Count == 0 ? " · No matches" : " · Newest first") + (query.Length > 0 ? " · Search: " + query : "");
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "D2RLoader request timed out."; }
            catch (Exception ex) { if (!closed) status.Text = ex.Message; }
            finally { loading = false; previous!.IsEnabled = page > 1; next!.IsEnabled = page < pages; searchButton!.IsEnabled = true; choose!.IsEnabled = list.SelectedItem != null; if (!closed && list.Items.Count > 0) { window.UpdateLayout(); ready?.Invoke(window); } }
        }
        async Task Search()
        {
            if (loading) return;
            query = search.Text.Trim(); page = pages = 1; list.Items.Clear(); await Load(1);
        }
        searchButton = Ui.Button("Search", async (_, _) => await Search()); Grid.SetColumn(searchButton, 1); searchButton.Margin = new Thickness(8, 0, 0, 12); searchRow.Children.Add(searchButton);
        search.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await Search(); } };
        previous = Ui.Button("← Previous", async (_, _) => await Load(page - 1)); buttons.Children.Add(previous);
        next = Ui.Button("Next →", async (_, _) => await Load(page + 1)); next.IsDefault = false; buttons.Children.Add(next);
        buttons.Children.Add(Ui.Button("Refresh", async (_, _) => await Load(page)));
        void Choose() { if (!loading && list.SelectedItem is ListBoxItem { Tag: string link }) { selected = link; window.DialogResult = true; } }
        choose = Ui.Button("Use this mod", (_, _) => Choose()); choose.IsDefault = true; choose.IsEnabled = false; buttons.Children.Add(choose);
        buttons.Children.Add(Ui.Button("Cancel", (_, _) => window.DialogResult = false)); list.SelectionChanged += (_, _) => choose.IsEnabled = !loading && list.SelectedItem != null;
        list.MouseDoubleClick += (_, _) => Choose(); window.Content = grid;
        window.Loaded += async (_, _) => await Load(1); window.Closed += (_, _) => { closed = true; cancel.Cancel(); };
        return window.ShowDialog() == true ? selected : null;
    }
}
