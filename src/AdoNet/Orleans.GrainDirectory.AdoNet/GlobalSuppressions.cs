// 代码分析使用此文件来维持 SuppressMessage
// 用于此工程的属性。
// 项目一级抑制措施要么没有目标,要么被给予
// 具体目标和范围,以命名空间、类型、成员等为对象。

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Style", "IDE0130:Namespace does not match folder structure", Justification = "Configuration types use the shared Orleans.Configuration namespace exposed by provider packages.", Scope = "namespace", Target = "~N:Orleans.Configuration")]
[assembly: SuppressMessage("Style", "IDE0130:Namespace does not match folder structure", Justification = "Builder extensions use the shared Orleans.Hosting namespace exposed by provider packages.", Scope = "namespace", Target = "~N:Orleans.Hosting")]
