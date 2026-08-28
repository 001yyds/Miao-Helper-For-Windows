using MiaoHelper;

// 复刻算法验证:结果应匹配原版 Android 的行为。
int failed = 0;

void Check(string name, bool ok)
{
    Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
    if (!ok) failed++;
}

var cfg = new CatConfig(); // 默认:追加+颜文字开,处理模式 punctuation

// 1. 多句切分追加 + 句末颜文字(颜文字随机,只校验前缀 + 是否以颜文字结尾)
string r1 = TextProcessor.Process("你好，世界。", cfg);
Check("多句追加", r1.StartsWith("你好喵，世界喵。") && !r1.EndsWith("。"));
Check("颜文字出现在句末(空格分隔)", r1.Contains("。 ") && r1.Length > "你好喵，世界喵。 ".Length);
Console.WriteLine("       例: " + r1);

// 2. 英文:空格也参与断句(原版正则含 \s),所以 hello 和 world 各加喵
string r2 = TextProcessor.Process("hello world!", cfg);
Check("英文标点切分", r2.StartsWith("hello喵 world喵!"));

// 3. 替换规则 from=to
var cfg2 = new CatConfig { Rules = new List<Rule> { new Rule { From = "你好", To = "hi" } } };
string r3 = TextProcessor.Process("你好，世界。", cfg2);
Check("替换规则", r3.StartsWith("hi喵，世界喵。"));

// 4. 只追加不开颜文字
var cfg3 = new CatConfig { EnableRandomEmoticon = false };
string r4 = TextProcessor.Process("在吗。", cfg3);
Check("关颜文字", r4 == "在吗喵。");

// 5. 不追加只颜文字
var cfg4 = new CatConfig { EnableAppend = false };
string r5 = TextProcessor.Process("在吗。", cfg4);
Check("关追加", r5.StartsWith("在吗。 "));

// 6. 全部关闭 → 原样返回
var cfg5 = new CatConfig { EnableAppend = false, EnableRandomEmoticon = false };
Check("全关原样", TextProcessor.Process("在吗。", cfg5) == "在吗。");

// 7. 空白文本原样返回
Check("空文本", TextProcessor.Process("   ", cfg) == "   ");
Check("null 文本", TextProcessor.Process(null!, cfg) == null);

// 8. parseRule 各种分隔符
Check("规则 '='", CatConfig.ParseRule("你好=hi")?.From == "你好" && CatConfig.ParseRule("你好=hi")?.To == "hi");
Check("规则 '＝'", CatConfig.ParseRule("你好＝hi")?.To == "hi");
Check("规则 '→'", CatConfig.ParseRule("你好→hi")?.To == "hi");
Check("规则 无分隔符→null", CatConfig.ParseRule("你好hi") == null);
Check("规则 from 为空→null", CatConfig.ParseRule("=hi") == null);

// 9. StripAll:去掉旧颜文字(带空格)
string withEmo = "在吗喵。 ( Φ ω Φ )";
string stripped = TextProcessor.StripAll(withEmo, cfg);
Check("StripAll 去颜文字", !stripped.Contains("Φ"));

// 10. IsPunctuationEnding(默认触发符号。！？!?，不含空格)
Check("句末标点判定", TextProcessor.IsPunctuationEnding("你好。", cfg.TriggerPunctuation)
    && TextProcessor.IsPunctuationEnding("你好！", cfg.TriggerPunctuation)
    && TextProcessor.IsPunctuationEnding("你好?", cfg.TriggerPunctuation)
    && TextProcessor.IsPunctuationEnding("你好！", cfg.TriggerPunctuation));
Check("非句末标点(含空格)", !TextProcessor.IsPunctuationEnding("你好", cfg.TriggerPunctuation)
    && !TextProcessor.IsPunctuationEnding("你好 ", cfg.TriggerPunctuation));

// 10b. 自定义触发符号(含手动加空格)
Check("自定义触发符号", TextProcessor.IsPunctuationEnding("你好~", "~")
    && !TextProcessor.IsPunctuationEnding("你好。", "~"));
Check("手动加空格触发", TextProcessor.IsPunctuationEnding("你好 ", "。！？!? "));

// 11. 整句无标点追加
var cfg6 = new CatConfig { EnableRandomEmoticon = false };
Check("整句无标点", TextProcessor.Process("在吗", cfg6) == "在吗喵");

// 12. 多句含空格与标点混合:空格、！ 各自独立断句
var cfg7 = new CatConfig { EnableRandomEmoticon = false };
string r12 = TextProcessor.Process("今天天气真好，出去走走 吧！", cfg7);
Check("混合切分", r12 == "今天天气真好喵，出去走走喵 吧喵！");

// 13. 配置保存/加载 JSON 往返
var cfg8 = new CatConfig { AppendText = "~", ProcessingMode = CatConfig.MODE_REALTIME };
cfg8.Rules.Add(new Rule { From = "a", To = "b" });
cfg8.CustomEmoticons.Add("(o.o)");
cfg8.Save();
var cfg9 = CatConfig.Load();
Check("配置保存/加载", cfg9.AppendText == "~"
    && cfg9.ProcessingMode == CatConfig.MODE_REALTIME
    && cfg9.Rules.Count == 1 && cfg9.Rules[0].From == "a" && cfg9.Rules[0].To == "b"
    && cfg9.CustomEmoticons.Count == 1 && cfg9.CustomEmoticons[0] == "(o.o)");
File.Delete(CatConfig.ConfigPath); // 清理测试残留

// 14. ReclaimRawText:还原已处理文本,重复处理不叠喵
var cfgR = new CatConfig { EnableRandomEmoticon = false };
string once = TextProcessor.Process("我真服咧", cfgR);          // "我真服咧喵"
string raw1 = TextProcessor.ReclaimRawText(once, cfgR);
Check("ReclaimRawText 去旧喵", raw1 == "我真服咧");
Check("重复处理幂等", TextProcessor.Process(raw1, cfgR) == once);

// 15. 带颜文字的还原(先手动处理,再去还原,再处理,结果一致)
var cfgE = new CatConfig();
string p1 = TextProcessor.Process("在吗。", cfgE);              // 在吗喵。 <emo>
string re1 = TextProcessor.ReclaimRawText(p1, cfgE);
Check("ReclaimRawText 去颜文字+喵", re1 == "在吗。");

// 16. RulesToString 用 \r\n 分隔(多行文本框需要 CRLF 才换行)
var cfgRL = new CatConfig();
cfgRL.Rules.Add(new Rule { From = "a", To = "1" });
cfgRL.Rules.Add(new Rule { From = "b", To = "2" });
Check("RulesToString 用 CRLF", cfgRL.RulesToString() == "a=1\r\nb=2");

// 17. CRLF 文本解析回规则不丢行
var cfgRL2 = new CatConfig();
cfgRL2.ApplyRulesText("a=1\r\nb=2");
Check("CRLF 规则解析", cfgRL2.Rules.Count == 2 && cfgRL2.Rules[1].From == "b");

Console.WriteLine();
Console.WriteLine(failed == 0 ? "全部通过 ✅" : $"有 {failed} 项失败 ❌");
return failed;
