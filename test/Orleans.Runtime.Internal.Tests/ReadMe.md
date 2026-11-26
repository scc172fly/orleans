<a id="testerinternal-project"></a>

# TesterInternal 项目

这个项目是用于 **white-box** 测试的试验和试验 Orleans 运行时间 
包括内部 Orleans 运行时API测试。

这个项目有“朋友”进入内部 API 和 Orleans 运行时间。

下列项目使用 `[InternalsVisibleTo]` 组装属性给予测试项目的内部准入特权:

- `Orleans.dll`
- `OrleansRuntime.dll`
- `OrleansAzureUtils.dll`

该项目可能包含单位和系统测试的混合体,包括开始试验 silos。
