// 代码分析使用此文件来维持 SuppressMessage
// 用于此工程的属性。
// 项目一级抑制措施要么没有目标,要么被给予
// 具体目标和范围,以命名空间、类型、成员等为对象。

[assembly: SuppressMessage("Blocker Code Smell", "S2699:Tests should include assertions", Justification = "ADO.NET integration test entry points delegate to shared test runners which perform the assertions.")]
