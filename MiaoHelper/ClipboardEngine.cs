using System.Runtime.InteropServices;

namespace MiaoHelper;

/// <summary>
/// 剪贴板 + SendInput 按键注入。
/// 因为 Windows 没有无障碍服务,读写任意输入框都经由剪贴板。
/// 快照 = Ctrl+A 全选 + Ctrl+C 复制;写回 = 设剪贴板 + Ctrl+A + Ctrl+V 粘贴覆盖。
/// </summary>
public static class ClipboardEngine
{
    // ---------- 剪贴板 ----------
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr lstrcpyW(IntPtr lpString1, string lpString2);

    // ---------- SendInput ----------
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint INPUT_KEYBOARD = 1;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_A = 0x41;
    private const byte VK_C = 0x43;
    private const byte VK_V = 0x56;

    // INPUT 联合体必须包含 MOUSEINPUT/KEYBDINPUT/HARDWAREINPUT 三者
    // 才能让 Marshal.SizeOf<INPUT>() == 40(x64),否则 SendInput 会以 87 参数无效失败。
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    // ---------- 剪贴板读写 ----------

    public static string? GetClipboardText()
    {
        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                IntPtr hMem = GetClipboardData(CF_UNICODETEXT);
                if (hMem == IntPtr.Zero) return null;
                IntPtr ptr = GlobalLock(hMem);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(hMem); }
            }
            finally { CloseClipboard(); }
        }
        catch { return null; }
    }

    public static void SetClipboardText(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return;
            try
            {
                EmptyClipboard();
                if (text == null || text.Length == 0) return;
                IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)((text.Length + 1) * 2));
                if (hMem == IntPtr.Zero) return;
                IntPtr ptr = GlobalLock(hMem);
                if (ptr == IntPtr.Zero) return;
                lstrcpyW(ptr, text);
                GlobalUnlock(hMem);
                SetClipboardData(CF_UNICODETEXT, hMem); // 交给系统托管内存
            }
            finally { CloseClipboard(); }
        }
        catch { }
    }

    // ---------- 按键注入 ----------

    private static void PressKey(byte vk)
    {
        var down = new INPUT { type = INPUT_KEYBOARD };
        down.U.ki.wVk = vk;
        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
        var up = new INPUT { type = INPUT_KEYBOARD, U = { } };
        up.U.ki.wVk = vk;
        up.U.ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
    }

    private static void SendCtrlCombo(byte vk)
    {
        var ctrlDown = new INPUT { type = INPUT_KEYBOARD };
        ctrlDown.U.ki.wVk = VK_CONTROL;
        SendInput(1, new[] { ctrlDown }, Marshal.SizeOf<INPUT>());
        PressKey(vk);
        var ctrlUp = new INPUT { type = INPUT_KEYBOARD, U = { } };
        ctrlUp.U.ki.dwFlags = KEYEVENTF_KEYUP;
        ctrlUp.U.ki.wVk = VK_CONTROL;
        SendInput(1, new[] { ctrlUp }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// 快照当前活动输入框的文本。
    /// 先写入哨兵值,若 Ctrl+A+C 后剪贴板仍是哨兵,说明没有可复制的文本,返回 null。
    /// </summary>
    public static string? SnapshotActiveText()
    {
        const string sentinel = "__MIAO_SNAPSHOT__";
        string? originalClip = GetClipboardText(); // 保存原始剪贴板内容
        SetClipboardText(sentinel);
        Thread.Sleep(30); // 等待剪贴板写入完成
        SendCtrlCombo(VK_A);
        Thread.Sleep(60); // wait select
        SendCtrlCombo(VK_C);
        Thread.Sleep(60); // wait copy
        
        // 多次检查,等待剪贴板内容变化
        for (int i = 0; i < 15; i++)
        {
            Thread.Sleep(30);
            string? t = GetClipboardText();
            if (t != null && t != sentinel)
            {
                // 读到有效内容,恢复原始剪贴板(延迟恢复,避免影响后续操作)
                return t;
            }
            // 如果连续3次都是哨兵值,说明可能没有可复制的文本
            if (i >= 2 && t == sentinel)
            {
                // 恢复原始剪贴板
                if (originalClip != null) SetClipboardText(originalClip);
                return null;
            }
        }
        // 超时,恢复原始剪贴板
        if (originalClip != null) SetClipboardText(originalClip);
        return null;
    }

    /// <summary>
    /// 把文本写回当前活动输入框(Ctrl+A 全选 + Ctrl+V 粘贴覆盖)。
    /// 若 alreadySelected=true(读取时的全选还在),则直接 Ctrl+V,不重复 Ctrl+A。
    /// </summary>
    public static void WriteBackToActive(string text, bool alreadySelected = false)
    {
        SetClipboardText(text);
        Thread.Sleep(40);
        if (!alreadySelected) SendCtrlCombo(VK_A);
        SendCtrlCombo(VK_V);
        Thread.Sleep(40);
    }

    /// <summary>Ctrl+End:取消全选并把光标移到末尾,快照后不写回时调用,避免下个按键删掉整段。</summary>
    public static void Deselect()
    {
        SendCtrlCombo(0x23); // VK_END
    }

    /// <summary>注入一次按键(按下+抬起),用于重发回车让应用发送处理后的文本。</summary>
    public static void SendKey(byte vk)
    {
        PressKey(vk);
    }

    /// <summary>Ctrl+End:把光标移到输入框末尾(配合 UIA SetValue 用)。</summary>
    public static void PressCtrlEnd()
    {
        SendCtrlCombo(0x23); // VK_END
    }
}
