using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using JGlancer.App.Library;
using JGlancer.App.Shell;

namespace JGlancer.App.Developer;

/// <summary>
/// <c>--sidebar-check &lt;log&gt;</c>: the folding sidebar at work (macOS D-128): it folds, comes out over the files, goes,
/// and docks again, the panel's width and its room beside the files sampled along the spring; a press beside the strip
/// must reach the files (macOS MAC-053), one on the sidebar over the files the sidebar. Logs and quits.
/// </summary>
internal static class SidebarCheck
{
    public static void Attach(MainWindow window, LibraryModel model, string log)
    {
        var started = false;
        model.RefsChanged += async (_, _) =>
        {
            if (started)
            {
                return;
            }
            started = true;
            var view = window.LibraryView;
            var lines = new List<string>();
            try
            {
                await Task.Delay(500);
                async Task Sample(string what)
                {
                    var samples = new List<string>();
                    for (var i = 0; i < 25; i++)
                    {
                        samples.Add(string.Create(CultureInfo.InvariantCulture, $"{view.SidebarVisibleWidth:0}/{view.SidebarRoom:0}"));
                        await Task.Delay(25);
                    }
                    lines.Add($"{what}: panel/room " + string.Join(" ", samples) + $" docked={view.IsSidebarDocked} animating={view.IsSidebarAnimating}");
                }
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"start: view width={view.Bounds.Width:0} docked={view.IsSidebarDocked} room={view.SidebarRoom:0}"));
                var beside = new Point(120, 300);
                lines.Add($"docked: hit at 120 reaches sidebar={view.HitsSidebar(beside)}");
                view.ToggleSidebar();
                await Sample("fold");
                lines.Add($"folded: hit at 120 reaches sidebar={view.HitsSidebar(beside)} state.collapsed={model.State.SidebarCollapsed}");
                view.PeekSidebar();
                await Sample("peek");
                lines.Add($"out: hit at 120 reaches sidebar={view.HitsSidebar(beside)}");
                view.LeaveSidebar();
                await Sample("leave");
                view.ToggleSidebar();
                await Sample("dock");
                lines.Add($"docked again: state.collapsed={model.State.SidebarCollapsed}");
                // A window narrower than the old minimum folds it by itself, without changing the remembered choice.
                window.WindowState = Avalonia.Controls.WindowState.Normal;
                var wide = window.Width;
                window.Width = 900;
                await Sample("narrow window");
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"narrow: window={window.Bounds.Width:0} view={view.Bounds.Width:0} docked={view.IsSidebarDocked} state.collapsed={model.State.SidebarCollapsed}"));
                view.ToggleSidebar();
                await Sample("show in narrow window");
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"widened: window={window.Bounds.Width:0} docked={view.IsSidebarDocked}"));
                // The narrowest window (MAC-047): the top bar's controls still fit the files' pane.
                window.Width = JGlancer.Design.JGMetrics.LibraryWindowMinWidth;
                await Task.Delay(600);
                foreach (var bar in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<TopBar>())
                {
                    var controls = bar.FindControl<StackPanel>("Controls")!;
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"narrowest: window={window.Bounds.Width:0} bar={bar.Bounds.Width:0} controls={controls.Bounds.Width:0} right edge={controls.Bounds.Right:0} hidden={string.Join(",", controls.Children.Where(c => !c.IsVisible).Select(c => c.Name))}"));
                }
                window.Width = wide;
                lines.Add("ok");
            }
            catch (Exception exception)
            {
                lines.Add("failed: " + exception);
            }
            await File.WriteAllLinesAsync(log, lines);
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        };
    }
}
