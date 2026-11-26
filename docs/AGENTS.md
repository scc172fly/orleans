<a id="orleans-documentation-guidance"></a>

# Orleans 文件指导

这些规则在以下文件、片片块和样本中依次适用:
`docs/`. 存储处一级的指导意见也适用于。

<a id="code-examples"></a>

## 守则示例

- 将每个 C# 示例放入一个 `snippets` 项目,并包含一个名为 `:::code`的 C# 示例
  当显示碎片时,在区域外添加隐藏的声明
  需要; 不使用 C# 内嵌的围栏 。
- 保持小片段、时装和自足,包括声明
  显示区域所需的区域。
- 汇编每个受影响的片段项目。 不要出版假代码, 好像它是一个可复制的例子一样 。
- 文件文件和片断项目目标 `net10.0`。
  仅针对 `net11.0` 准确目标 `net11.0` 并解释为何
  `OrleansDocumentationTargetFrameworkException`. 这些包件的片段
  `snippets/compiled-durable/` 并汇编在 `DurableDocumentationSnippets.csproj`中。
- 每个 `Microsoft.Orleans.*` 套件参考必须使用核准的版本 `10.2.2`。保持 Orleans 套件的家族式对齐,并集中项目结构支持其使用的版本。
- 仅对用于目的的狭义迁移示例使用旧 Orleans 套件
  需要该版本。 将其保持在 `migration` 下,并记录原因
  `OrleansDocumentationVersionException` 在那项工程里
- 将直接依赖性版本保持在或超过选定 Orleans 软件包要求的最低值。
- 不要使用不包含它的旧包件来显示未释放的 API 。 在有可调制源或包件示例可用之前, 请将其链接到其 API 引用处 。

<a id="links"></a>

## 链接链接链接

- Orleans 文档链接应相对,因此在 `https://dotnet.github.io/orleans`下工作。
- 使用完全合格、局部中立的外部卡路里链接,如 `https://learn.microsoft.com/azure/...`。
- 不要将迁移的仓库 `.md` 后缀移入已公布的链接中。
- 校验新添加或更改的外部链接,并运行文件站点的链接验证。

<a id="api-references"></a>

## API 引用

- 公共链接 .NET 要生成符号 API 使用 DocFX xref 语法, 而不是将符号格式化为内嵌代码。
- 使用 `<xref:Namespace.Type>` 来处理成员或超载组的类别和 `<xref:Namespace.Type.Member*>` 。
- 当完全合格的显示名称更加清晰时添加 `?displayProperty=nameWithType` 。
- 使用内线代码进行字面、配置值、 CLI 命令、文件名,
  提供商名, 以及不是可链接的公共符号的语法。 避免 。
  当先前的关联关系更明确时,反复将符号链接。
- 在发布前确认生成的 API 表面存在 xref 目标。

实例:

```markdown
Use <xref:Orleans.Runtime.IPersistentState`1> for persistent grain state.

Configure it with <xref:Orleans.Hosting.AzureTableSiloBuilderExtensions.AddAzureTableGrainStorage*?displayProperty=nameWithType>.
```

<a id="documentation-types"></a>

## 文件类型

解决相关的业务问题:正确性、安全性、可用性、
负荷中的升级、能力、成本和诊断。

- **建筑**:组成部分、协议、变量、一致性、故障和
  以规模和版本为单位设计货币语义,并设计权衡交易。
- **如何进入/编制**:一项任务,附有先决条件、陷阱和核查。
- **教学**:一个工作从开始到完成的学习路径。
- **概念**:精神模式、适合性、权衡和经核实的限制。
- **API 参考**:语义、线安全、寿命、例外、默认和示例。
- **FAQ/troubleshooting**:观察到的症状、原因、补救措施和预防。
- **移徙指南**:可操作版本到版本的先决条件,兼容性
  变更、升级步骤、核查和恢复制约因素。
- **发布说明**:发布时的变化,包括宣布、
  实验性收养者实验性功能历史和一次性兼容性说明。

更新内容放在适当的类别和交叉链接中,而不是混合用途。

<a id="content-placement"></a>

## 内容放置

- 主要概念、如何使用、架构和 API 文件描述当前
  运行时行为、 默认、 配置和辅助约束 。 请保留
  过去状况描述、以往的违约情况、排放量比较以及
  实验时代设置 从这些页面。
- 将特定版本的移徙程序和 API 适应指导意见置于
  `docs/site/src/content/docs/migration/`. 相关
  移徙指南,而不是重复其程序。
- 在发布时公布发布公告和历史实验性细节
  注:与辅助升级程序和主要
  当前特性行为文档。
- 保持一般部署指导,包括 `deployment/upgrades.md`,
  具体释放的过渡属于《移徙指南》、
  即使涉及滚动部署或回落。

<a id="content"></a>

## 内容内容内容内容内容内容

- 保持主文档以当前状态为焦点。 Name Orleans 释放
  仅以移民指南和发布说明的形式,在边界释放事项上。
- 描述已实施的行为和经核实的限制:相关触发因素,
  运行时动作、结果和操作者反应。
  执行机制。
- 使用 `LegacyGrainDirectory` 来定义用户标签目录名称和
  `UseLegacyGrainDirectory` 配置的配置 API. 保持执行
  源路径和代码引用中的 `LocalGrainDirectory` 等识别符号。
- 保存有用的内容和权威性参考文献;更新受影响的合同
  和行为。删除过时或多余的材料。
- 将枢纽网页作为概览处理:链接到同行详细页面,而不是挑选一个提供者或功能来提供内嵌配置指导。
- 移动内容时保存稳定的 URL 和 锁定,或提供明确的重定向或兼容性锁定。

<a id="sources-and-generated-output"></a>

## 来源和产出

- 在 `docs/site/src/content/docs/toc.yml` 中的每一新页,在
  与文件类型相符的章节。保留兼容页
  仅为稳定的入境路线设置 `navigation: hidden`;将其与
  当前焦点页面。
- 保持循环包括在文档源树内。 缺少
  环形、环形、绝对、驱动-相对性或对称链接-
  无效。编辑包含源的源头,确保活动包括参与
  链接验证。
- 不要手工编辑它生成的网站输出, 生成 API 数据, 依赖文件夹,
  或构建输出, 如生成的 `.mdx` 兄弟姐妹、 `dist`、 `node_modules`,
  `bin`或 `obj`。

<a id="validation"></a>

## 审定

- 运行分析器支持的链接,包括、 重定向、 导航和项目政策
  检查内容已修改的内容。
- 运行 `docs/site/src/content/docs/validate-snippets.ps1` 用于修改片段
  以及片段项目政策变化。它建立普通片段项目和
  以 `IsTestProject=true`标记的项目运行中,所以可执行的测试示例
  行为上是有效的。
- 当样本变化时运行 `samples/Validate-Samples.ps1` 。
- 当 `docs/Docs.slnx` 在整合后存在时,将其建为综合文件项目。
- 从 `docs/site`运行 `npm run validate`,包括重定向和完成
  产出审计。
- 在承诺前检查 `git diff --check` 。
