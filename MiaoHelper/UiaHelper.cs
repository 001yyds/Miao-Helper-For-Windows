using System.Text;
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

            // 2. 焦点元素子树里的输入框(聊天记录不是 Edit,输入框才是)
            AutomationElement edit = el.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit != null) { v = GetValue(edit); if (v != null) return v; }

            // 3. 沿父链向上找输入框(焦点可能在输入框的子元素上)
            AutomationElement walk = el;
            for (int i = 0; i < 8 && walk != null; i++, walk = TreeWalker.ControlViewWalker.GetParent(walk))
            {
                v = GetValue(walk);
                if (v != null) return v;
            }

            // 4. 兜底:富文本框(可能拿到整个文档,调用方会做长度拦截并退回剪贴板)
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out object? tp) && tp is TextPattern text)
                return text.DocumentRange.GetText(-1);
        }
        catch { }
        return null;
    }

    private static string? GetValue(AutomationElement el)
    {
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) && vp is ValuePattern value)
                return value.Current.Value;
        }
        catch { }
        return null;
    }

    /// <summary>诊断:描述焦点元素的 UIA 控件树(前两层),用于定位微信/QQ 输入框。</summary>
    public static string DescribeFocusedTree()
    {
        var sb = new StringBuilder();
        try
        {
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null) return "无焦点元素";
            sb.AppendLine($"焦点元素: 类型={el.Current.ControlType.ProgrammaticName} 名称=[{el.Current.Name}] 类名=[{el.Current.ClassName}] Value={el.TryGetCurrentPattern(ValuePattern.Pattern, out _)} Text={el.TryGetCurrentPattern(TextPattern.Pattern, out _)}");
            int budget = 0;
            DumpChildren(el, sb, 0, ref budget);
            return sb.ToString();
        }
        catch (Exception ex) { return "描述异常: " + ex.Message; }
    }

    private static void DumpChildren(AutomationElement el, StringBuilder sb, int depth, ref int budget)
    {
        if (depth >= 2 || budget >= 15) return;
        try
        {
            var children = el.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement c in children)
            {
                if (budget >= 15) break;
                budget++;
                sb.AppendLine($"{new string(' ', (depth + 1) * 2)}类型={c.Current.ControlType.ProgrammaticName} 名称=[{c.Current.Name}] Value={c.TryGetCurrentPattern(ValuePattern.Pattern, out _)} Text={c.TryGetCurrentPattern(TextPattern.Pattern, out _)}");
                int sub = 0;
                DumpChildren(c, sb, depth + 1, ref sub);
            }
        }
        catch { }
    }

    /// <summary>尝试用 UIA 直接写入文本(光标移到末尾)。返回 null 表示成功,否则返回失败原因。</summary>
    public static string? WriteFocusedText(string text)
    {
        try
        {
            AutomationElement el = AutomationElement.FocusedElement;
            if (el == null) return "无焦点元素";

            // 候选:焦点元素本身、其 Edit 后代、父链上的元素
            var candidates = new List<AutomationElement> { el };
            AutomationElement edit = el.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit != null) candidates.Add(edit);
            AutomationElement walk = el;
            for (int i = 0; i < 6 && walk != null; i++, walk = TreeWalker.ControlViewWalker.GetParent(walk))
                candidates.Add(walk);

            foreach (var c in candidates)
            {
                if (c.TryGetCurrentPattern(ValuePattern.Pattern, out object? vp) && vp is ValuePattern value)
                {
                    if (value.Current.IsReadOnly) return "找到 ValuePattern 但控件只读";
                    value.SetValue(text);
                    // SetValue 后光标通常在开头,补一个 Ctrl+End 移到末尾
                    ClipboardEngine.PressCtrlEnd();
                    return null; // 成功
                }
            }
            return "未找到可写输入框(焦点元素及其树均无 ValuePattern)";
        }
        catch (Exception ex) { return "异常: " + ex.Message; }
    }
}
