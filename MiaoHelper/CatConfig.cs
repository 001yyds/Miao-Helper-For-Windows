using System.IO;
using System.Text.Json;

namespace MiaoHelper;

/// <summary>一条文本替换规则 from → to。</summary>
public sealed class Rule
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public override string ToString() => $"{From}={To}";
}

/// <summary>
/// 配置(对应原版 SharedPreferences cat_config)。6 个可自定义键:
/// 处理模式 / 断句追加开关 / 追加文本 / 句末颜文字开关 / 替换规则 / 自定义颜文字。
/// </summary>
public sealed class CatConfig
{
    public const string MODE_PUNCTUATION = "punctuation";
    public const string MODE_REALTIME = "realtime";

    /// <summary>内置颜文字:直接取自反编译 CatConfig.java 第 20-48 行的 unicode 转义,原样保留。</summary>
    public static readonly string[] BUILTIN_EMOTICONS = new string[]
    {
        "=^ᚖ6^=",
        "ฅ•̀∀•́ฅ",
        "ฅ(̳•·̫•̳ฅ)♡",
        "=^•ω•^=",
        "/ᐠ - ˕ -マ Ⳋ",
        "ฅ՞•ﻌ•՞ฅ",
        "ฅ(*`ω´*)ฅ",
        "₍˄·͈༝·͈˄*₎◞ ̑̑",
        "₍^⸝⸝> ·̫ <⸝⸝ ^₎",
        "₍Ἰ0˄•͈༝•͈˄₎ฅ˒˒",
        "꒰ఎ(^ . ֑ .^)໒꒱",
        "₍⸍⸌·͈༝·͈⸍⸌₎◞",
        "ฅ^-﹃-^ฅ",
        "୧₍˄·͈༝·͈˄₎୨",
        "˓˓ก(⸍⸌̣ʷ̣̫⸍̣⸌₎ค˒˒",
        "(`･ω･´)ฅ",
        "(^ω^ฅ)",
        "ฅ(=´▽`=)ฅ",
        "(ฅ◑ω◑ฅ)",
        "(ฅ>ω<*ฅ)",
        "(=´ᴥ`)",
        "(=^-ω-^=)",
        "ヽ(=^･ω･^=)丿",
        "( Φ ω Φ )",
        "ฅ( ̳• ◡ • ̳)ฅ",
        "~o( =∩ω∩= )m",
        "^⌯ᚖ6⌯^ ੭ ^",
        "≡ω≡",
    };

    public bool EnableAppend { get; set; } = true;
    public string AppendText { get; set; } = "喵";
    public bool EnableRandomEmoticon { get; set; } = true;
    public string ProcessingMode { get; set; } = MODE_PUNCTUATION;
    public List<Rule> Rules { get; set; } = new();
    public List<string> CustomEmoticons { get; set; } = new();

    /// <summary>生效的颜文字集合:自定义非空则用自定义,否则内置。</summary>
    public string[] GetActiveEmoticons()
        => CustomEmoticons.Count > 0 ? CustomEmoticons.ToArray() : BUILTIN_EMOTICONS;

    /// <summary>解析一行 "from=to" 规则,分隔符支持 = ＝ →,取最靠前的那个。</summary>
    public static Rule? ParseRule(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        string s = line.Trim();
        const string seps = "=＝→";
        int idx = -1;
        for (int i = 0; i < seps.Length; i++)
        {
            int p = s.IndexOf(seps[i]);
            if (p >= 0 && (idx < 0 || p < idx)) idx = p;
        }
        if (idx <= 0) return null;
        string from = s[..idx].Trim();
        if (from.Length == 0) return null;
        string to = s[(idx + 1)..].Trim();
        return new Rule { From = from, To = to };
    }

    public string RulesToString()
    {
        var lines = new List<string>();
        foreach (var r in Rules)
            if (r != null && r.From.Length > 0)
                lines.Add(r.From + "=" + r.To);
        // 多行文本框需要 \r\n 才会换行
        return string.Join("\r\n", lines);
    }

    public void ApplyRulesText(string text)
    {
        Rules = new List<Rule>();
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var line in text.Split('\n'))
        {
            var r = ParseRule(line);
            if (r != null) Rules.Add(r);
        }
    }

    public string CustomEmoticonsText => string.Join("\r\n", CustomEmoticons);

    public void ApplyCustomEmoticonsText(string text)
    {
        CustomEmoticons = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return;
        foreach (var line in text.Split('\n'))
        {
            string s = line.Trim();
            if (s.Length > 0) CustomEmoticons.Add(s);
        }
    }

    // ---------- JSON 存取 ----------

    public static string ConfigPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "喵喵助手");
            return Path.Combine(dir, "config.json");
        }
    }

    public static string DebugLogPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "喵喵助手");
            return Path.Combine(dir, "debug.log");
        }
    }

    public static CatConfig Load()
    {
        try
        {
            string path = ConfigPath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var c = JsonSerializer.Deserialize<CatConfig>(json);
                if (c != null) return c;
            }
        }
        catch { /* 配置损坏时用默认值 */ }
        return new CatConfig();
    }

    public void Save()
    {
        try
        {
            string path = ConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
