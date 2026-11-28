---
title: Grain 服务
description: 为 Orleans Grain 实现驻留于 Silo 的分区服务。
ms.date: 08/12/2026
ms.topic: how-to
---

<a id="grain-services"></a>

# Grain 服务

Grain 服务是驻留于 Silo、可远程调用的服务，用于支持 Grain 功能。Orleans 在每个 Silo 上启动一个实例，并在该 Silo 的整个生命周期内保留它。<xref:Orleans.Runtime.Services.GrainServiceClient`1> 将发起调用的 Grain 映射到负责为其提供服务的实例。

提醒器是此模式的一个示例。大多数应用程序工作负载应使用普通 Grain 或托管服务；仅当框架级功能必须分区到每个 Silo 时，才使用 Grain 服务。

<a id="define-and-implement-the-service"></a>

## 定义并实现服务

服务接口派生自 <xref:Orleans.Services.IGrainService>：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="index_grain_service_interface":::
实现类继承自 <xref:Orleans.Runtime.GrainService>：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="index_grain_service":::
仅当服务需要在相应生命周期阶段执行工作时，才重写 <xref:Orleans.Runtime.GrainService.Init*>、<xref:Orleans.Runtime.GrainService.Start*>、<xref:Orleans.Runtime.GrainService.StartInBackground*> 和 <xref:Orleans.Runtime.GrainService.Stop*>。Grain 服务不是普通 Grain：它们不具有稳定的应用程序标识，不会因空闲而被回收，也不会迁移。

<a id="create-the-grain-facing-client"></a>

## 创建面向 Grain 的客户端

定义客户端接口及代理：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="index_grain_service_client":::
<xref:Orleans.Runtime.Services.GrainServiceClient`1.GetGrainService*> 将发起调用的 Grain 一致地映射到某个服务分区。其他重载支持显式 Silo 或哈希路由，供高级实现使用。

`IIndexService` 是由驻留于 Silo 的服务实现的运行时契约。`IIndexServiceClient` 是由代理实现、面向 Grain 的依赖注入契约。应区分两者的职责：应用程序 Grain 调用客户端，客户端将每次调用路由到 `IIndexService` 实例。

<a id="register-and-consume-the-service"></a>

## 注册并使用服务

同时注册服务和客户端：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="register_index_grain_service":::
将客户端注入普通 Grain：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="use_index_grain_service":::
注册和注入时应使用同一客户端接口。在此示例中，注册与构造函数注入都使用 `IIndexServiceClient`。若注册 `IIndexServiceClient`，却在 Grain 构造函数中请求 `IIndexService`，则依赖解析会失败，因为它们是不同的服务类型。

客户端可以将调用路由至远程 Silo；不要假定调用始终留在本地。

Grain 服务实现 API 由 [Microsoft.Orleans.Runtime](https://www.nuget.org/packages/Microsoft.Orleans.Runtime) 提供，Silo 应用程序通过 `Microsoft.Orleans.Server` 获得该包。

有关 Orleans 测试所使用的可编译实现，请参阅 [`TestGrainService.cs`](https://github.com/dotnet/orleans/blob/main/test/Orleans.Runtime.Tests/GrainServiceTests/TestGrainService.cs)。
