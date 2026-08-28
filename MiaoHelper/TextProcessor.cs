using System.Text;
using System.Text.RegularExpressions;

namespace MiaoHelper;

/// <summary>
/// 复刻原版 TextProcessor:句子切分追加 + 句末随机颜文字 + 去旧颜文字。
/// 切分正则与原版一致:([，,。！!？?\s]+)
/// </summary>
public static class TextProcessor
{
    private static readonly Regex SENTENCE_SPLIT = new(@"([，,。！!？?\s]+)");
    private static readonly Random Rnd = new();

    /// <summary>
    /// 按标点/空白切句,每个非空句子后追加 appendText(分隔符保留)。
    /// 逻辑与原版 appendPerSentence 一一对应。
    /// </summary>
    private static string AppendPerSentence(string text, string appendText)
    {
        if (appendText == null) appendText = "";

        var sentences = new List<string>();
        var delims = new List<string>();
        Match m = SENTENCE_SPLIT.Match(text);
        int lastEnd = 0;
        while (m.Success)
        {
            sentences.Add(text.Substring(lastEnd, m.Index - lastEnd));
            delims.Add(m.Groups[1].Value);
            lastEnd = m.Index + m.Length;
            m = m.NextMatch();
        }
        if (lastEnd >= text.Length)
        {
            // 以分隔符结尾:补一个空尾巴,保证循环里能原样拼回分隔符
            if (sentences.Count > 0 && lastEnd == text.Length) sentences.Add("");
        }
        else
        {
            sentences.Add(text.Substring(lastEnd));
        }
        if (sentences.Count == 0) sentences.Add(text);

        var sb = new StringBuilder();
        for (int i = 0; i < sentences.Count; i++)
        {
            string s = sentences[i].Trim();
            if (s.Length > 0)
            {
                sb.Append(s);
                sb.Append(appendText);
            }
            if (i < delims.Count) sb.Append(delims[i]);
        }

        string result = sb.ToString().Trim();
        return result.Length > 0 ? result : text + appendText;
    }

    private static string GetRandomEmoticon(CatConfig config)
    {
        string[] arr = config.GetActiveEmoticons();
        if (arr.Length == 0) return "";
        return arr[Rnd.Next(arr.Length)];
    }

    /// <summary>主处理流程:替换规则 → 断句追加 → 句末随机颜文字。与原版 process 一致。</summary>
    public static string Process(string text, CatConfig config)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        string v = text.Trim();

        if (config.Rules != null)
        {
            foreach (var r in config.Rules)
            {
                if (r != null && r.From.Length > 0) v = v.Replace(r.From, r.To);
            }
        }

        if (config.EnableAppend) v = AppendPerSentence(v, config.AppendText);

        if (config.EnableRandomEmoticon)
        {
            string emo = GetRandomEmoticon(config);
            if (!string.IsNullOrEmpty(emo)) v = v + " " + emo;
        }
        return v;
    }

    /// <summary>
    /// 去掉文本中已存在的颜文字(按长度降序,连同前面的一个字符一并删除,通常是空格),
    /// 最后把遗留的连续符号串清理成一个空格。与原版 stripAll 一致。
    /// </summary>
    public static string StripAll(string text, CatConfig config)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string[] emoticons = config.GetActiveEmoticons();
        if (emoticons.Length == 0) emoticons = CatConfig.BUILTIN_EMOTICONS;
        Array.Sort(emoticons, (a, b) => b.Length.CompareTo(a.Length)); // 长度降序

        string s = text;
        foreach (string emo in emoticons)
        {
            if (string.IsNullOrEmpty(emo)) continue;
            int idx;
            while ((idx = s.IndexOf(emo, StringComparison.Ordinal)) >= 0)
            {
                int removeFrom = idx <= 0 ? idx : idx - 1;
                s = s.Substring(0, removeFrom) + s.Substring(idx + emo.Length);
            }
        }
        return Regex.Replace(s, @"\s*[\p{S}\p{So}\p{Sm}\p{Sk}\p{P}]{3,}\s*", " ").Trim();
    }

    /// <summary>
    /// 把"已处理过的文本"还原成用户原始文本:去掉已加的颜文字(StripAll),
    /// 再去掉每个句子末尾已有的 appendText。供手动处理/重复处理时避免叠「喵」。
    /// </summary>
    public static string ReclaimRawText(string text, CatConfig config)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string s = StripAll(text, config);
        string app = config.AppendText ?? "";
        if (app.Length == 0) return s;

        var sentences = new List<string>();
        var delims = new List<string>();
        Match m = SENTENCE_SPLIT.Match(s);
        int lastEnd = 0;
        while (m.Success)
        {
            sentences.Add(s.Substring(lastEnd, m.Index - lastEnd));
            delims.Add(m.Groups[1].Value);
            lastEnd = m.Index + m.Length;
            m = m.NextMatch();
        }
        if (lastEnd >= s.Length)
        {
            if (sentences.Count > 0 && lastEnd == s.Length) sentences.Add("");
        }
        else
        {
            sentences.Add(s.Substring(lastEnd));
        }
        if (sentences.Count == 0) sentences.Add(s);

        var sb = new StringBuilder();
        for (int i = 0; i < sentences.Count; i++)
        {
            string part = sentences[i].TrimEnd();
            if (part.EndsWith(app, StringComparison.Ordinal)) part = part[..^app.Length].TrimEnd();
            sb.Append(part);
            if (i < delims.Count) sb.Append(delims[i]);
        }
        return sb.ToString().Trim();
    }

    /// <summary>句末标点判定:文本末尾字符是否在 triggerPunct 中。</summary>
    public static bool IsPunctuationEnding(string text, string triggerPunct)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(triggerPunct)) return false;
        char c = text[^1];
        return triggerPunct.IndexOf(c) >= 0;
    }
}
