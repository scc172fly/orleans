---
title: Orleans 3.x 到 7.x 的归档迁移说明
description: 跨越 Orleans 3 到 7 的不兼容标识、托管和序列化边界的历史指南。
ms.date: 08/08/2026
ms.topic: how-to
---

<a id="archived-orleans-3x-to-7x-migration-notes"></a>

# Orleans 3.x 到 7.x 的归档迁移说明

> [!WARNING]
> Orleans 3.x 和 Orleans 7.x 已停止支持。仅使用这些说明迁移到可作为后续升级起点的中间代码库，然后继续按照 [Orleans 7.x 到 10.x](7-to-10.md) 迁移。

<a id="starting-and-target-assumptions"></a>

## 起始版本与目标版本的前提条件

这些说明适用于必须先升级到 Orleans 7 的 Orleans 3.x 应用程序。此迁移不具备传输协议兼容性。Orleans 3 和 Orleans 7 的 Silo 无法组成混合集群，因此请将 Orleans 7 部署在独立集群中，并制定针对应用程序的状态迁移方案。

<a id="required-architectural-changes"></a>

## 必需的体系结构变更

- 在 Silo 项目中引用 [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server)，在客户端项目中引用 [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client)，在共享契约项目中引用 [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk)。
- 移除旧版 MSBuild 代码生成器和 `Microsoft.Orleans.OrleansRuntime` 程序包。
- 将 `Microsoft.Orleans.OrleansServiceBus` 替换为 [Microsoft.Orleans.Streaming.EventHubs](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.EventHubs)。应用程序使用提醒器和流式传输功能时，显式添加 [Microsoft.Orleans.Reminders](https://www.nuget.org/packages/Microsoft.Orleans.Reminders) 和 [Microsoft.Orleans.Streaming](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) 引用。
- 移除 Application Parts 配置。Orleans 源代码生成器会发现应用程序类型。
- 使用 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 及 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 和 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>。
- 更新 <xref:Orleans.Grain.OnActivateAsync*> 和 <xref:Orleans.Grain.OnDeactivateAsync*> 的重写方法，使其采用 Orleans 7 中包含取消令牌和停用原因的签名。
- 为应用程序类型添加 <xref:Orleans.GenerateSerializerAttribute> 和稳定的 <xref:Orleans.IdAttribute> 值。
- 用 Orleans 7 基于字符串的标识模型替换旧版 Grain、接口和流标识的相关假设。
- 将简单消息流替换为广播通道或持久化流提供程序。
- 将旧版遥测使用者替换为 .NET 指标和基于 <xref:System.Diagnostics.ActivitySource> 的跟踪。

旧的 <xref:Orleans.Hosting.GrainCallFilterServiceCollectionExtensions.AddGrainCallFilter*> API 在 Orleans 7 之前已被移除。在 <xref:Orleans.Hosting.ISiloBuilder> 或 <xref:Orleans.Hosting.IClientBuilder> 上注册传入和传出调用筛选器。

有关迁移到 Orleans 7 的示例的逐项记录，请参阅 [dotnet/orleans 问题 #8035](https://github.com/dotnet/orleans/issues/8035)。

<a id="why-this-boundary-requires-conversion"></a>

## 为什么跨越此边界需要转换

Orleans 7 更改了两项由 Orleans 10 延续的持久契约：

- Grain、Grain 接口和流标识迁移至 <xref:Orleans.Runtime.GrainId> 和 <xref:Orleans.Runtime.StreamId> 使用的基于字符串的类型、命名空间和键表示形式。
- Orleans 传输序列化器被基于 <xref:Orleans.GenerateSerializerAttribute>、<xref:Orleans.IdAttribute> 和 <xref:Orleans.AliasAttribute> 的版本容错序列化器取代。

这些变更使应用程序在 Orleans 7 检查点之后更易于演进，但不会使 Orleans 3 标识或有效载荷自动兼容。在 Orleans 7 集群读取生产数据之前，定义并测试转换过程。

<a id="map-identities-and-serialized-state"></a>

### 映射标识和序列化状态

为每个持久标识创建显式映射：

- 将旧 Grain 类型映射到 Orleans 7 及后续版本的类型名称时，保留每个 GUID、整数、字符串或复合 Grain 键的领域值。当该名称必须独立于 CLR 类名称时，使用 <xref:Orleans.GrainTypeAttribute>。
- 将流命名空间和 GUID 键映射为稳定的 <xref:Orleans.Runtime.StreamId> 命名空间和键字符串。保持提供程序名称不变，除非迁移有意将订阅移到另一个提供程序。
- 添加稳定的序列化成员 ID，并在重命名序列化类型之前添加别名。不要在 Orleans 7 检查点之后重复使用 ID 或别名。
- 将已序列化的 Grain 引用、队列中的消息、提醒器、流订阅和提供程序元数据都纳入转换范围，而不仅仅是 Grain 状态数据行。

版本容错序列化器约束的是转换之后的兼容性变更。不要假定它可以解码 Orleans 3 传输序列化器或使用不同设置的存储序列化器写入的有效载荷。使用有代表性的生产数据验证每种存储类型。有关 Orleans 10 契约，请参阅 [Grain 标识](../grains/grain-identity.md) 和 [Orleans 序列化](../host/configuration-guide/serialization.md)。

<a id="replace-simple-message-streams"></a>

### 替换简单消息流

简单消息流已在 Orleans 7 中移除。根据应用程序所依赖的行为选择替代方案：

- 使用[广播通道](../streaming/broadcast-channel.md)，向隐式订阅者提供非持久、尽力而为的投递。广播通道不保留事件、不重放消息，也不支持显式订阅。
- 应用程序需要显式订阅、持久订阅元数据、外部消息代理、重放或由提供程序支持的投递时，使用 [Orleans 流提供程序](../streaming/index.md)。

不要仅因为两个 API 都能投递通知就选择替代方案。记录每个旧提供程序名称、命名空间、键和订阅到新抽象的映射方式，然后测试重复消息、延迟消息和丢失消息的行为。有关当前行为对照表，请参阅[选择 Orleans 消息传递抽象](../streaming/streams-why.md)。

<a id="replace-legacy-telemetry-consumers"></a>

### 替换旧版遥测使用者

`Microsoft.Orleans.TelemetryConsumers.*` 程序包和旧版遥测使用者模型已被移除。Orleans 10 生成结构化日志、`Microsoft.Orleans` <xref:System.Diagnostics.Metrics.Meter> 指标以及 <xref:System.Diagnostics.ActivitySource> 跟踪。使用应用程序的日志记录及 OpenTelemetry 管道替换旧使用者，并在每个参与的 Silo 和客户端上注册活动传播。请参阅 [Orleans 可观测性](../host/monitoring/index.md)。

<a id="account-for-separated-feature-packages"></a>

### 考虑独立的功能程序包

流式传输、提醒器和事务已从核心程序包中分离。仅在使用相应功能的位置添加 `Microsoft.Orleans.Streaming`、`Microsoft.Orleans.Reminders` 和 `Microsoft.Orleans.Transactions`。以前作为 <xref:Orleans.Grain> 实例方法提供的 API，在这些程序包中成为扩展方法，因此未限定的调用可能需要 `this.`，例如 `this.GetReminders()` 或 `this.GetStreamProvider("provider")`。

<a id="state-and-deployment-boundary"></a>

## 状态与部署边界

Orleans 7 中的 Grain 和流标识以及传输序列化器发生了不兼容变更。在验证以下内容之前，不要将新集群指向生产状态：

- 旧 Grain 标识如何映射到新的字符串标识。
- 如何转换或读取每个持久化有效载荷。
- 如何重新创建或迁移提醒器和流订阅。
- 如何切换流量而不让两个集群同时处理相同的逻辑实体。

优先采用脱机导出、转换、导入流程，或采用具有幂等写入能力的应用程序级桥接方案。在 Orleans 7 集群完成验证之前保留 Orleans 3 的数据恢复点。

<a id="continue-to-orleans-10"></a>

## 继续迁移到 Orleans 10

Orleans 7 应用程序稳定后：

1. 将其更新到最新 Orleans 7.2 补丁版本。
1. 遵循[将 Orleans 7.x 升级到 10.x](7-to-10.md)。
1. 为 Orleans 8.2、Orleans 9.2 和 Orleans 10.x 分别设置独立的部署检查点。

<a id="checklist"></a>

## 检查清单

- [ ] 使用当前的程序包和托管模式构建 Orleans 7 代码库。
- [ ] 定义 Grain、流、提醒器及状态标识映射。
- [ ] 转换并验证有代表性的持久化有效载荷。
- [ ] 在独立集群中部署 Orleans 7。
- [ ] 验证是否能够回滚到 Orleans 3 恢复点。
- [ ] 在 Orleans 7.2 上达到稳定状态后，再按顺序继续升级到 Orleans 10。
