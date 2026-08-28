using System.IO;
using System.Text;
using Microsoft.Win32;

namespace MiaoHelper;

/// <summary>
/// 开机自启动(HKCU 注册表 Run 键 + 静默 VBS 启动器)。
/// 直接注册 exe 会因为缺少 DOTNET_ROOT 找不到自带运行时;注册 bat 会闪黑框。
/// 改为生成 自动启动.vbs 由 wscript 静默执行:设置运行时目录 + 隐藏窗口启动 exe。
/// 全部写入 HKCU,不需要管理员权限。
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "喵喵助手";
    private const string VbsName = "自动启动.vbs";

    /// <summary>程序所在目录(设置窗体运行时即 exe 所在目录)。</summary>
    private static string BaseDir => AppContext.BaseDirectory.TrimEnd('\\', '/');

    public static string VbsPath => Path.Combine(BaseDir, VbsName);

    /// <summary>自启动是否已启用(注册表值存在且指向当前程序目录的 VBS)。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            string? v = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(v) && v.Contains(VbsName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>开启/关闭开机自启动。</summary>
    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key == null) return;
            if (enable)
            {
                WriteVbs();
                // 注册表直接指向 vbs,系统会用 wscript 静默执行,不闪黑框
                key.SetValue(ValueName, "\"" + VbsPath + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
                DeleteVbs();
            }
        }
        catch { }
    }

    /// <summary>生成静默启动器:设 DOTNET_ROOT(有自带运行时才设) + 隐藏窗口启动 exe。</summary>
    private static void WriteVbs()
    {
        string dir = BaseDir;
        string exe = Path.Combine(dir, "MiaoHelper.exe");
        var sb = new StringBuilder();
        sb.AppendLine("Set sh = CreateObject(\"WScript.Shell\")");
        sb.AppendLine($"sh.CurrentDirectory = \"{dir}\"");
        if (Directory.Exists(Path.Combine(dir, "dotnet")))
            sb.AppendLine($"sh.Environment(\"PROCESS\")(\"DOTNET_ROOT\") = \"{dir}\\dotnet\"");
        sb.AppendLine($"sh.Run \"\"\"{exe}\"\"\", 0, False");
        // VBS 用 UTF-16LE(带 BOM),wscript 才能正确解析含中文的路径
        File.WriteAllText(VbsPath, sb.ToString(), Encoding.Unicode);
    }

    private static void DeleteVbs()
    {
        try { if (File.Exists(VbsPath)) File.Delete(VbsPath); } catch { }
    }
}
