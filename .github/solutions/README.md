<a id="ci-test-graphs"></a>

# CI 测试依赖图

`CodeGenerator.slnf` 选择 CodeGen 分区的测试根项目：
`Orleans.CodeGenerator.Tests` 和 `Orleans.DefaultCluster.Tests`。现有的
`Provider`、`Area` 和 `Suite` 筛选器负责选择这些程序集内的测试。
项目引用提供传递性构建依赖图，包括运行时资产、F# 程序集以及
分别发布的两个 `TestVersionGrains` 版本。

每次测试尝试均使用选定的解决方案进行还原、构建和测试发现，
并使用相同的代码覆盖率产物标识。其他分区使用
`Orleans.slnx`。完整解决方案的 CI 构建与包兼容性验证覆盖待发布的项目依赖图。
构建后，CI 对两个目标框架分别比较完整解决方案与聚焦 CodeGen 解决方案的测试发现结果，
比较测试 ID、名称、程序集、目标框架、体系结构及报告的测试特征。

在其他项目中添加 CodeGen 测试时，请将该项目纳入此筛选器，
并在完成全量构建后，针对每个目标框架运行
`.github/scripts/test-codegenerator-graph.ps1`。
