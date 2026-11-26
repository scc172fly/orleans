---
title: "存档 Orleans 3.x至 7.x 移民便条"
description: "跨越不兼容的 Orleans 3-7身份、托管和序列边界的历史指南。"
ms.date: 08/08/2026
ms.topic: how-to
---

<a id="archived-orleans-3x-to-7x-migration-notes"></a>

# 存档 Orleans 3.x至 7.x 移民便条

> [!WARNING]
> Orleans 3.x和 Orleans 7.x 都得不到支持。 这些注释仅用于到达一个支持的中间代码基,然后持续到 [Orleans 7.x至 10.x](7-to-10.md)。

<a id="starting-and-target-assumptions"></a>

## 起始假设和目标假设

这些注解适用于 Orleans 3. 申请必须首先成为 Orleans 7 应用程序。 这种过渡与电线不兼容。 Orleans 和3和 Orleans 7 silos 无法组成混合组, 所以部署 Orleans 计划具体国家过渡。

<a id="required-architectural-changes"></a>

## 所需建筑变动

- 参考参考资料 [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server) 调自 silo 项目, [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client) 客户项目;以及 [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk) 共同合同项目。
- 删除遗留的 MSBilud 代码生成器和 `Microsoft.Orleans.OrleansRuntime` 软件包。
- 将 `Microsoft.Orleans.OrleansServiceBus` 改为 [Microsoft.Orleans.Streaming.EventHubs](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.EventHubs)。在应用程序使用这些特征时,添加明确的 [Microsoft.Orleans.Reminders](https://www.nuget.org/packages/Microsoft.Orleans.Reminders) 和 [Microsoft.Orleans.Streaming](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) 参考文献。
- 删除应用程序部件配置。 Orleans 源生成器会发现应用类型。
- 使用 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 与 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 和 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>.
- 更新更新 <xref:Orleans.Grain.OnActivateAsync*> 和 <xref:Orleans.Grain.OnDeactivateAsync*> 覆盖到 Orleans 7个取消签字和解除理由签字。
- 将 <xref:Orleans.GenerateSerializerAttribute> 和稳定 <xref:Orleans.IdAttribute> 值添加到应用类型中。
- 替换遗物 grain、接口和流数据身份假设 Orleans 7个基于字符串的身份模型。
- 将简单信件流替换为广播频道或持续流源提供商。
- 将遗留的遥测消费者替换为 .NET 指标和 <xref:System.Diagnostics.ActivitySource>-基于追踪。

旧的 <xref:Orleans.Hosting.GrainCallFilterServiceCollectionExtensions.AddGrainCallFilter*> API 在 Orleans 7. <xref:Orleans.Hosting.ISiloBuilder> 或 <xref:Orleans.Hosting.IClientBuilder>上登记册收发过滤器之前被删除。

详细记录的详细明细的记录 Orleans 迁移到 Orleans 7, 见 [点网/orleans 问题 #8035](https://github.com/dotnet/orleans/issues/8035).

<a id="why-this-boundary-requires-conversion"></a>

## 为什么边界需要转换

Orleans 7项更改了两项 Orleans 10项保留的长期合同:

- Grain、 grain-接口和流特性移到 <xref:Orleans.Runtime.GrainId> 和 <xref:Orleans.Runtime.StreamId>所使用的基于字符串的类型、命名空间和关键表示式。
- Orleans 线性序列器被基于 <xref:Orleans.GenerateSerializerAttribute>、 <xref:Orleans.IdAttribute>和 <xref:Orleans.AliasAttribute>的耐版本序列器取代。

这些修改使得应用程序在 Orleans 7检查站之后更容易发展,但不会自动使 Orleans 3身份或有效载荷兼容。定义和测试在 Orleans 7组之前转换的数据读取生产数据。

<a id="map-identities-and-serialized-state"></a>

### 地图身份和序列状态

明确绘制每个持久身份的地图:

- 保存每个图形界面D、整数、字符串或化合物的域值 grain 键,然后绘制旧 grain 类型 Orleans 7-和7-后型名称。 <xref:Orleans.GrainTypeAttribute> 当该名称必须独立于 CLR 类名称时 。
- 稳定 <xref:Orleans.Runtime.StreamId> 命名空间和密钥字符串的地图流行名空间和 GUID 键。保持提供者名称的稳定,除非迁移有意将订阅移动到另一个提供者。
- 添加稳定的序列器成员身份识别码, 在重新命名序列类型之前添加别名。 不要在 Orleans 7检查站之后再使用身份识别符或别名 。
- 将序列 grain 参考文献、排队式信件、提醒、串流订阅和提供商元数据作为转换的一部分,而不仅仅是 grain- 国家行。

允许版本的序列器对转换后兼容的变化进行管理。不要假定它能够解码由 Orleans 3号线序列器或不同设置的存储序列器所写的有效载荷。使用具有代表性的生产数据核查每个储存型号。见 [Grain 身份](../grains/grain-identity.md) 和 [Orleans 序号](../host/configuration-guide/serialization.md) 10合同中的 Orleans 。

<a id="replace-simple-message-streams"></a>

### 替换简单的信件流

Orleans 7. 根据应用程序所依赖的行为选择替换:

- 使用 [广播频道](../streaming/broadcast-channel.md) 向隐含用户发送非持久性、最佳操作。 广播频道不保留事件, 重放信件或支持明确订阅 。
- 当应用程序需要明确订阅、耐用订阅元数据、外部中介、重播或供应商支持的交付时,使用 [Orleans 流流提供者](../streaming/index.md) 。

不要只因为两个 API 发送通知而选择替换。 请记录每个旧的提供者名称、 命名空间、 密钥和订阅地图如何进行新的抽象, 然后测试重复、 延迟和丢失消息行为 。 请参见当前行为矩阵 [选择 Orleans 短信抽象](../streaming/streams-why.md) 。

<a id="replace-legacy-telemetry-consumers"></a>

### 替换遗留遥测用户

取消了 `Microsoft.Orleans.TelemetryConsumers.*` 软件包和遗留的遥测-消费者模式。 Orleans 10号发出结构化日志、一个 `Microsoft.Orleans` <xref:System.Diagnostics.Metrics.Meter>和 <xref:System.Diagnostics.ActivitySource> 痕迹。用应用程序的伐木和 OpenTelemetry 管道取代旧客户,并登记每个参与方 silo 和客户的活动传播情况。见 [Orleans 易观测](../host/monitoring/index.md)。

<a id="account-for-separated-feature-packages"></a>

### 分离功能包账户

串流、提醒和交易从核心软件包中移出。只有在使用这些特性时才添加 `Microsoft.Orleans.Streaming`、 `Microsoft.Orleans.Reminders`和 `Microsoft.Orleans.Transactions` 。以前作为 <xref:Orleans.Grain> 实例方法出现的API是这些软件包中的扩展方法,因此不合格的调用可能需要 `this.`,例如 `this.GetReminders()` 或 `this.GetStreamProvider("provider")`。

<a id="state-and-deployment-boundary"></a>

## 国家边界和部署边界

Grain 和流身份以及电线序列器在 Orleans 7.中变化不相容,除非你核实:

- 多大 grain 身份映射到新字符串身份。
- 如何转换或阅读每个持续有效载荷。
- 如何重新创建或迁移提醒和流订阅。
- 如何截断交通,而不使用两个组群处理相同的逻辑实体。

最好有一个离线出口/转换/进口流程或一个与无能者连接的应用级桥梁。 保留 Orleans 3数据恢复点,直到 Orleans 7组完成验证。

<a id="continue-to-orleans-10"></a>

## 继续到 Orleans 10

在 Orleans 7应用稳定之后:

1. 更新到最新的 Orleans 7.2补丁。
1. 之后 [升级 Orleans 7.x 到 10.x](7-to-10.md)。
1. 使用单独的部署检查站 Orleans 8.2, Orleans 9.2, 和 Orleans 10.x.

<a id="checklist"></a>

## 核对列表

- [ ] 使用当前软件包和主机模式构建 Orleans 7 代码基。
- [ ] 定义 grain、流、提醒和状态身份图。
- [ ] 转换和验证代表的持续有效载荷。
- [ ] 在一个单独的组别中部署 Orleans 7。
- [ ] 核查返回到 Orleans 3号回收点。
- [ ] 在 Orleans 7.2上稳定,然后按顺序继续到 Orleans 10。
