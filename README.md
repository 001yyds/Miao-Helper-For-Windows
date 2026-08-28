# 喵喵助手 (MiaoHelper)

**特别声明，本程序由DeepSeek辅助编写**

BUG反馈:`jvvoch@vvoch.cn`

Windows 桌面小工具：自动在聊天消息里给每个句子末尾加「喵」，并随机追加一个颜文字。复刻自 Android 版「喵喵助手」，侵删。

> 说明：本仓库只包含代码，不含原始 APK 及其反编译产物

## 功能

- **自动加喵**：句子以触发符号结尾（默认 `。` `！` `？` `!` `?` 空格）时**立即**自动把当前消息处理成「每句末尾 + 喵 + 随机猫颜文字」并写回输入框
- **7 个可自定义项**（托盘右键 → 设置）：
  1. 处理方式：标点触发 / 实时处理
  2. 触发符号：句子以这些字符结尾时立即处理（默认 `。！？!?` + 空格）
  3. 断句追加开关
  4. 追加文本（默认「喵」）
  5. 句末随机颜文字开关
  6. 文本替换规则（`原词=新词`，分隔符支持 `=` `＝` `→`）
  7. 自定义颜文字（留空则使用内置的 28 个猫颜文字）
- **全局热键 `Ctrl + Alt + M`**：立即处理剪贴板里的文本
- 系统托盘常驻，可一键停用

## 推荐配置
![image](https://cdn.web.small.vvoch.chat/Miao/MiaoHelper_rec_conf.png)

### 替换文字推荐
（以下是喵喵群里公告的内容）
```
替换规则可以使用 清柠 的
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

## 环境要求

- Windows 10 / 11
- 构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)

## 构建

```bash
# 普通构建
dotnet build MiaoHelper -c Release

# 发布单文件 exe（框架依赖，本机已装 .NET 8 运行时即可直接运行）
dotnet publish MiaoHelper -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

## 使用

1. 运行 `dist/喵喵助手.exe`，程序驻留在系统托盘；
2. 在聊天框输入 `你好。`，停顿片刻 → 自动变成 `你好喵。 ฅ(̳•·̫•̳ฅ)♡`；
3. 右键托盘图标打开设置，可调整各项配置（保存在 `%APPDATA%\喵喵助手\config.json`）；
4. `Ctrl + Alt + M`：把剪贴板里的文本直接处理好，再粘贴发送。

## 测试

```bash
# 算法单元测试（切分、替换、去旧颜文字、配置往返等）
dotnet run --project MiaoHelper.Tests -c Release

# 记事本端到端测试（需要桌面环境；验证 UIA 读取 + 剪贴板写回真实生效）
dotnet run --project IntegrationCheck -c Release
```

## 目录结构

```
MiaoHelper/          主程序（WinForms + 系统托盘）
MiaoHelper.Tests/    算法单元测试
IntegrationCheck/    记事本端到端测试
```

## 免责声明

本工具仅做本地文本处理，不读取、不存储、不传输任何聊天内容。请在使用前确认符合你所在地区及所用软件的规范。
