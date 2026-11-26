<a id="ci-test-graphs"></a>

# CI 测试图形

`CodeGenerator.slnf` 选择 CodeGen 分区的测试根根:
`Orleans.CodeGenerator.Tests` 和 `Orleans.DefaultCluster.Tests`。
`Provider`、 `Area`和 `Suite` 过滤器选择这些组件内的测试。
项目参考提供过渡性建筑图,包括运行时资产、
F#集会,以及分别出版的 `TestVersionGrains`版本。

测试动作使用选定解决方案恢复、构建和发现
每次尝试, 都以相同的文物身份覆盖。 其他分割区使用
`Orleans.slnx`. 完整的溶解 CIC 构建和包件兼容性验证
建筑后,光学分类比较了全部和重点突出 CodeGen
测试识别、名称、组装、目标框架、建筑和报告的检测身份、名称、组装、目标框架、结构及报告
两个目标框架的特点。

在另一个工程中添加 CodeGen 测试时, 请将该项目包含在此过滤器中
运行每个目标框架的 `.github/scripts/test-codegenerator-graph.ps1`
满额后,
