using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MiaoHelper;

/// <summary>托盘驻留主程序:图标 + 右键菜单 + 全局热键 + 引擎生命周期。</summary>
public sealed class TrayContext : ApplicationContext
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 1;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint VK_M = 0x4D;

    private readonly NotifyIcon _tray;
    private readonly Icon _catIcon;
    private readonly HookEngine _engine;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly ToolStripMenuItem _miToggle;
    private readonly ContextMenuStrip _menu;
    private SettingsForm? _settingsForm;

    public TrayContext()
    {
        _engine = new HookEngine();
        _engine.Log += WriteLog;

        _catIcon = CreateCatIcon();
        _tray = new NotifyIcon
        {
            Icon = _catIcon,
            Text = "喵喵助手",
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _miToggle = new ToolStripMenuItem("启用（处理消息）") { Checked = true };
        _miToggle.Click += (_, _) =>
        {
            _engine.Enabled = !_engine.Enabled;
            _miToggle.Checked = _engine.Enabled;
            _tray.ShowBalloonTip(1500, "喵喵助手", _engine.Enabled ? "已启用" : "已停用", ToolTipIcon.Info);
        };

        _menu = new ContextMenuStrip();
        _menu.Items.Add("打开设置", null, (_, _) => OpenSettings());
        _menu.Items.Add(_miToggle);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("立即处理剪贴板", null, (_, _) => _engine.ProcessClipboardNow());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = _menu;

        // 接收全局热键的隐藏窗口
        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += () => _engine.ProcessClipboardNow();
        _hotkeyWindow.Show();
        try { RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_M); }
        catch { }

        _engine.Start();

        _tray.ShowBalloonTip(
            2500, "喵喵助手", "已在后台运行。\n打字停顿或句末出现 。！？ 后自动加喵。\n全局热键 Ctrl+Alt+M 立即处理剪贴板。",
            ToolTipIcon.Info);
    }

    private void OpenSettings()
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_engine);
            _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        }
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private static void WriteLog(string line)
    {
        try
        {
            string path = CatConfig.DebugLogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss} {line}\r\n");
        }
        catch { }
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        try { UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID); } catch { }
        _engine.Dispose();
        _tray.Dispose();
        _catIcon.Dispose();
        _hotkeyWindow.Dispose();
        base.ExitThreadCore();
    }

    // ---------- 热键接收窗口 ----------

    private sealed class HotkeyWindow : Form
    {
        public event Action? HotkeyPressed;

        public HotkeyWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
            ShowIcon = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY) HotkeyPressed?.Invoke();
            base.WndProc(ref m);
        }
    }

    // ---------- 图标 ----------

    private static Icon CreateCatIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillPolygon(Brushes.Orange, new[] { new Point(9, 15), new Point(3, 3), new Point(14, 9) });
            g.FillPolygon(Brushes.Orange, new[] { new Point(23, 15), new Point(29, 3), new Point(18, 9) });
            g.FillEllipse(Brushes.Orange, 5, 8, 22, 20);
            g.FillEllipse(Brushes.Black, 12, 16, 3, 3);
            g.FillEllipse(Brushes.Black, 19, 16, 3, 3);
            g.DrawArc(Pens.Black, 14, 20, 6, 5, 0, 180);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---------- 热键 P/Invoke ----------

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
