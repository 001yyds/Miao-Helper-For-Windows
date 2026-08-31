using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MiaoHelper;

/// <summary>
/// 处理引擎(UIA + 剪贴板混合):只观察按键,绝不拦截/吞掉任何键。
/// 优先 UIA 读(无闪屏);UIA 拿不到(QQ/微信等)则退回剪贴板快照。
/// 剪贴板快照会全选文本——不写回时必须取消全选,否则用户下一个按键会删除整段。
/// </summary>
public sealed class HookEngine : IDisposable
{
    private const int MAX_SNAPSHOT_LEN = 500;
    private const long WRITE_ECHO_WINDOW_MS = 600;
    private const int DEBOUNCE_FAST = 200;       // 检测到句末标点(。！？)后,快速检查
    private const int DEBOUNCE_SLOW = 1200;      // 其他按键:长防抖兜底,平时不闪
    private const int UIA_CACHE_RESET_MS = 30000; // UIA缓存重置间隔(30秒)

    private readonly KeyboardHook _hook;
    private readonly System.Windows.Forms.Timer _debounce;
    private readonly System.Windows.Forms.Timer _uiaCacheReset; // UIA缓存重置定时器
    private CatConfig _config;
    private bool _isProcessing;
    private bool _enabled = true;
    private bool _punctTrigger;   // 本次防抖是否由句末标点键触发
    private int _retryCount;
    private string _userOriginal = "";
    private string _lastSet = "";
    private long _lastWriteTime;
    private IntPtr _lastForeground = IntPtr.Zero;
    private IntPtr _uiaOffHwnd = IntPtr.Zero; // 已知 UIA 读不到的窗口,直接走剪贴板

    /// <summary>调试日志(托盘写 debug.log)。</summary>
    public event Action<string>? Log;

    public HookEngine()
    {
        _config = CatConfig.Load();
        _hook = new KeyboardHook();
        _hook.Key += OnKey;
        _debounce = new System.Windows.Forms.Timer { Interval = DEBOUNCE_FAST };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            bool retry = DoCheck();
            if (retry && _retryCount < 2)
            {
                _retryCount++;
                _debounce.Interval = 250;
                _debounce.Start();
            }
        };
        
        // UIA缓存重置定时器:定期清除缓存,让之前读不到UIA的窗口有机会重新尝试
        _uiaCacheReset = new System.Windows.Forms.Timer { Interval = UIA_CACHE_RESET_MS };
        _uiaCacheReset.Tick += (_, _) =>
        {
            if (_uiaOffHwnd != IntPtr.Zero)
            {
                Log?.Invoke("UIA缓存重置:清除窗口标记,下次将重新尝试UIA读取");
                _uiaOffHwnd = IntPtr.Zero;
            }
        };
    }

    public CatConfig Config => _config;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) { _debounce.Stop(); _userOriginal = ""; _lastSet = ""; }
        }
    }

    public void ReloadConfig() => _config = CatConfig.Load();

    public void Start()
    {
        try
        {
            _hook.Install();
            _uiaCacheReset.Start(); // 启动UIA缓存重置定时器
            // 预热 UIA,避免第一次读取时的延迟
            try { _ = UiaHelper.ReadFocusedText(); } catch { }
            Log?.Invoke($"启动完成。模式={_config.ProcessingMode} 追加={_config.EnableAppend}(\"{_config.AppendText}\") 颜文字={_config.EnableRandomEmoticon} 规则数={_config.Rules?.Count ?? 0}");
        }
        catch (Exception ex) { Log?.Invoke("键盘钩子安装失败: " + ex.Message); }
    }

    public void Stop()
    {
        _hook.Uninstall();
        _uiaCacheReset.Stop();
    }

    // ---------- 键盘事件(只观察) ----------

    private void OnKey(ushort vk, uint scan, bool isDown)
    {
        if (!isDown || !_enabled || _isProcessing) return;

        // 前台窗口变化时重置增量状态
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd != _lastForeground)
        {
            _lastForeground = hwnd;
            if (_lastSet.Length > 0 || _userOriginal.Length > 0)
            {
                _userOriginal = "";
                _lastSet = "";
                Log?.Invoke("前台窗口变化,重置增量状态");
            }
        }

        if (!IsTextRelevantKey(vk)) return;

        bool shiftNow = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        bool realtime = _config.ProcessingMode == CatConfig.MODE_REALTIME;
        bool fast = KeyProducesSentenceEnd(vk, scan);
        // 标点模式:只有句末标点(。！？)才触发,平时完全不碰输入框(不闪);
        // 实时模式:任意文本键,停顿后处理
        if (!realtime && !fast) return;

        _retryCount = 0;
        _punctTrigger = fast;
        _debounce.Stop();
        _debounce.Interval = fast ? DEBOUNCE_FAST : DEBOUNCE_SLOW;
        _debounce.Start();
        if (fast) Log?.Invoke($"句末标点键 vk=0x{vk:X2} shift={shiftNow} → 快速检查");
    }

    /// <summary>
    /// 该键是否会产生句末标点(。！？!?)。Shift 状态用 GetAsyncKeyState 实时读取,
    /// 再按实际键盘布局 ToUnicodeEx 翻译兜底,兼容不同输入法。
    /// </summary>
    private bool KeyProducesSentenceEnd(ushort vk, uint scan)
    {
        string triggerPunct = _config.TriggerPunctuation;
        if (string.IsNullOrEmpty(triggerPunct)) return false;

        bool shiftNow = (GetAsyncKeyState(0x10) & 0x8000) != 0;

        // 针对单个按键就能打出的常见标点做 VK 快速判断（不用 ToUnicodeEx 兜底）
        // 句号键 vk=0xBE:中文输入法打出全角'。'(快速触发);英文输入模式打出半角'.'。
        // 若英文模式也按'。'快速触发,会"快速检查→文本未以触发符号结尾→重试3次",
        // 每次重试都做剪贴板快照+取消全选,闪3下很烦。
        // 修复:vk=0xBE 快速触发仅在中文输入法开启时生效,英文模式落到 ToUnicodeEx 兜底。
        if (vk == 0xBE && IsImeActive())
        {
            if (triggerPunct.IndexOf('。') >= 0) return true;
            if (triggerPunct.IndexOf('.') >= 0) return true;
        }
        // 问号键 vk=0xBF:Shift+? 或中文模式下'？'
        if (vk == 0xBF && shiftNow && triggerPunct.IndexOf('?') >= 0) return true;
        if (vk == 0xBF && !shiftNow && IsImeActive() && triggerPunct.IndexOf('？') >= 0) return true;

        // ToUnicodeEx 兜底：翻译按键，看打出的字符是否在触发符号里
        try
        {
            byte[] state = new byte[256];
            if (shiftNow) state[0x10] = 0x80;
            if (GetAsyncKeyState(0x11) < 0) state[0x11] = 0x80;
            if (GetAsyncKeyState(0x12) < 0) state[0x12] = 0x80;
            var sb = new StringBuilder(8);
            int r = ToUnicodeEx(vk, scan, state, sb, 8, 0, IntPtr.Zero);
            if (r > 0)
            {
                for (int i = 0; i < sb.Length; i++)
                {
                    char c = sb[i];
                    if (triggerPunct.IndexOf(c) >= 0)
                    {
                        Log?.Invoke($"ToUnicodeEx 识别触发符号 '{c}' vk=0x{vk:X2}");
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>可能改变文本的键:退格/制表/回车/空格/删除/数字/字母/小键盘/OEM/IME 键。</summary>
    private static bool IsTextRelevantKey(ushort vk)
    {
        if (vk == 0x08 || vk == 0x09 || vk == 0x0D || vk == 0x20 || vk == 0x2E) return true;
        if (vk >= 0x30 && vk <= 0x39) return true;
        if (vk >= 0x41 && vk <= 0x5A) return true;
        if (vk >= 0x60 && vk <= 0x6F) return true;
        if (vk >= 0xBA && vk <= 0xE7) return true;
        return false;
    }

    // ---------- 处理 ----------

    private bool DoCheck()
    {
        if (!_enabled || _isProcessing) return false;
        Log?.Invoke("检查: 开始");
        if (IsImeComposing())
        {
            Log?.Invoke("检查: 输入法组词中,跳过");
            return false;
        }
        if (IsOurWindow()) { Log?.Invoke("检查: 焦点在本程序,跳过"); return false; }

        _isProcessing = true;
        string? originalClip = ClipboardEngine.GetClipboardText();
        bool usedClipboard = false;
        bool needDeselect = false;
        try
        {
            IntPtr curHwnd = GetForegroundWindow();
            bool skipUia = (curHwnd != IntPtr.Zero && curHwnd == _uiaOffHwnd);

            string? box = null;
            if (!skipUia)
            {
                box = UiaHelper.ReadFocusedText();
                if (box != null) Log?.Invoke($"检查: UIA 读取 {box.Length} 字");
                // UIA 读到空或整个聊天记录(超长)→ 弃用
                if (box != null && (box.Length == 0 || box.Length > MAX_SNAPSHOT_LEN)) box = null;
                if (box == null)
                {
                    // 本窗口 UIA 读不到,标记后直接走剪贴板(避免每次都白等)
                    _uiaOffHwnd = curHwnd;
                }
            }
            if (box == null)
            {
                box = ClipboardEngine.SnapshotActiveText();
                usedClipboard = true;
                // 剪贴板快照 Ctrl+A+C 会留下全选;若读到非空文本,后续需要取消全选
                needDeselect = !string.IsNullOrEmpty(box);
                if (box != null) Log?.Invoke($"检查: 剪贴板读取 {box.Length} 字");
            }
            if (box == null)
            {
                Log?.Invoke("检查: 读取失败(UIA+剪贴板均拿不到文本),跳过");
                Cleanup(usedClipboard, needDeselect, originalClip);
                return _punctTrigger; // 标点触发时重试,等字符上屏
            }

            string text = box.Trim();
            if (text.Length == 0)
            {
                _userOriginal = "";
                _lastSet = "";
                Cleanup(usedClipboard, needDeselect, originalClip);
                return _punctTrigger; // 标点触发时重试,等字符上屏
            }
            if (text.Length > MAX_SNAPSHOT_LEN)
            {
                Log?.Invoke($"检查: 文本过长({text.Length}字),疑似误选中页面,跳过");
                Cleanup(usedClipboard, needDeselect, originalClip);
                return false;
            }

            bool realtime = _config.ProcessingMode == CatConfig.MODE_REALTIME;
            bool endsWithPunct = TextProcessor.IsPunctuationEnding(text, _config.TriggerPunctuation);

            // 标点模式:句末标点还没出现 → 稍后重试(标点可能刚按还没上屏)
            if (!realtime && !endsWithPunct)
            {
                Log?.Invoke($"检查: 未以句末标点结尾([{text}]),跳过");
                Cleanup(usedClipboard, needDeselect, originalClip);
                return _retryCount < 2;
            }

            // 写回后的回显保护
            long now = Environment.TickCount64;
            if (_lastWriteTime > 0 && now - _lastWriteTime < WRITE_ECHO_WINDOW_MS && text == _lastSet)
            {
                _lastWriteTime = 0;
                Cleanup(usedClipboard, needDeselect, originalClip);
                return false;
            }

            // 增量:当前文本以 lastSet 开头 → 只取新增部分并入 userOriginal(原始文本)
            if (_lastSet.Length > 0 && text.StartsWith(_lastSet))
            {
                _userOriginal += text.Substring(_lastSet.Length);
            }
            else
            {
                // 文本不是在上次写回结果上继续输入(用户删改过已处理文本/窗口切换/重启等)。
                // 必须还原成"用户原始文本":StripAll 只去旧颜文字,会残留已追加的"喵",
                // 再跑 Process 就会每句再叠一个"喵"(如 你好喵 → 你好喵喵)。
                // ReclaimRawText = 去颜文字 + 去每句末尾已追加文本,与 Process 互逆。
                _userOriginal = TextProcessor.ReclaimRawText(text, _config);
            }

            if (_userOriginal.Length == 0)
            {
                Log?.Invoke("检查: 用户原文为空,跳过");
                Cleanup(usedClipboard, needDeselect, originalClip);
                return false;
            }

            if (!PlausibleMessage(text, endsWithPunct, realtime))
            {
                Log?.Invoke("检查: 文本不像聊天消息(无中文、无句末标点、含盘符路径),跳过");
                Cleanup(usedClipboard, needDeselect, originalClip);
                return false;
            }

            // 实时模式:句子未完成(不以句末标点结尾)时追加 喵,但不加随机颜文字
            CatConfig cfg = _config;
            if (realtime && !endsWithPunct) cfg = CloneWithoutEmoticon(cfg);

            string target = TextProcessor.Process(_userOriginal, cfg);

            if (target == text)
            {
                _lastSet = target;
                Cleanup(usedClipboard, needDeselect, originalClip);
                return false;
            }

            Log?.Invoke($"[{_config.ProcessingMode}] 读取=[{text}] 用户原文=[{_userOriginal}] → 目标=[{target}]");

            string? writeFail = null;
            bool needClipboardWrite = skipUia; // 窗口已标记走剪贴板 → 直接剪贴板写
            if (!skipUia)
            {
                writeFail = UiaHelper.WriteFocusedText(target);
                needClipboardWrite = writeFail != null;
            }
            if (needClipboardWrite)
            {
                Log?.Invoke("写入走剪贴板(" + (writeFail ?? "UIA 已标记跳过") + ")");
                // 始终 Ctrl+A+V 全选替换,保证在网页输入框(全选可能不保持)里也能替换成功
                ClipboardEngine.WriteBackToActive(target);
            }
            
            // 写回后验证:检查是否真的写入成功
            Thread.Sleep(80); // 等待写入完成
            string? verifyText = null;
            if (!skipUia)
            {
                verifyText = UiaHelper.ReadFocusedText();
            }
            if (verifyText == null && usedClipboard)
            {
                // UIA读不到,用剪贴板快照验证
                verifyText = ClipboardEngine.SnapshotActiveText();
                needDeselect = true; // 剪贴板快照会全选
            }
            
            if (verifyText != null && verifyText.Trim() != target.Trim())
            {
                Log?.Invoke($"写回验证失败:期望=[{target.Trim()}] 实际=[{verifyText.Trim()}],可能写入被拒绝");
                // 写入失败,不更新_lastSet,下次重试
                Cleanup(usedClipboard, needDeselect, originalClip);
                return false;
            }
            
            _lastSet = target;
            _lastWriteTime = Environment.TickCount64;
            Thread.Sleep(40);
            if (usedClipboard) RestoreClipboard(originalClip);
            return false;
        }
        catch (Exception ex)
        {
            Log?.Invoke("处理异常: " + ex);
            Cleanup(usedClipboard, needDeselect, originalClip);
            return false;
        }
        finally { _isProcessing = false; }
    }

    /// <summary>快照若用了剪贴板(Ctrl+A+C),文本会处于全选状态;不写回时必须取消全选,避免下一个按键删掉整段。</summary>
    private void Cleanup(bool usedClipboard, bool needDeselect, string? originalClip)
    {
        try
        {
            if (usedClipboard && needDeselect) ClipboardEngine.Deselect();
        }
        catch { }
        RestoreClipboard(originalClip);
    }

    /// <summary>文本是否像一条聊天消息:含中文、或以句末标点结尾、或含常见聊天词汇。挡掉文件路径/网页误选中。</summary>
    private static bool PlausibleMessage(string text, bool endsWithPunct, bool realtime)
    {
        if (endsWithPunct) return true;
        if (text.Contains(":\\", StringComparison.OrdinalIgnoreCase)) return false; // C:\ 盘符路径
        
        // 检查是否包含中文字符
        foreach (char c in text)
        {
            // CJK 统一表意文字 / 扩展A / 兼容表意文字
            if ((c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF') || (c >= '\uF900' && c <= '\uFAFF'))
                return true;
        }
        
        // 实时模式下,允许纯英文消息(至少包含一个空格,像聊天内容)
        if (realtime && text.Contains(' ') && text.Length >= 3)
        {
            // 排除明显的路径或URL
            if (text.Contains('/') || text.Contains('\\') || text.Contains("://"))
                return false;
            return true;
        }
        
        return false;
    }

    /// <summary>手动处理当前剪贴板文本(Ctrl+Alt+M 热键 / 托盘菜单),处理后写回剪贴板。</summary>
    public void ProcessClipboardNow()
    {
        if (!_enabled) return;
        string? s = ClipboardEngine.GetClipboardText();
        if (string.IsNullOrWhiteSpace(s)) { Log?.Invoke("剪贴板无文本,忽略"); return; }
        // 先去旧喵/旧颜文字,再处理,保证重复处理不叠字
        string raw = TextProcessor.ReclaimRawText(s.Trim(), _config);
        string target = TextProcessor.Process(raw, _config);
        ClipboardEngine.SetClipboardText(target);
        Log?.Invoke($"剪贴板处理: [{s.Trim()}] → [{target}]");
    }

    private static CatConfig CloneWithoutEmoticon(CatConfig src) => new CatConfig
    {
        EnableAppend = src.EnableAppend,
        AppendText = src.AppendText,
        EnableRandomEmoticon = false,
        ProcessingMode = src.ProcessingMode,
        Rules = src.Rules,
        CustomEmoticons = src.CustomEmoticons,
    };

    private static void RestoreClipboard(string? originalClip)
    {
        if (originalClip != null) ClipboardEngine.SetClipboardText(originalClip);
    }

    // ---------- 原生调用 ----------

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    private static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [DllImport("imm32.dll")]
    private static extern int ImmGetCompositionString(IntPtr hIMC, uint dwIndex, IntPtr lpBuf, uint dwBufLen);

    [DllImport("imm32.dll")]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    /// <summary>取前台窗口真正聚焦控件的输入法上下文(hFocus, imc)。AttachThreadInput 保证跨线程取到正确的 GetFocus。</summary>
    private static (IntPtr hFocus, IntPtr imc) GetImeContext()
    {
        try
        {
            IntPtr hFore = GetForegroundWindow();
            if (hFore == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero);
            uint fgTid = GetWindowThreadProcessId(hFore, out _);
            uint myTid = GetCurrentThreadId();

            bool attached = false;
            if (fgTid != myTid) attached = AttachThreadInput(myTid, fgTid, true);
            IntPtr hFocus = GetFocus();
            if (attached) AttachThreadInput(myTid, fgTid, false);
            if (hFocus == IntPtr.Zero) hFocus = hFore;

            return (hFocus, ImmGetContext(hFocus));
        }
        catch { return (IntPtr.Zero, IntPtr.Zero); }
    }

    /// <summary>当前前台输入法是否开启(中文模式)。英文输入模式返回 false。</summary>
    private static bool IsImeActive()
    {
        var (hFocus, imc) = GetImeContext();
        if (imc == IntPtr.Zero) return false;
        try { return ImmGetOpenStatus(imc); }
        finally { if (hFocus != IntPtr.Zero) ImmReleaseContext(hFocus, imc); }
    }

    /// <summary>输入法是否正在组词(拼音未上屏)。用 AttachThreadInput 取到前台窗口真正聚焦的控件。</summary>
    private static bool IsImeComposing()
    {
        var (hFocus, imc) = GetImeContext();
        if (imc == IntPtr.Zero) return false;
        try
        {
            if (!ImmGetOpenStatus(imc)) return false;
            return ImmGetCompositionString(imc, 0x0008 /* GCS_COMPSTR */, IntPtr.Zero, 0) > 0;
        }
        finally { if (hFocus != IntPtr.Zero) ImmReleaseContext(hFocus, imc); }
    }

    private static bool IsOurWindow()
    {
        IntPtr h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out uint pid);
        return pid == Environment.ProcessId;
    }

    public void Dispose()
    {
        _debounce.Stop();
        _uiaCacheReset.Stop();
        _hook.Dispose();
    }
}
