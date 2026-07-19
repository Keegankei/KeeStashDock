using System.Drawing;
using System.Windows;
using WF = System.Windows.Forms;
using KeeStashDock.Desktop.Localization;

namespace KeeStashDock.Desktop;

/// <summary>
/// System tray presence - the app's anchor while every window is stowed.
/// Uses WinForms NotifyIcon (ships with the Windows Desktop runtime; no
/// third-party dependency). The context menu is rebuilt on every language
/// switch so tray strings hot-switch together with the rest of the UI.
/// Left click / double click => toggle quick panel.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly WF.NotifyIcon _icon;

    public event Action? ShowPanelRequested;
    public event Action? ShowWorkbenchRequested;
    public event Action? ShowSettingsRequested;
    public event Action? ExitRequested;

    public TrayIconManager()
    {
        _icon = new WF.NotifyIcon
        {
            // Reuse the executable icon so tray and application branding match.
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Visible = true,
            Text = "KeeStashDock",
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WF.MouseButtons.Left) ShowPanelRequested?.Invoke();
        };
        BuildMenu();
        LocalizationService.Instance.LanguageChanged += BuildMenu;
    }

    private void BuildMenu()
    {
        var loc = LocalizationService.Instance;
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add(loc["App.Tray.Show"],      null, (_, _) => ShowPanelRequested?.Invoke());
        menu.Items.Add(loc["App.Tray.Workbench"], null, (_, _) => ShowWorkbenchRequested?.Invoke());
        menu.Items.Add(loc["App.Tray.Settings"],  null, (_, _) => ShowSettingsRequested?.Invoke());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add(loc["App.Tray.Exit"],      null, (_, _) => ExitRequested?.Invoke());
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>Balloon/toast helper (e.g. "copied" confirmations if ever needed from tray).</summary>
    public void ShowInfo(string title, string message)
        => _icon.ShowBalloonTip(2000, title, message, WF.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
