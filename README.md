# 喵喵助手 (MiaoHelper)

**特别声明，本程序由 DeepSeek 辅助编写**

BUG 反馈：`jvvoch@vvoch.cn`

一个 Windows 小工具：你聊天时打完一句话，它自动在每句后面加个「喵」，句末再随机带一个猫颜文字。效果是这样：

```
你好。  →  你好喵。 ฅ(̳•·̫•̳ฅ)♡
```

最早是安卓上一个叫「喵喵助手」的小工具（侵删），我把它复刻到了 Windows。完全本地运行，不联网，不碰你的聊天内容。

## 用起来什么样

- 常驻右下角系统托盘，平时不打扰，打字照常
- 输入 `你好。`，停顿片刻 → 自动变成 `你好喵。` + 随机颜文字
- 想处理剪贴板里的文字？按 `Ctrl + Alt + M` 一下就好
- 实测记事本、浏览器、QQ、微信都能用。QQ/微信的输入框是自绘的，处理时会有一次「选中替换」的闪烁，这是机制下限，去不掉

## 设置里能调什么

右键托盘小猫 → 设置：

1. 处理方式：标点触发（句末出现 `。！？!?` 才处理，推荐）/ 实时处理（打字一停顿就处理）
2. 触发符号：默认 `。！？!?`；想让空格也触发，自己加个空格
3. 追加文本：默认「喵」，留空也是「喵」——填成「喵喵」可就真的会变喵喵哦
4. 句末随机颜文字：开关
5. 替换规则：每行一条 `原词=新词`，比如 `我=本喵`
6. 自定义颜文字：留空用内置 28 个
7. 开机自启动：勾上，登录 Windows 自动开

配置存在 `%APPDATA%\喵喵助手\config.json`，改完立即生效。

## 群里推荐的替换规则

（来自喵喵群公告）

```
我=本喵
你=主人
大家=主人们
你们=主人们
哥哥=主人
乐乐=杂鱼🐟♡
乐子=杂鱼🐟♡
傻逼=杂鱼🐟♡
。= （后面是空格，不是什么都没有）
```

推荐配置长这样：

![推荐配置](https://cdn.web.small.vvoch.chat/Miao/MiaoHelper_rec_conf.png)

## 遇到问题？

- 一句话里冒出两个「喵」？去设置看「追加文本」是不是被填成了「喵喵」，改成「喵」或留空就好
- 打拼音时它不会动你输入框——组词中的内容它绝不碰
- 回车发送不会被拦截，放心用
- 其它问题或建议，发邮件：`jvvoch@vvoch.cn`

## 想自己构建？

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)，Windows 10 / 11：

```bash
dotnet build MiaoHelper -c Release

# 单文件 exe：
dotnet publish MiaoHelper -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

测试：

```bash
dotnet run --project MiaoHelper.Tests -c Release   # 算法测试
dotnet run --project IntegrationCheck -c Release   # 记事本端到端（需要桌面）
```

## 声明

- 本仓库只包含代码，不含原始 APK 及其反编译产物
- 仅做本地文本处理，不读取、不存储、不传输任何聊天内容；使用前请确认符合你所在地区及所用软件的规范
