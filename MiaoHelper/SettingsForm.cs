using System.Drawing;
using System.Windows.Forms;

namespace MiaoHelper;

/// <summary>设置窗口:5 个自定义项 + 测试按钮。</summary>
public sealed class SettingsForm : Form
{
    private readonly HookEngine _engine;
    private readonly CatConfig _cfg;

    private readonly RadioButton rdoPunct;
    private readonly RadioButton rdoRealtime;
    private readonly CheckBox chkAppend;
    private readonly TextBox txtAppendText;
    private readonly CheckBox chkEmoticon;
    private readonly TextBox txtRules;
    private readonly TextBox txtEmoticons;
    private readonly Label lblTest;

    public SettingsForm(HookEngine engine)
    {
        _engine = engine;
        _cfg = engine.Config;

        Text = "喵喵助手 - 设置";
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 572);

        var title = new Label
        {
            Text = "喵喵助手",
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
            ForeColor = Color.DarkOrange,
            AutoSize = true,
            Location = new Point(16, 12),
        };

        // ---- 处理方式 ----
        var gMode = new GroupBox { Text = "处理方式", Location = new Point(16, 44), Size = new Size(428, 76) };
        rdoPunct = new RadioButton
        {
            Text = "标点触发（句末出现 。！？ 时自动加喵）",
            Location = new Point(14, 22),
            AutoSize = true,
        };
        rdoRealtime = new RadioButton
        {
            Text = "实时处理（打字停顿片刻即处理）",
            Location = new Point(14, 46),
            AutoSize = true,
        };
        gMode.Controls.Add(rdoPunct);
        gMode.Controls.Add(rdoRealtime);

        // ---- 追加设置 ----
        var gAppend = new GroupBox { Text = "追加设置", Location = new Point(16, 128), Size = new Size(428, 88) };
        chkAppend = new CheckBox
        {
            Text = "每个句子末尾追加文本：",
            Location = new Point(14, 22),
            AutoSize = true,
        };
        txtAppendText = new TextBox { Location = new Point(190, 20), Width = 120 };
        var lblAppendHint = new Label
        {
            Text = "（默认：喵）",
            Location = new Point(318, 24),
            AutoSize = true,
            ForeColor = Color.Gray,
        };
        chkEmoticon = new CheckBox
        {
            Text = "整条消息末尾随机追加一个猫颜文字",
            Location = new Point(14, 52),
            AutoSize = true,
        };
        gAppend.Controls.AddRange(new Control[] { chkAppend, txtAppendText, lblAppendHint, chkEmoticon });

        // ---- 文本替换规则 ----
        var gRules = new GroupBox { Text = "文本替换规则", Location = new Point(16, 224), Size = new Size(428, 130) };
        var lblRulesHint = new Label
        {
            Text = "每行一条，格式：原词=新词（分隔符支持 = ＝ →）",
            Location = new Point(14, 20),
            AutoSize = true,
            ForeColor = Color.Gray,
        };
        txtRules = new TextBox
        {
            Location = new Point(14, 42),
            Size = new Size(400, 78),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
        };
        gRules.Controls.Add(lblRulesHint);
        gRules.Controls.Add(txtRules);

        // ---- 自定义颜文字 ----
        var gEmo = new GroupBox { Text = "自定义颜文字", Location = new Point(16, 362), Size = new Size(428, 130) };
        var lblEmoHint = new Label
        {
            Text = "每行一个，留空则使用内置颜文字",
            Location = new Point(14, 20),
            AutoSize = true,
            ForeColor = Color.Gray,
        };
        txtEmoticons = new TextBox
        {
            Location = new Point(14, 42),
            Size = new Size(400, 78),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
        };
        gEmo.Controls.Add(lblEmoHint);
        gEmo.Controls.Add(txtEmoticons);

        // ---- 测试 / 保存 / 取消 ----
        var btnTest = new Button { Text = "测试", Location = new Point(16, 500), Size = new Size(80, 30) };
        lblTest = new Label
        {
            Text = "",
            Location = new Point(104, 507),
            AutoSize = true,
            ForeColor = Color.DarkSlateGray,
        };
        var btnSave = new Button { Text = "保存", Location = new Point(280, 500), Size = new Size(80, 30), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Location = new Point(364, 500), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
        var lblCredit = new Label
        {
            Text = "v1.2 by---JanVvoch",
            Location = new Point(16, 540),
            AutoSize = true,
            ForeColor = Color.Gray,
        };

        btnTest.Click += (_, _) => Test();
        btnSave.Click += (_, _) => Save();
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[]
        {
            title, gMode, gAppend, gRules, gEmo,
            btnTest, lblTest, btnSave, btnCancel, lblCredit,
        });

        LoadConfigToUi();
    }

    private void LoadConfigToUi()
    {
        rdoRealtime.Checked = _cfg.ProcessingMode == CatConfig.MODE_REALTIME;
        rdoPunct.Checked = !rdoRealtime.Checked;
        chkAppend.Checked = _cfg.EnableAppend;
        txtAppendText.Text = _cfg.AppendText;
        chkEmoticon.Checked = _cfg.EnableRandomEmoticon;
        txtRules.Text = _cfg.RulesToString();
        txtEmoticons.Text = _cfg.CustomEmoticonsText;
    }

    private void Test()
    {
        var cfg = BuildConfig();
        cfg.ApplyRulesText(txtRules.Text);
        cfg.ApplyCustomEmoticonsText(txtEmoticons.Text);
        string input = "你好，世界。";
        string result = TextProcessor.Process(input, cfg);
        lblTest.Text = "测试：你好，世界。 → " + result;
    }

    private CatConfig BuildConfig() => new CatConfig
    {
        ProcessingMode = rdoRealtime.Checked ? CatConfig.MODE_REALTIME : CatConfig.MODE_PUNCTUATION,
        EnableAppend = chkAppend.Checked,
        AppendText = txtAppendText.Text,
        EnableRandomEmoticon = chkEmoticon.Checked,
    };

    private void Save()
    {
        var cfg = BuildConfig();
        cfg.ApplyRulesText(txtRules.Text);
        cfg.ApplyCustomEmoticonsText(txtEmoticons.Text);
        cfg.Save();
        _engine.ReloadConfig();
        Close();
    }
}
