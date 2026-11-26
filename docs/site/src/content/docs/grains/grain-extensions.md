---
title: "Grain 扩展"
description: "将运行时提供的接口添加到 Orleans grains。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-extensions"></a>

# Grain 扩展

Grain 扩展附加一个可调用界面,用于启动 grain 。 Orleans 内部使用取消和流流等功能的扩展。应用程序可用于基础设施行为,而这种行为应该可以跨越许多类型 grain 提供。

扩展期是先进的一体化机制。 grain 当行为是 grain域合同

<a id="define-an-extension"></a>

## 定义扩展名

接口来自 <xref:Orleans.Runtime.IGrainExtension> ,使用正常 grain 方法返回类型:

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="diagnostics_extension":::
Orleans 在构建时生成延期请求和参考代码。

<a id="register-the-extension"></a>

## 登记延期

登记 silo的默认履行情况:

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="register_diagnostics_extension":::
实施是通过为目标 grain 背景提供依赖注射来实施的。

<a id="call-the-extension"></a>

## 延长的调用

刻录现有扩展界面的 grain 引用 :

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="use_diagnostics_extension":::
扩展引用保留了相同的 grain 身份。 它不处理单独的 grain。

收到用于扩展呼叫的 [grain 调用过滤器](interceptors.md) 运行。过滤器不应假设每个 `ImplementationMethod` 都属于 grain 执行类别。

<a id="per-activation-components"></a>

## 活性部件

框架组件可以使用 <xref:Orleans.Runtime.IGrainContext> 组件API和 `GetGrainExtension<T>()` 提供激活特定扩展。这些API用于控制 grain 启动设置的运行时间整合。应用程序代码通常应使用 `AddGrainExtension` 和构建器输入依赖性,而不是在激活过程中突变一个 grain 环境。

通过通用扩展避免暴露可变 grain 状态。 这样做绕过 grain域变量和夫妇基础设施,而忽略了实施细节。
