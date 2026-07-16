using System.Drawing;
using System.Windows.Forms;

namespace LolScout.App.Services;

public interface ITrayMenu : IDisposable
{
    object NativeMenu { get; }
    void Add(string text, Action action);
    void AddAsync(string text, Func<Task> action);
    void AddSeparator();
}

public interface ITrayIcon : IDisposable
{
    void Configure(object menu, Action show);
    void Hide();
}

public interface ITrayPlatformFactory
{
    ITrayMenu CreateMenu();
    ITrayIcon CreateIcon();
}

public sealed class TrayIconService : IDisposable
{
    private readonly ITrayMenu menu;
    private readonly ITrayIcon icon;
    private bool disposed;

    public TrayIconService(Action show, Action refresh, Func<Task> exit)
        : this(show, refresh, exit, new WinFormsTrayPlatformFactory()) { }

    public TrayIconService(Action show, Action refresh, Func<Task> exit, ITrayPlatformFactory factory)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(factory);
        ITrayMenu? createdMenu = null;
        ITrayIcon? createdIcon = null;
        try
        {
            createdMenu = factory.CreateMenu();
            createdMenu.Add("显示", show);
            createdMenu.Add("重新查询", refresh);
            createdMenu.AddSeparator();
            createdMenu.AddAsync("退出", exit);
            createdIcon = factory.CreateIcon();
            createdIcon.Configure(createdMenu.NativeMenu, show);
            menu = createdMenu;
            icon = createdIcon;
        }
        catch
        {
            createdIcon?.Dispose();
            createdMenu?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        icon.Hide();
        icon.Dispose();
        menu.Dispose();
    }

    private sealed class WinFormsTrayPlatformFactory : ITrayPlatformFactory
    {
        public ITrayMenu CreateMenu() => new WinFormsMenu();
        public ITrayIcon CreateIcon() => new WinFormsIcon();
    }

    private sealed class WinFormsMenu : ITrayMenu
    {
        private readonly ContextMenuStrip value = new();
        public object NativeMenu => value;
        public void Add(string text, Action action) => value.Items.Add(text, null, (_, _) => action());
        public void AddAsync(string text, Func<Task> action) => value.Items.Add(text, null, async (_, _) => await action());
        public void AddSeparator() => value.Items.Add(new ToolStripSeparator());
        public void Dispose() => value.Dispose();
    }

    private sealed class WinFormsIcon : ITrayIcon
    {
        private readonly NotifyIcon value = new();
        public void Configure(object menu, Action show)
        {
            value.Text = "联盟一区对手战绩助手";
            value.Icon = SystemIcons.Application;
            value.ContextMenuStrip = (ContextMenuStrip)menu;
            value.DoubleClick += (_, _) => show();
            value.Visible = true;
        }
        public void Hide() => value.Visible = false;
        public void Dispose() => value.Dispose();
    }
}
