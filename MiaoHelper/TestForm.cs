using System.Drawing;
using System.Windows.Forms;

namespace MiaoHelper;

/// <summary>
/// 测试窗口:上面是用户自定义填字(可编辑),下面是"喵完之后"的处理结果(只读)。
/// 配置取自当前设置窗口(未保存也生效);随机颜文字每次点"喵一下"会重新 roll。
/// </summary>
public sealed class TestForm : Form
{
    private readonly TextBox _txtInput;
    private readonly TextBox _txtOutput;
    private CatConfig _cfg;

    public TestForm(CatConfig cfg)
    {
        _cfg = cfg;

        Text = "喵喵助手 - 测试";
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 400);
        MinimumSize = new Size(380, 320);

        var lblInput = new Label
        {
            Text = "输入要测试的文字：",
            Location = new Point(14, 12),
            AutoSize = true,
        };
        _txtInput = new TextBox
        {
            Location = new Point(14, 34),
            Size = new Size(452, 130),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Text = "你好，世界。",
        };

        var btnProcess = new Button
        {
            Text = "喵一下（处理）",
            Location = new Point(14, 172),
            Size = new Size(140, 30),
        };
        btnProcess.Click += (_, _) => ProcessNow();

        var lblOutput = new Label
        {
            Text = "喵完之后的结果：",
            Location = new Point(14, 210),
            AutoSize = true,
        };
        _txtOutput = new TextBox
        {
            Location = new Point(14, 232),
            Size = new Size(452, 150),
            Multiline = true,
            ReadOnly = true,
            BackColor = Color.White,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
        };

        Controls.AddRange(new Control[] { lblInput, _txtInput, btnProcess, lblOutput, _txtOutput });
        AcceptButton = btnProcess;

        ProcessNow();
    }

    /// <summary>用设置窗口的最新配置刷新,并重新处理一次。</summary>
    public void SetConfig(CatConfig cfg)
    {
        _cfg = cfg;
        ProcessNow();
    }

    private void ProcessNow()
    {
        string input = _txtInput.Text;
        _txtOutput.Text = string.IsNullOrWhiteSpace(input)
            ? "（输入为空，喵不动）"
            : TextProcessor.Process(input, _cfg);
    }
}
