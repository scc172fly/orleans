// 此文件由代码分析工具用于维护应用于此项目的 SuppressMessage
// 特性。
// 项目级抑制要么没有指定目标，要么具有
// 特定目标并限定在命名空间、类型、成员等范围内。

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Style", "IDE0130:Namespace does not match folder structure", Justification = "Configuration types use the shared Orleans.Configuration namespace exposed by provider packages.", Scope = "namespace", Target = "~N:Orleans.Configuration")]
[assembly: SuppressMessage("Style", "IDE0130:Namespace does not match folder structure", Justification = "Builder extensions use the shared Orleans.Hosting namespace exposed by provider packages.", Scope = "namespace", Target = "~N:Orleans.Hosting")]
