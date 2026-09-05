# 默认调性与临时记号

NoteView 支持七个降号至七个升号的全部 15 组标准调号，包含对应的大调、小调，共 30 个选择。大调与关系小调使用相同调号；小调选项使用自然小调调号，和声小调、旋律小调中的变化音通过临时记号显示。

例如，D 大调／B 小调的调号包含 F♯、C♯：弹 F♯ 时，F 的位置直接亮起，不重复写升号；弹普通 F 时，同一位置显示 ♮；C 与 C♯ 同理。F 大调中的普通 B、降 B 大调中的普通 E，也会显示还原号。

谱面是当前发声音的静态快照，没有小节或时间轴。因此，每个音始终相对于所选调号独立标注临时记号，不把刚才弹过的临时记号延续到下一音。若同一八度的 F 和 F♯在 D 大调下同时发声（包括踏板延音），两个音会紧凑并排，并分别明确写出 ♮ 和提示性的 ♯，使记号与各自音符对应。通常音头中心相距约 30 像素，只有周围音符或记号实际发生遮挡时才适当增加间距。只有 F♯单独发声时，仍不重复写升号。

调内音优先按调号拼写，包含 E♯、B♯、C♭、F♭；如 C♯ 大调中的 MIDI C4 写为 B♯3，C♭ 大调中的 MIDI B3 写为 C♭4。调外白键优先使用本位音，需要时加还原号；其他变化音在升号调中使用升号、降号调中使用降号。C 大调／A 小调继续使用设置中的“无调号偏好”。MIDI 只提供音高，变化音的具体和声拼写采用上述固定显示规则。

和弦识别仍按实际音级集合计算，名称采用相应的升号／降号偏好。极端调号下的和弦根音暂不进行完整等音重拼，例如 C♭ 大调的主和弦可能命名为 B；谱面与逐音列表会正确使用 C♭。

理论依据：

- [Open Music Theory — Key signatures](https://openmusictheory.github.io/keySignatures.html)：标准调号的升降号顺序、D 大调的 F♯／C♯、关系大小调共享调号。
- [The Open University — Accidentals](https://www.open.edu/openlearn/history-the-arts/music/an-introduction-music-theory/content-section-6.1)：还原号可以取消调号中的升号或降号。
- [LilyPond — Displaying pitches](https://lilypond.org/doc/v2.25/Documentation/notation/displaying-pitches.html)：同位置的不同变化音同时出现时可以强制显示各自记号，避免歧义。

独立验证脚本：`tests\TestKeySignature.ps1`。检查 30 个调性名称、15 个大调音阶、全部 128 个 MIDI 音高在每种调号及升降号偏好下的音高回转、还原号，以及极端调号的等音跨八度拼写。
