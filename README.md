# NoteView · 钢琴桌面五线谱

一个 Windows 独立桌面挂件。连接 USB MIDI 电钢琴后，居中的竖列大谱表和下方琴键同步点亮；和弦决定整体染色，击键力度决定亮度，亮度随后自然衰减。

## 直接使用

Windows x64，需要 .NET Framework 4.8 和可用的 MIDI 输入设备。

从 [Releases](https://github.com/MarcWHY/NoteView/releases) 下载便携版，完整解压后运行 `NoteView.exe`。下载源代码的用户请先执行下方构建命令，再双击 `Launch.cmd` 或运行 `dist\NoteView.exe`。

1. 启动后自动连接第一个可用 MIDI 输入；如果有多个设备，在左上角选择电钢琴。
2. 直接弹琴。音符按音高集中在同一竖列，不滚动；相邻二度或同位置的音符会小幅错开，避免遮挡。按住的音从力度对应的击键亮度自然衰减，最低保留该亮度的 14%；踩着踏板松键时，从当时亮度继续指数淡出到消失，并保留柔和模糊效果。重复击键重新点亮。颜色跟随当前稳定和声，同组和声保持配色，确认换和弦后平滑切换。
3. 点击 **设置**：自选背景颜色，或者把背景不透明度调到 **0% / 全透明**。力度只控制亮度；大和弦偏暖金，小和弦偏冷蓝，挂留偏青绿，属和弦偏珊瑚橙，减、增及变化和弦偏紫／玫红。具体规则见 [和声配色](docs/HARMONY_COLORS.md)。
4. 浅色桌面使用「深色谱线与文字」。点击 **纯谱面** 隐藏 MIDI 工具栏；拖动窗口顶部移动，拖动右下角调整尺寸。
5. **演示** 可在不弹琴时查看效果，不产生声音。收到实际琴键 Note On 后自动退出演示。
6. 在谱面上方的调式下拉框，或 **设置 → 默认调式** 中选择调式。支持 15 种调号对应的大调、小调，共 30 个选项；选择后即时生效并保存。
7. 点击窗口顶部的 **调整布局**：绿色矩形是整个布局画布，五线谱、键盘和和弦各有独立边框。拖动对应框内区域移动控件，拖右下角手柄或滚轮等比例缩放；点击 **完成调整** 或按 Esc 退出。三个控件均可移动到画布的其他区域，边框会限制在画布内。布局自动保存，OBS 使用相同的位置和大小。设置中也可分别精确调整、复位。

## 常用操作

| 操作 | 作用 |
| --- | --- |
| F2 | 打开设置 |
| Esc | 调整布局时结束调整；平时退出纯谱面并恢复鼠标操作 |
| 调整布局时拖动控件 / 滚轮 | 移动 / 缩放对应的五线谱、键盘或和弦 |
| 控件右下角手柄 | 等比例缩放该控件 |
| Ctrl+Alt+N | 找回窗口，并关闭鼠标穿透 |
| 双击系统托盘中的 NoteView 图标 | 找回窗口 |
| 托盘右键 | 设置、纯谱面、鼠标穿透、退出 |
| 清音 | 清除程序内所有亮音，不向电钢琴发送命令 |
| 断开 | 释放 MIDI 输入，方便其他音乐软件使用 |

鼠标穿透开启前会确认恢复快捷键已成功注册，避免窗口无法操作。程序退出时会释放 MIDI 端口。

## OBS 直播与录制

需要在 **NoteView 最小化后继续显示演奏** 时，使用内置的 OBS 浏览器输出。在 NoteView 的 **设置 → OBS 输出** 中保持输出开启，并点击 **复制 OBS 地址**；此功能默认开启。

在 OBS 的「来源」中添加 **浏览器**，关闭「本地文件」，填写：

| 选项 | 值 |
| --- | --- |
| URL | `http://127.0.0.1:18765/` |
| 宽度 / 高度 | `1120` / `640` |
| 使用自定义帧率 / FPS | 开启 / `30` |

确认后即可最小化 NoteView，MIDI 接收与独立输出会继续运行；退出程序会停止输出。背景跟随 NoteView 的背景颜色和不透明度，设置为全透明即可叠加在 OBS 其他来源上。地址只在本机使用，不需要互联网或额外插件。完整设置、黑屏排查见 [OBS 使用说明](docs/OBS.md)。

## 显示与和弦识别

- 默认完整 88 键 A0–C8；也可选择 C2–C6。常用音域模式下，范围外音符仍可参与和弦识别，但不显示在谱面上。
- 五线谱、键盘、和弦是统一矩形画布中的三个独立控件，移动或缩放一个不会改变另外两个；尺寸和位置受画布边界约束。桌面随窗口大小整体适配，OBS 保持 1120×640 的同一布局。C1–C8 八度文字已移除。
- 踏板延留的谱面音符、临时记号与加线使用独立的低透明度模糊层；实按音与五线谱保持清晰。键盘上的延留音仅保留淡色填充，去掉亮边和底部亮条。
- 高音、低音五线谱共用连续音高坐标，显示为居中的短谱线和一列音符。下方键盘独立按正常钢琴顺序排列。淡色音符提示当前调号中的七个基本音位，支持关闭。两个谱号后都会显示调号，临时记号在音符左侧分列避让。
- 记谱以所选调号为准。例如 **D 大调**有 F♯、C♯：这两个音不重复加升号，弹 F、C 自然音则显示 **♮ 还原号**；F 大调中的 B♭不重复加降号，B 自然音显示 ♮。支持 E♯、B♯、C♭、F♭等调内拼写及正确的谱面八度。
- 调式修改影响记谱和音名，不改变电钢琴的实际音高。小调采用其常规调号，升高的导音等临时变化仍会显示记号。此处是实时静态音符显示，临时记号逐个与调号比较，不沿用小节内的前一个临时记号。详见 [调式与调号说明](docs/KEY_SIGNATURES.md)。
- 力度来自 Note On 的 velocity（1–127），表示按键力度，在本次击键的整个按住及踏板延留阶段保持该次力度；不会测量扬声器实际音量，也不把弯音或触后解释为音量。
- 支持全部 16 个 MIDI 通道，或过滤指定通道；支持 CC64 延音踏板、CC120、CC121、CC123，以及 velocity=0 的 Note On。
- 和弦库包含 **106 类完整模式 + 67 类省略五度变体**，覆盖三和弦、挂留、附加音、六和弦、七和弦、九／十一／十三和弦，以及变化属和弦；支持全部 12 个根音、转位和斜线低音，界面仅显示和弦名称。完整类型与组成音见 [和弦库说明](docs/CHORDS.md)。
- 和声识别会合并重复八度，并结合当前和声基底判断经过音。七和弦、六和弦与加音和弦允许省略纯五度，会明确标注省略；不推测未弹出的根音；少数省略三音类型会标注 no3。无法识别时和弦名称留空。可选择是否把踏板延音纳入识别，已淡出消失的音不再参与；不进行整首乐曲的调性或功能和声分析。
- 本程序是实时音高显示器，不是乐谱导入、节奏记谱或录音转谱软件；不产生音频，不向电钢琴发送 MIDI。

## 连接排查

- 找不到设备：确认 USB 连接及电钢琴 USB MIDI 设置，点击「刷新」。设备热插拔会自动检测。
- 提示端口被占用：关闭其他音乐软件对该 MIDI 输入的占用，再点击「连接」。
- 连接成功但没有亮音：检查设置中的 MIDI 通道是否选为「全部通道」，并检查电钢琴是否发送 MIDI 音符。
- 和弦包含上一个和弦的音：松开延音踏板，或者关闭「和弦识别包含踏板延音」。
- 脱机显示卡住：点击「清音」。MIDI 设备断开时也会自动清除亮音。
- 多台完全同名的 MIDI 设备：WinMM 显示名称不能可靠区分相同设备的重插身份，重新插拔后请确认选择。

## 构建和验证

Windows x64，.NET Framework 4.8。使用 Windows 自带的 .NET Framework C# 编译器，无需额外安装 SDK 或 npm 包。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestMidiInput.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestUi.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestStaffLayout.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestStaffVisual.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestKeySignature.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestObsRenderer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestObsWorker.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestObsOutputServer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\TestObsIntegration.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\ListMidi.ps1
```

`dist\NoteView.exe --preview artifacts\preview.png` 在屏幕外渲染演示 PNG 后退出，不打开 MIDI，不覆盖用户设置。透明 PNG 保留真实 alpha 通道。

测试覆盖音乐状态、和弦在全部 12 个根音上的识别及转位、MIDI 消息顺序与生命周期，以及竖列布局、窗口、踏板、主题同步和真实透明像素。设备兼容性取决于 Windows MIDI 驱动。

开发中可以使用 `build.ps1 -OutputDirectory artifacts\staging-v5 -Test` 输出到临时目录，验证后再替换正在使用的版本。

设置保存在 `%APPDATA%\NoteView\settings.xml`。异常信息写入同目录 `error.log`。运行时不访问互联网；OBS 输出启用后仅在本机回环地址 `127.0.0.1:18765` 提供画面，关闭该设置即可停止服务。复制发布目录时保留 `assets` 子目录和 `NoteView.exe.config`。

## 代码结构与来源

- `src/MidiInput.cs`：WinMM 输入与断线检测。
- `src/MusicTheory.cs`：音符状态和和弦识别。
- `src/StaffView.cs`：固定大谱表、音符和键盘绘制。
- `src/KeySignature.cs`：调号、调内音拼写和还原号计算。
- `src/ObsFrameRenderer.cs`：独立于桌面窗口的 OBS 演奏画面绘制。
- `src/ObsOutputServer.cs`：本机浏览器源页面和 PNG 帧输出。
- `src/MainWindow.cs`、`src/SettingsWindow.cs`：桌面窗口、设置、托盘及快捷键。
- `assets/Bravura.otf`：Steinberg Bravura 音乐字体，SIL Open Font License 1.1；许可证随附。

使用的官方参考：[Windows MIDI 输入](https://learn.microsoft.com/en-us/windows/win32/api/mmeapi/nf-mmeapi-midiinopen)、[WPF 透明窗口](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency)、[Bravura 字体](https://github.com/steinbergmedia/bravura)。

### 基底与和弦记号

和弦名称与染色共享和声基底，明确延留的支撑音会保护当前基底，孤立经过音不立即引发重新命名。新配音或支撑音淡出后允许换基底。界面使用 ♭、♯、△、°、ø、+ 等音乐记号，保持单行固定字号。

## 开源协议

NoteView 代码与文档采用 [MIT License](LICENSE)。内附 Bravura 音乐字体采用 SIL Open Font License 1.1，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。
