# Sony Music Center SMTC Bridge
[English Version](README.md)

专为桌面版 Sony Music Center for PC 打造的 Windows 10/11 原生系统媒体控制（SMTC）桥接插件。

Sony Music Center for PC 是一款基于 Electron 开发的应用，但由于其 Electron 版本较老且缺少必要的启动参数，无法原生支持 Windows 系统自带的媒体控制悬浮窗（SMTC）。本项目通过注入一个轻量级的后台桥接服务，为该软件赋予了完美的原生系统级整合能力。

## 核心特性
- **媒体热键：** 完美支持键盘上的 播放/暂停/上一曲/下一曲 等物理多媒体按键。即使软件最小化到系统托盘，依然可以全局控制。
- **曲目信息：** 实时提取正在播放的歌曲名、歌手以及专辑名称，完美原生支持 UTF-8（中日韩等全语种不乱码）。
- **专辑封面：** 自动拦截并提取高清专辑封面（甚至支持最难解析的内存 `blob:` 格式图片），直接渲染在 Windows 系统的音量悬浮窗上。
- **时间轴同步：** 自动提取极其精准的歌曲时长，并在后台动态生成“虚拟音频流”，实现进度条在系统控件上的完美同步，彻底告别弹窗鬼畜狂闪或时间轴失效的问题。
- **无侵入性：** 核心进程采用独立的 C# 原生程序运行在后台，通讯极简，绝不阻塞主程序的任何界面渲染线程。

## 安装方法

1. 请先彻底关闭正在运行的 Sony Music Center。
2. 克隆或下载本仓库的所有文件。
3. 右键点击 **`Install.bat`**，选择 **“以管理员身份运行”**。
   *(该脚本会自动将桥接程序复制到安装目录，并安全、自动地对原软件的启动脚本进行补丁注入)。*
4. 打开 Sony Music Center，随便播放一首歌，按一下键盘的音量键或者媒体键，享受原生体验！

## 架构原理

本项目由两部分巧妙组合而成：
1. **`renderer-hook.js`:** 这是一个注入到 Electron 渲染进程的脚本，负责实时抓取网页 DOM 节点并监听系统级别的播放控制回调。它通过 HTTP POST 请求巧妙地绕过了 Electron 严格的 CSP (内容安全策略) 限制。
2. **`SonyMusicCenterSMTC.cs`:** 一个极度精简的 C# WinRT 原生可执行文件。它通过创建一个 `MediaPlayer` 以及一段虚拟的 `MediaStreamSource` 音频流，直接与 Windows 底层通信，在不引起 UI 错乱的情况下完美接管了系统的媒体时间轴控制权。

## 卸载方法

如果你想移除该补丁，还原为原生软件：
1. 打开目录：`C:\Program Files (x86)\Sony\Music Center\resources\app\`。
2. 将里面那个被修改过的 `index.js` 文件直接删除。
3. 把旁边的 `index.js.bak` 文件重命名回 `index.js`。
4. 打开 `C:\Program Files (x86)\Sony\Music Center\` 主目录，删除 `SonyMusicCenterSMTC.exe` 即可。