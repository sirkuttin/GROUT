using System.Windows.Controls;
using System.Windows.Input;

namespace ReleaseDock;

internal sealed class MenuDismissal : IDisposable
{
    private ContextMenu? active;
    private readonly MenuClickGate gate = new();
    private Action? close;
    private Action<bool>? showShield;
    internal bool ShieldVisible { get; private set; }
    internal void Connect(Action<bool> visibility) => showShield = visibility;
    internal void Track(Action closeMenu) { close = closeMenu; SetShield(true); }
    private void SetShield(bool visible) { ShieldVisible = visible; showShield?.Invoke(visible); }
    public void Open(ContextMenu menu)
    {
        Dispose(); active = menu; active.StaysOpen = true; active.Closed += Closed; active.PreviewKeyDown += KeyDown;
        menu.AddHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent, new MouseButtonEventHandler(OutsideCaptured), true);
        Track(() => menu.IsOpen = false);
        menu.IsOpen = true;
    }
    private void Closed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is ContextMenu menu) { menu.Closed -= Closed; menu.PreviewKeyDown -= KeyDown; menu.RemoveHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent, new MouseButtonEventHandler(OutsideCaptured)); }
        active = null;
        // Do not uncover controls merely because native popup closure came first.
        // The shield stays until the outside gesture completes (or explicit cleanup).
    }
    private void KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Dispose(); }
    }
    private void OutsideCaptured(object sender, MouseButtonEventArgs e)
    {
        if (ConsumeWindowClick(e.ChangedButton, MouseButtonState.Pressed)) e.Handled = true;
    }
    internal bool ConsumeWindowClick(MouseButton button, MouseButtonState state)
    {
        if (!ShieldVisible) return false;
        // Native capture may deliver only the release to the owner. The shield
        // still owns that event and must not reveal the header beneath it first.
        if (!gate.WaitingForRelease && state == MouseButtonState.Released && active == null) { Dispose(); return true; }
        if (!gate.Consume(true, false, button, state, out var dismiss)) return false;
        if (dismiss) close?.Invoke();
        if (!gate.WaitingForRelease) Dispose();
        return true;
    }
    public void Dispose()
    {
        if (active != null) { active.Closed -= Closed; active.PreviewKeyDown -= KeyDown; active.RemoveHandler(Mouse.PreviewMouseDownOutsideCapturedElementEvent, new MouseButtonEventHandler(OutsideCaptured)); active.IsOpen = false; active = null; }
        close = null; gate.Reset(); SetShield(false);
    }
}

internal sealed class MenuClickGate
{
    private MouseButton? dismissButton;
    public bool WaitingForRelease => dismissButton != null;
    public void Reset() => dismissButton = null;
    public bool Consume(bool menuOpen, bool insideMenu, MouseButton button, MouseButtonState state, out bool dismiss)
    {
        dismiss = false;
        if (dismissButton == button)
        {
            if (state == MouseButtonState.Released) dismissButton = null;
            return true;
        }
        if (!menuOpen || insideMenu || state != MouseButtonState.Pressed) return false;
        dismissButton = button; dismiss = true; return true;
    }
}
