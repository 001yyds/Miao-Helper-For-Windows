using System.Windows.Automation;

namespace MiaoHelper;

/// <summary>
/// 用 UI Automation 读写当前聚焦输入框,不注入任何按键、不碰剪贴板、无闪屏。
/// 标准编辑控件(QQ/微信/记事本等)都暴露 ValuePattern/TextPattern。
/// </summary>
public static class UiaHelper
{
    /// <summary>
    /// 读取当前焦点输入框的文本;拿不到返回 null。
    /// 优先找输入框(Edit/ValuePattern),避免误抓到整个聊天记录(TextPattern 文档)。
    /// </summary>
    public static string? ReadFocusedText()
    {
        try
        {
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null) return null;

            // 1. 焦点元素本身就是输入框
            string? v = GetValue(el);
            if (v != null) return v;

            // 2. 沿父链向上找输入框(焦点可能在输入框的子元素上)。
            //    只向上、不做全树扫描,避免在网页渲染窗口(QQ/微信)上卡几秒。
            AutomationElement walk = el;
            for (int i = 0; i < 8 && walk != null; i++, walk = TreeWalker.ControlViewWalker.GetParent(walk))
            {
                v = GetValue(walk);
                if (v != null) return v;
            }

            // 3. 兜底:富文本框(可能拿到网页/URL 等非输入框内容,需过滤)
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out object? tp) && tp is TextPattern text)
            {
                string? t = text.DocumentRange.GetText(-1);
                if (!string.IsNullOrEmpty(t) && IsPlausibleInput(t)) return t;
                return null;
            }
        }
        catch { }
        return null;
    }

    /// <summary>过滤掉明显不是聊天输入框的内容(网页 URL、超长无空格 token 等,如 NT QQ 的 WebView)。</summary>
    private static bool IsPlausibleInput(string text)
    {
        if (text.Contains("://", StringComparison.OrdinalIgnoreCase)) return false;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return false;
        // 超长且无空格,像 URL/token 而非聊天内容
        if (text.Length > 200 && !text.Contains(' ')) return false;
        return true;
    }

    private static string? GetValue(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) && vp is ValuePattern value)
            {
                string? v = value.Current.Value;
                if (!string.IsNullOrEmpty(v) && IsPlausibleInput(v)) return v;
            }
        }
        catch { }
        return null;
    }

    /// <summary>尝试用 UIA 直接写入文本(光标移到末尾)。返回 null 表示成功,否则返回失败原因。</summary>
    public static string? WriteFocusedText(string text)
    {
        try
        {
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null) return "无焦点元素";

            // 只尝试焦点元素 + 父链(不做全树扫描,避免在网页渲染窗口上卡几秒)
            AutomationElement walk = el;
            for (int i = 0; i < 8 && walk != null; i++, walk = TreeWalker.ControlViewWalker.GetParent(walk))
            {
                if (walk.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) && vp is ValuePattern value)
                {
                    if (value.Current.IsReadOnly) return "找到 ValuePattern 但控件只读";
                    value.SetValue(text);
                    // SetValue 后光标通常在开头,补一个 Ctrl+End 移到末尾
                    ClipboardEngine.PressCtrlEnd();
                    return null; // 成功
                }
            }
            return "未找到可写输入框(焦点元素及父链均无 ValuePattern)";
        }
        catch (Exception ex) { return "异常: " + ex.Message; }
    }
}
