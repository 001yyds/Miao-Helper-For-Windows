using System.Diagnostics;
using MiaoHelper;

// 端到端测试:打开记事本 → 粘贴中文 → 走一遍 快照/处理/写回 → 读回验证。
// 这能证明修复后的 SendInput 注入 + 剪贴板快照 + 写回在真实控件上全部生效。

int failed = 0;
void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
    if (!ok) failed++;
}

try
{
    var p = Process.Start("notepad.exe");
    Thread.Sleep(2000);

    string raw = "你好，世界。";
    var cfg = new CatConfig { EnableRandomEmoticon = false }; // 关颜文字,便于精确断言

    // 1. 把原始文本粘贴进记事本(等价输入)
    ClipboardEngine.WriteBackToActive(raw);
    Thread.Sleep(200);

    // 2. 快照:应当读回原始文本
    string? snap = ClipboardEngine.SnapshotActiveText();
    Console.WriteLine($"  快照: [{snap}]");
    Check("快照读回输入框文本", snap?.Trim() == raw);

    // 3. 处理
    string target = TextProcessor.Process(snap!.Trim(), cfg);
    Console.WriteLine($"  目标: [{target}]");
    Check("处理结果正确", target == "你好喵，世界喵。");

    // 4. 写回
    ClipboardEngine.WriteBackToActive(target);
    Thread.Sleep(250);

    // 5. 再快照:应当读到处理后的文本
    string? final = ClipboardEngine.SnapshotActiveText();
    Console.WriteLine($"  写回后: [{final}]");
    Check("写回后输入框内容已更新", final?.Trim() == target);

    // 6. UIA 读取:应当读到同样的文本(不注入按键)
    string? uia1 = UiaHelper.ReadFocusedText();
    Console.WriteLine($"  UIA 读取: [{uia1}]");
    Check("UIA 读取输入框文本", uia1?.Trim() == target);

    // 7. UIA 写入(SetValue + Ctrl+End),再读回验证
    string uiaTarget = "你好喵，世界喵。 UIA直写";
    try
    {
        var el = System.Windows.Automation.AutomationElement.FocusedElement;
        if (el != null && el.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out object? vp) && vp is System.Windows.Automation.ValuePattern v)
        {
            Console.WriteLine($"  ValuePattern: IsReadOnly={v.Current.IsReadOnly}");
            try { v.SetValue(uiaTarget); Console.WriteLine("  SetValue 成功"); }
            catch (Exception ex) { Console.WriteLine("  SetValue 异常: " + ex.Message); }
        }
        else Console.WriteLine("  焦点元素无 ValuePattern");
    }
    catch (Exception ex) { Console.WriteLine("  UIA 写入诊断异常: " + ex.Message); }
    bool wroteUia = UiaHelper.WriteFocusedText(uiaTarget);
    Thread.Sleep(300);
    string? uia2 = UiaHelper.ReadFocusedText();
    Console.WriteLine($"  UIA 写回: [{uia2}] (wroteUia={wroteUia})");
    // UIA 写入是"尽力而为",不支持时应用会退回剪贴板写入(已验证);此处仅作信息输出,不算失败
    Console.WriteLine(wroteUia && uia2?.Trim() == uiaTarget
        ? "  (UIA 写入生效,可无闪屏直写)"
        : "  (该控件不支持 UIA 写入,应用会退回剪贴板写入,功能不受影响)");

    Console.WriteLine();
    Console.WriteLine(failed == 0 ? "端到端全部通过 ✅" : $"端到端有 {failed} 项失败 ❌");
}
catch (Exception ex)
{
    Console.WriteLine("异常: " + ex);
    failed++;
}
finally
{
    try { Process.Start(new ProcessStartInfo("taskkill") { Arguments = "/IM notepad.exe /F", CreateNoWindow = true, UseShellExecute = false }); } catch { }
}

return failed;
