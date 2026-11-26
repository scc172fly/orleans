---
name: review-testing
description: "Orleans ——从胸镜中检测的镜头——根据胸孔、例如测试、根据协议进行的模型测试、 CsCheck 基于财产的测试、以控制时间为主的确定性模拟、错误注射和核查快照。"
---

<a id="testing-lens"></a>

# 测试透镜

应用 root `AGENTS.md` 和 `test/AGENTS.md`。 此镜头检查证据:
对照其特性和变异特性,包括
失败。

<a id="procedure"></a>

## 程序程序程序

1. 将神谕与本部分的变异因素脱钩,而不考虑目前的执行。
2. 使用以下的核对表选择确定每个属性的最简单技术。
3. 覆盖代表有效且无效的输入、它们之间的边界以及更改的错误路径。
4. 使每个失败都可复制:记录随机测试的种子和控制时间、同步和外部状态。
5. 重新利用现有测试基础设施、固定装置和 TestKits。

<a id="checklist"></a>

## 核对列表

- **示例测试** 点别特定回归和假设情景。
- **示范测试([租户](https://www.nuget.org/packages/Microsoft.Accordant))** 核对具有更简单参照模型的状态式部件,这些组件贯穿整个操作序列,包括同值货币和合同所涉的故障(见 `test/Orleans.Clustering.TestKit.Tests/MembershipTableModelBasedTestRunnerTests.cs`)。
- **基于财产的测试([CsCheck](https://github.com/AnthonyLloyd/CsCheck))** 涵盖变异物、圆柱和代数法,可复制种子并有效缩水(见 `test/Orleans.Core.Tests/Directory/RingRangeTests.cs`)。
- **确定性模拟** 控制时钟和明确阶段障碍 `test/AGENTS.md`,使用 `InProcessTestCluster` 当财产跨越了边界时([FoundationDB](https://apple.github.io/foundationdb/testing.html)).
- **错误注入** 练习更改了故障界限:储存(`FaultInjectionGrainStorage`、 `AddFaultInjectionMemoryStorage`、交易(`src/Orleans.Transactions.TestKit.Base/FaultInjection`)和 silo 停止和杀死(`StopSiloAsync`、 `KillSiloAsync`)。
- **安全和进度阶段。** 故障测试检查安全性,当故障发生时,在故障正常时,应恢复协议的进展假设并核查已声明的受约束([和平和平生存组织](https://research.google/pubs/paxos-made-live-an-engineering-perspective-2006-invited-talk/))下完成的工作。
- **抓图([核查](https://github.com/VerifyTests/Verify))** pin 线格式和生成的产出;基准变化作为语义变化加以审查(见 `test/Orleans.Serialization.UnitTests/GeneratedSerializerBitwiseCompatibilityTests.cs`)。
- **兼容性测试** 涵盖滚动升级窗口所要求的对已发运序列化的更改或双向持续数据。
- **右层** 单位测试包括纯逻辑逻辑,集集测试涵盖运行时间边界和供应商 TestKits 承租人合同;在装有套房的 `[TestCategory]` 公约。
- **合同主张。** 销售书对可观察合同进行精确检查:准确的合同准确到哪里(价值,在控制时间内订购),不附带细节。
- **理由先说** 随机测试发现虫虫,但无法证明没有虫虫;代码中的变量和说法的清晰模型可以确定正确性,测试也证实了这一点。

<a id="findings"></a>

## 审计结果

风险、未试验财产或变差以及
技术覆盖它。
