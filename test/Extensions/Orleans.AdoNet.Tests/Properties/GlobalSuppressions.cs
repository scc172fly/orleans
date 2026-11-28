// 代码分析使用此文件维护应用于
// 本项目的 SuppressMessage 特性。
// 项目级抑制项可以没有目标，也可以指定
// 特定目标，并限定在命名空间、类型、成员等范围内。

[assembly: SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = "ADO.NET integration test entry points delegate to shared test runners which perform the assertions.")]
