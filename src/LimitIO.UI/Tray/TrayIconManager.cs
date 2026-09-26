using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using LimitIO.Core.Ipc;
using LimitIO.UI.Diagnostics;
using LimitIO.UI.Ipc;
using LimitIO.UI.Views;
using Application = System.Windows.Application;

namespace LimitIO.UI.Tray;

/// <summary>
/// Owns the system tray icon and its context menu: Open Settings (password-gated), a per-rule "ignore
/// limit for 1 minute" submenu for whatever is currently blocked, and About. WPF has no native tray API,
/// so this uses <see cref="NotifyIcon"/> from WinForms - the standard, well-worn way to get a tray icon
/// out of a WPF app.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly ServiceClient _client;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _ignoreSubmenu;

    public TrayIconManager(ServiceClient client)
    {
        _client = client;

        _menu = new System.Windows.Forms.ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open LimitIO...");
        openItem.Click += (_, _) => UiSafe.Run(OpenSettingsAsync);
        _menu.Items.Add(openItem);

        _ignoreSubmenu = new ToolStripMenuItem("Ignore limit for 1 minute");
        _menu.Items.Add(_ignoreSubmenu);

        _menu.Items.Add(new ToolStripSeparator());

        var aboutItem = new ToolStripMenuItem("About LimitIO");
        aboutItem.Click += (_, _) => ShowAbout();
        _menu.Items.Add(aboutItem);

        _menu.Opening += (_, _) => UiSafe.Run(RefreshIgnoreSubmenuAsync);

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Shield,
            Visible = true,
            Text = "LimitIO",
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => UiSafe.Run(OpenSettingsAsync);
    }

    private async Task RefreshIgnoreSubmenuAsync()
    {
        // ToolStripItemCollection.Clear() only removes items from the collection - it does not Dispose()
        // them, so the native GDI/USER handles each ToolStripMenuItem holds leak on every single menu
        // open unless disposed explicitly here first. Left unfixed, this leaks a little on every tray
        // icon click (even when there's nothing to show) and, over enough uses, exhausts the process's
        // handle quota - which is exactly what "the app eventually becomes unresponsive and gets closed"
        // looks like from the outside.
        foreach (ToolStripItem item in _ignoreSubmenu.DropDownItems)
        {
            item.Dispose();
        }
        _ignoreSubmenu.DropDownItems.Clear();

        var status = await _client.GetStatusAsync().ConfigureAwait(true);
        var blockedRules = status?.Rules.Where(r => r.Blocked && r.GraceAvailable).ToList() ?? [];

        if (blockedRules.Count == 0)
        {
            _ignoreSubmenu.Enabled = false;
            _ignoreSubmenu.DropDownItems.Add(new ToolStripMenuItem("(nothing currently blocked)") { Enabled = false });
            return;
        }

        _ignoreSubmenu.Enabled = true;
        foreach (var rule in blockedRules)
        {
            var item = new ToolStripMenuItem(rule.Target);
            item.Click += (_, _) => UiSafe.Run(async () =>
            {
                var result = await _client.RequestGraceAsync(rule.Id).ConfigureAwait(true);
                if (!result.Success)
                {
                    System.Windows.MessageBox.Show(result.Reason ?? "Couldn't grant the one-minute ignore.",
                        "LimitIO", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            });
            _ignoreSubmenu.DropDownItems.Add(item);
        }
    }

    private async Task OpenSettingsAsync()
    {
        var status = await _client.GetStatusAsync().ConfigureAwait(true);
        if (status is null)
        {
            System.Windows.MessageBox.Show(
                "Couldn't reach the LimitIO service. It may not be running - try reinstalling LimitIO.",
                "LimitIO", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (!status.PasswordConfigured)
        {
            var wizard = new FirstRunSetupWizard(_client);
            wizard.ShowDialog();
            return;
        }

        var prompt = new PasswordPromptWindow(_client);
        if (prompt.ShowDialog() == true && prompt.SessionToken is not null)
        {
            var settings = new SettingsWindow(_client, prompt.SessionToken);
            settings.ShowDialog();
        }
    }

    private static void ShowAbout()
    {
        System.Windows.MessageBox.Show(
            "LimitIO - a self-imposed screen-time limiter for Windows.\n\n" +
            "Not a substitute for account-level separation: see the README for the honest tamper-resistance " +
            "notes before relying on this for someone who could plausibly reach an admin account.",
            "About LimitIO", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }
}
