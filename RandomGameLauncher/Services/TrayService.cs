using System.Drawing;
using System.Windows.Forms;

namespace RandomGameLauncher.Services;

public sealed class TrayService : IDisposable
{
    readonly NotifyIcon _icon;
    readonly Action _open;
    readonly Action _launchRandom;
    readonly Func<bool> _getFavOnly;
    readonly Action<bool> _setFavOnly;

    public TrayService(string iconPath, Action open, Action launchRandom, Func<bool> getFavOnly, Action<bool> setFavOnly)
    {
        _open = open;
        _launchRandom = launchRandom;
        _getFavOnly = getFavOnly;
        _setFavOnly = setFavOnly;

        _icon = new NotifyIcon
        {
            Visible = true,
            Icon = new Icon(iconPath),
            Text = "Random Game Launcher"
        };

        _icon.DoubleClick += (_, _) => _open();

        var menu = new ContextMenuStrip();

        var launch = new ToolStripMenuItem("Launch Random");
        launch.Click += (_, _) => _launchRandom();
        menu.Items.Add(launch);

        var favOnly = new ToolStripMenuItem("Favorites Only");
        favOnly.CheckOnClick = true;
        favOnly.Checked = _getFavOnly();
        favOnly.CheckedChanged += (_, _) => _setFavOnly(favOnly.Checked);
        menu.Items.Add(favOnly);

        menu.Items.Add(new ToolStripSeparator());

        var openItem = new ToolStripMenuItem("Open");
        openItem.Click += (_, _) => _open();
        menu.Items.Add(openItem);

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                System.Windows.Application.Current.Shutdown());
        menu.Items.Add(exitItem);

        _icon.ContextMenuStrip = menu;
    }

    public void UpdateFavoritesOnlyChecked(bool value)
    {
        if (_icon.ContextMenuStrip is null) return;
        foreach (ToolStripItem item in _icon.ContextMenuStrip.Items)
        {
            if (item is ToolStripMenuItem mi && mi.Text == "Favorites Only")
            {
                mi.Checked = value;
                return;
            }
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
