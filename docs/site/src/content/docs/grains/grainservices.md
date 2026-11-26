---
title: "Grain 服务"
description: "实施 silo- Orleans grains的居民分隔服务。"
ms.date: 08/12/2026
ms.topic: how-to
---

<a id="grain-services"></a>

# Grain 服务

A grain 服务为 silo- 支持驻地、远程呼叫服务 grain 功能。 Orleans 开始于每个 silo 并保留它 silo 寿命寿命, A <xref:Orleans.Runtime.Services.GrainServiceClient`1> 地图 grain 向负责此事的部门提供服务。

提醒提醒是这种模式的一个实例。 大多数应用工作量应定期使用 grains 或主机服务; 使用 grain 当框架一级的能力必须分于每个 silo.

<a id="define-and-implement-the-service"></a>

## 定义和实施服务

服务界面来自 <xref:Orleans.Services.IGrainService>:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="index_grain_service_interface":::
产生 <xref:Orleans.Runtime.GrainService>执行效果:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="index_grain_service":::
只有当服务需要在这些生命周期点工作时,才覆盖 <xref:Orleans.Runtime.GrainService.Init*>、 <xref:Orleans.Runtime.GrainService.Start*>、 <xref:Orleans.Runtime.GrainService.StartInBackground*>和 <xref:Orleans.Runtime.GrainService.Stop*> 。 Grain 服务不是普通的 grains:它们没有稳定的应用程序身份,闲置时不收集,也不迁移。

<a id="create-the-grain-facing-client"></a>

## 创建 grain-向上客户端

定义客户界面和代理服务器:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="index_grain_service_client":::
<xref:Orleans.Runtime.Services.GrainServiceClient`1.GetGrainService*> 一致地将调用 grain 的地图绘制为服务分区。其他超载支持明确的 silo 或散列路径,以进行高级执行。

`IIndexService` 是 silo-居民服务公司执行的运行时间合同。 `IIndexServiceClient` 是代理商执行的 grain-直达依赖注射合同。保持这些作用:应用程序 grains 呼叫客户,客户将每个呼叫都转到 `IIndexService` 实例。

<a id="register-and-consume-the-service"></a>

## 注册和消费服务

注册服务及其客户:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="register_index_grain_service":::
将客户端注入正常 grain:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="use_index_grain_service":::
注册并输入相同的客户界面。在此示例中,登记和建筑师注射使用 `IIndexServiceClient`登记 `IIndexServiceClient` 要求 `IIndexService` 中在 grain 建筑师无法解决依赖关系,因为这些是不同的服务类型。

客户可以前往远程 silo; 不要假设通话是本地的。

Grain 服务执行动保单由 [Microsoft.Orleans.Runtime](https://www.nuget.org/packages/Microsoft.Orleans.Runtime)提供, silo 申请通过 `Microsoft.Orleans.Server`收到。

用于 Orleans 测试的汇编执行情况见 [`TestGrainService.cs`](https://github.com/dotnet/orleans/blob/main/test/Orleans.Runtime.Tests/GrainServiceTests/TestGrainService.cs)。
