using System.Drawing;
using System.Windows.Forms;

namespace LolScout.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon notifyIcon;
    private bool disposed;

    public TrayIconService(Action show, Action refresh, Action exit)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(exit);
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => show());
        menu.Items.Add("重新查询", null, (_, _) => refresh());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        notifyIcon = new NotifyIcon
        {
            Text = "联盟一区对手战绩助手",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => show();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.ContextMenuStrip?.Dispose();
        notifyIcon.Dispose();
    }
}
