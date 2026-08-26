# 喵喵助手 (MiaoHelper)

**特别声明，本程序由DeepSeek辅助编写**

Windows 桌面小工具：自动在聊天消息里给每个句子末尾加「喵」，并随机追加一个颜文字。复刻自 Android 版「喵喵助手」，侵删。

**完全本地运行，不联网、不上传、不读取任何聊天数据。**

> 说明：本仓库只包含代码，不含原始 APK 及其反编译产物。

## 功能

- **自动加喵**：输入 `。` `！` `？` 后**立即**自动把当前消息处理成「每句末尾 + 喵 + 随机猫颜文字」并写回输入框
- **5 个可自定义项**（托盘右键 → 设置）：
  1. 处理方式：标点触发 / 实时处理
  2. 断句追加开关
  3. 追加文本（默认「喵」）
  4. 句末随机颜文字开关
  5. 文本替换规则（`原词=新词`，分隔符支持 `=` `＝` `→`）
  6. 自定义颜文字（留空则使用内置的 28 个猫颜文字）
- **全局热键 `Ctrl + Alt + M`**：立即处理剪贴板里的文本
- 系统托盘常驻，可一键停用

## 工作原理

Windows 没有 Android 那样的无障碍服务，无法直接读写任意应用输入框。程序采用：

1. **键盘钩子**只做观察（绝不拦截任何按键，绝不打断输入法）；
2. 按键后立即用 **UI Automation** 直接读取输入框文本（无需注入按键、不闪屏、不碰剪贴板）；
3. 若文本以句末标点结尾则处理，并优先用 UIA 写回；不支持 UIA 写入的输入框自动退回**剪贴板方案**（`Ctrl+A` + `Ctrl+V`）。

> 文本切分正则与追加逻辑与原版一致：`([，,。！!？?\s]+)`。

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
