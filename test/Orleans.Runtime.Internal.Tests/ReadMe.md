<a id="testerinternal-project"></a>

# TesterInternal 项目

本项目用于对 Orleans 运行时进行**白盒**测试，
包括对 Orleans 内部运行时 API 的测试。

本项目具有对 Orleans 运行时内部 API 的友元访问权限。

以下项目使用 `[InternalsVisibleTo]` 程序集特性授予本测试项目内部访问权限：

- `Orleans.dll`
- `OrleansRuntime.dll`
- `OrleansAzureUtils.dll`

本项目可能同时包含单元测试和系统测试，包括启动测试 Silo 的测试。
