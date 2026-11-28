---
title: 广播通道
description: 使用正确的标识及投递语义配置和使用 Orleans 广播通道。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="broadcast-channels"></a>
# 广播通道

[`Microsoft.Orleans.BroadcastChannel`](https://www.nuget.org/packages/Microsoft.Orleans.BroadcastChannel) 包提供广播通道，实现面向 Grain 的非持久化隐式扇出。广播通道不使用队列、不保留历史、不从日志重试，也不维护显式订阅。对于允许丢失的尽力而为通知，可使用广播通道；当事件或订阅需要持久化或重放时，请使用 [Orleans 流](index.md)。

<a id="identity-and-routing"></a>
## 标识与路由

通过配置的**提供程序名称**及 <xref:Orleans.BroadcastChannel.ChannelId> 选择广播写入器。<xref:Orleans.BroadcastChannel.ChannelId.Create*> 创建一个标识符，其中的命名空间和键具有不同的路由作用：

- **命名空间**与 <xref:Orleans.ImplicitChannelSubscriptionAttribute> 声明匹配，以选择订阅者 Grain 类型。
- **键**映射到每种匹配的 Grain 类型中一个订阅者 Grain 标识的主键。

发布操作不会枚举当前所有激活实例。它寻址匹配的虚拟 Grain 标识，并在必要时激活对应 Grain。<xref:Orleans.BroadcastChannel.DefaultChannelIdMapper> 针对每种匹配的 Grain 类型解释通道键：对于字符串键 Grain，直接使用原始文本；对于 GUID 键 Grain，解析 GUID 文本；对于整数键 Grain，解析十进制文本。<xref:Orleans.BroadcastChannel.ChannelId.Create*> 中接受 <xref:System.Guid> 的重载会格式化 GUID 键；对于整数键，请使用十进制字符串。自定义 <xref:Orleans.BroadcastChannel.IChannelIdMapper> 实现可以改变该映射方式。

提供程序名称和通道命名空间相互独立。可以按约定使用相同的字符串，但注册提供程序不会自动将该字符串设为通道命名空间。

<a id="configure-silos-and-clients"></a>
## 配置 Silo 和客户端

使用 <xref:Orleans.Hosting.ChannelHostingExtensions.AddBroadcastChannel*> 在每个 Silo 上注册命名广播提供程序：

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Silo/Program.cs" id="broadcast_channel_silo_program":::

负责发布的 Orleans 客户端必须注册相同的提供程序名称及兼容的选项：

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Client/Program.cs" id="broadcast_channel_client_program":::

<xref:Orleans.BroadcastChannel.BroadcastChannelOptions.FireAndForgetDelivery> 默认值为 `true`。在此模式下，<xref:Orleans.BroadcastChannel.IBroadcastChannelWriter`1.Publish*> 发起对订阅者的调用，然后直接返回而不等待这些调用完成；订阅者异常会被记录，但不会返回给发布者。将其设置为 `false` 会等待所有订阅者回调完成，并以聚合异常的形式传播失败。两种模式都不会增加持久化、重试、重放或恰好一次处理的保证。

<a id="define-a-subscriber-grain"></a>
## 定义订阅者 Grain

<a id="define-a-consumer-grain"></a>

使用隐式通道订阅特性标记 Grain 类，并实现 <xref:Orleans.BroadcastChannel.IOnBroadcastChannelSubscribed>。Orleans 提供通道订阅时，附加回调：

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Silo/LiveStockGrain.cs" id="broadcast_channel_live_stock_grain":::

无参数特性匹配所有非空通道命名空间。可以传入命名空间以进行精确匹配、使用 <xref:Orleans.RegexImplicitChannelSubscriptionAttribute> 进行模式匹配，或提供自定义命名空间谓词。

<xref:Orleans.BroadcastChannel.IBroadcastChannelSubscription.Attach*> 选择有效载荷类型，并提供数据项及错误回调。通道订阅是隐式元数据绑定，因此没有显式订阅或取消订阅操作。

<a id="publish"></a>
## 发布消息

<a id="publish-messages-to-a-broadcast-channel"></a>

按提供程序名称解析 <xref:Orleans.BroadcastChannel.IBroadcastChannelProvider>，构造通道 ID，获取强类型写入器，然后发布消息：

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Silo/Services/StockWorker.cs" id="broadcast_channel_stock_worker":::

此示例中的通道命名空间为 `live-stock-ticker`，键为 <xref:System.Guid.Empty>，因此每种匹配的 GUID 键订阅者 Grain 类型中具有该标识的 Grain 都会收到消息。也可以使用客户、租户、设备或其他领域键，以目标标识为接收方。

<a id="broadcast-channels-compared-with-streams"></a>
## 广播通道与流的对比

<a id="broadcast-channels-vs-streams"></a>
<a id="when-to-use-broadcast-channels"></a>
<a id="when-to-use-streams"></a>

| 能力 | 广播通道 | Orleans 流 |
|---|---|---|
| 扇出 | 每种匹配的订阅者 Grain 类型对应一个 Grain 标识 | 每个显式及隐式订阅 |
| 订阅模型 | 仅隐式 Grain 元数据 | 显式和隐式 |
| 事件持久化 | 无 | 取决于提供程序 |
| 订阅持久化 | 无须持久化 | 显式订阅记录依赖 `PubSubStore` |
| 重放 | 不支持 | 取决于提供程序 |
| 发布者完成语义 | 默认发起调用后不等待；可以选择等待回调 | 由提供程序接受，不代表消费者已完成处理 |
| 外部消息代理集成 | 不支持 | 可通过流提供程序实现 |

有关选型指导，请参阅[选择 Orleans 消息传递抽象](streams-why.md)。
