using System.Windows.Automation;

namespace MiaoHelper;

/// <summary>
/// 用 UI Automation 读写当前聚焦输入框,不注入任何按键、不碰剪贴板、无闪屏。
/// 标准编辑控件(QQ/微信/记事本等)都暴露 ValuePattern/TextPattern。
/// </summary>
public static class UiaHelper
{
    /// <summary>读取当前焦点输入框的文本;拿不到返回 null。</summary>
    public static string? ReadFocusedText()
    {
        try
        {
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null) return null;

            // 常规编辑框:ValuePattern
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) && vp is ValuePattern value)
                return value.Current.Value;

            // 富文本框:TextPattern
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out object? tp) && tp is TextPattern text)
                return text.DocumentRange.GetText(-1);

            // 焦点元素不是编辑框时,向下找可编辑子元素
            AutomationElement edit = el.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit != null
                && edit.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp2) && vp2 is ValuePattern v2)
                return v2.Current.Value;
        }
        catch { }
        return null;
    }

    /// <summary>尝试用 UIA 直接写入文本(光标移到末尾)。失败返回 false(调用方退回剪贴板方案)。</summary>
    public static bool WriteFocusedText(string text)
    {
        try
        {
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null) return false;

            // 焦点元素自身
            if (TrySetValue(el, text)) return true;

            // 向下找可编辑子元素(焦点可能在父容器上)
            AutomationElement edit = el.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit != null && TrySetValue(edit, text)) return true;

            // 向上找(焦点可能在子元素上)
            AutomationElement walk = el;
            for (int i = 0; i < 6 && walk != null; i++, walk = TreeWalker.ControlViewWalker.GetParent(walk))
            {
                if (TrySetValue(walk, text)) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TrySetValue(AutomationElement el, string text)
    {
        if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) && vp is ValuePattern value
            && !value.Current.IsReadOnly)
        {
            value.SetValue(text);
            // SetValue 后光标通常在开头,补一个 Ctrl+End 移到末尾
            ClipboardEngine.PressCtrlEnd();
            return true;
        }
        return false;
    }
}
