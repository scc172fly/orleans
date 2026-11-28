---
title: Grain 扩展
description: 为 Orleans Grain 添加由运行时提供的接口。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-extensions"></a>
# Grain 扩展

Grain 扩展为 Grain 激活实例附加额外的可调用接口。Orleans 在内部使用扩展实现取消和流处理等功能。应用程序可以利用扩展实现需要跨多种 Grain 类型提供的基础设施行为。

扩展是一种高级集成机制。如果某项行为属于 Grain 的领域契约，应优先使用普通的 Grain 接口。

<a id="define-an-extension"></a>
## 定义扩展

扩展接口继承自 <xref:Orleans.Runtime.IGrainExtension>，并使用常规 Grain 方法的返回类型：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="diagnostics_extension":::
Orleans 在构建时生成扩展的请求代码和引用代码。

<a id="register-the-extension"></a>
## 注册扩展

在 Silo 上注册默认实现：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="register_diagnostics_extension":::
通过依赖注入为目标 Grain 上下文创建实现实例。

<a id="call-the-extension"></a>
## 调用扩展

将已有 Grain 引用转换为扩展接口：

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="use_diagnostics_extension":::
扩展引用仍保有相同的 Grain 标识，并不会寻址另一个 Grain。

扩展调用会执行传入的 [Grain 调用筛选器](interceptors.md)。筛选器不应假定每个 `ImplementationMethod` 都属于 Grain 的实现类。

<a id="per-activation-components"></a>
## 每个激活实例的组件

框架组件可以使用 <xref:Orleans.Runtime.IGrainContext> 的组件 API 和 `GetGrainExtension<T>()` 来提供某个激活实例专用的扩展。这些 API 面向控制 Grain 激活实例初始化过程的运行时集成。应用程序代码通常应使用 `AddGrainExtension` 和通过构造函数注入的依赖项，而不是在激活期间修改 Grain 上下文。

避免通过通用扩展暴露可变的 Grain 状态。这样做会绕过 Grain 的领域不变量，并使基础设施与实现细节耦合。
