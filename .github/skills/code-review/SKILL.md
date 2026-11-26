---
name: code-review
description: "审查层合同、变异性、最低复杂性、运行时间性能、测试和重点文件的 Orleans 变化。 用于 GitHub 联合试码审查和其他审查任务。"
---

<a id="orleans-code-review"></a>

# Orleans 代码审查

1. 读取 root `AGENTS.md` 和可适用的目录指令。应用
   标准正确性、安全、
   兼容性、货币、可靠性和测试检查。
2. 确定变革的目标,以及每个受影响的层的要求和规定
   跟踪与《公约》相关的打电话者、所有权、寿命期和过渡情况
   更改以核实合同,区分经证明的变差和假设。
3. 检查是否在 diff 中添加了每一项(状态、抽象、工作、分配、
   保留内存、处理错误、文件)是具体要求。
   建议最简单完整的更正,以保持要求的行为和
   合同。
4. 检查行为测试和自动公用
   受影响代码中存在有意义的空白或倒退; 说明缺少证据的时间。
   以计量或明确费用假设来支持业绩索赔。
5. 能够简化代码的旗帜外加变量:“如果我们能保证X,
   然后,我们为Y而简化它。你应当辨别自己的层层,执行的层,
   可行性;区分有条件设计建议与已确认的设计建议
   违反合同。
6. 以每种透镜技能审查受影响的代码,仅报告材料;
   范围范围内的调查结果:
   `.github/skills/review-architecture/SKILL.md`,
   `.github/skills/review-invariants/SKILL.md`,
   `.github/skills/review-testing/SKILL.md`,
   `.github/skills/review-performance/SKILL.md`, 和
   `.github/skills/review-code-craft/SKILL.md`.

与改变的行线挂钩,说明结果
将正确性和有意义的运行时间或
维持费用高于主观偏好;将建议保留在范围之内。
将共同根源、按优先事项将其排在最根本的顺序
`AGENTS.md`,并分别列出开放的变量和兼容性问题。
