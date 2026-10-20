# NoteView 标志

两枚音符构成字母 N，背后的五条细线对应五线谱。暖金经过浅紫过渡到青绿，呼应 NoteView 随和声变化的配色；深蓝底板上的两片淡色柔光与应用的背景氛围一致。

## 文件

| 文件 | 用途 |
| --- | --- |
| [noteview.svg](../assets/branding/noteview.svg) | 完整应用标志，可编辑矢量源文件 |
| [noteview-1024.png](../assets/branding/noteview-1024.png) | 1024 像素应用标志，圆角外透明 |
| [noteview-256.png](../assets/branding/noteview-256.png) | 应用窗口与文档配图 |
| [noteview-mark.svg](../assets/branding/noteview-mark.svg) / [PNG](../assets/branding/noteview-mark.png) | 无底板的彩色透明标志 |
| [noteview-mono.svg](../assets/branding/noteview-mono.svg) | 白色单色标志，用于深色背景 |
| [noteview-mono-dark.svg](../assets/branding/noteview-mono-dark.svg) | 深色单色标志，用于浅色背景 |
| [noteview.ico](../assets/branding/noteview.ico) | Windows 图标，含 16、20、24、32、48、64、128、256 像素 |

16–24 像素版本省略谱线，保持 N 和音符轮廓清楚。ICO 的小尺寸使用 32 位 DIB，大尺寸使用 PNG，兼顾托盘和资源管理器。窗口、任务栏、Alt+Tab 和托盘共用这套图标；演奏画布不额外叠加品牌元素。

## 修改与生成

编辑 `assets/branding/noteview.svg` 后运行：

```powershell
node scripts/GenerateBranding.cjs
```

此可选工具需要 Node.js 和可解析到的 `sharp` 包。它从矢量源生成 PNG、透明版、单色版、多尺寸 ICO，以及 `artifacts/branding/noteview-brand-board.png` 设计展示图。正常构建直接使用已提交的图标，不需要 Node.js 或 sharp。

标志为本项目绘制，无外部图片或字体轮廓依赖，与项目一同采用 MIT 许可。展示图中的文字仅用于排版说明。
