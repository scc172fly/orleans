---
title: "广播频道"
description: "配置和使用 Orleans 广播频道,具有正确的身份和发送语义。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="broadcast-channels"></a>

# 广播频道

由 [`Microsoft.Orleans.BroadcastChannel`](https://www.nuget.org/packages/Microsoft.Orleans.BroadcastChannel) 软件包提供的广播频道向 grains提供非持久性、隐含的扇门。它们不使用队列,保留历史,从日志中重试或保持明确的订阅。在损失可以接受的情况下,使用它们进行最佳努力通知;当事件或订阅需要耐久性或重播时使用 [Orleans 流流](index.md) 。

<a id="identity-and-routing"></a>

## 身份和路由

广播作家由配置的 **提供者名称** 和 <xref:Orleans.BroadcastChannel.ChannelId>选择。 <xref:Orleans.BroadcastChannel.ChannelId.Create*> 创建了一个识别符,其命名空间和键子有两个不同的路由作用:

- 下 **命名空间** 匹配 <xref:Orleans.ImplicitChannelSubscriptionAttribute> 选择订阅者时的语句 grain 类型。
- 下 **键键** 地图的图中,一个用户主键 grain 每项匹配的标识 grain 类型。

出版并不列出所有当前激活的列表。 它处理匹配虚拟 grain 身份,必要时激活 <xref:Orleans.BroadcastChannel.DefaultChannelIdMapper> 每次匹配的频道密钥解释 grain 类型:它使用字符串键的原始文字 grain,为以图形界面为键的用户界面 grain,然后对整键键的文字进行小数分析。 grain. The <xref:Orleans.BroadcastChannel.ChannelId.Create*> 超载,可接受 <xref:System.Guid> 格式化 GUID 键; 对整数键使用小数字符串。 自定义 <xref:Orleans.BroadcastChannel.IChannelIdMapper> 实施可以改变这一绘图。

提供方名称和频道命名空间是独立的。 它们可以按常规使用相同的字符串, 但供应商注册并不能使该字符串成为频道命名空间 。

<a id="configure-silos-and-clients"></a>

## 配置 silos 和客户端

a 在每个 silo 和 <xref:Orleans.Hosting.ChannelHostingExtensions.AddBroadcastChannel*>上都有有名广播供应商的登记册:

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Silo/Program.cs" id="broadcast_channel_silo_program":::

发布客户端的 Orleans 客户端必须注册同一供应商名称和兼容选项:

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Client/Program.cs" id="broadcast_channel_client_program":::

<xref:Orleans.BroadcastChannel.BroadcastChannelOptions.FireAndForgetDelivery> defaults to `true`. In that mode, <xref:Orleans.BroadcastChannel.IBroadcastChannelWriter`1.Publish*> starts subscriber calls and returns without awaiting them; subscriber exceptions are logged and aren't returned to the publisher. Setting it to `false` awaits all subscriber callbacks and propagates failures as an aggregate exception. Neither mode adds persistence, retry, replay, or exactly-once processing.

<a id="define-a-subscriber-grain"></a>

## 定义订阅者 grain

<a id="define-a-consumer-grain"></a>

以隐含频道订阅方式标记 grain 类并安装 <xref:Orleans.BroadcastChannel.IOnBroadcastChannelSubscribed>。在 Orleans 提供频道订阅时,附加回调:

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Silo/LiveStockGrain.cs" id="broadcast_channel_live_stock_grain":::

无参数属性符合所有非空通道命名空间。 通过一个匹配的命名空间, 使用 <xref:Orleans.RegexImplicitChannelSubscriptionAttribute> 来显示模式, 或者提供自定义的名称空间前置 。

<xref:Orleans.BroadcastChannel.IBroadcastChannelSubscription.Attach*> 选择有效载荷类型和供应项目以及错误回调。频道订阅是隐含的元数据绑定,因此没有明确的订阅或不订阅操作。

<a id="publish"></a>

## 出版

<a id="publish-messages-to-a-broadcast-channel"></a>

以提供者姓名解决 <xref:Orleans.BroadcastChannel.IBroadcastChannelProvider> ,构建频道ID,获得打字作家并公布:

:::code language="csharp" source="./snippets/broadcastchannel/BroadcastChannel.Silo/Services/StockWorker.cs" id="broadcast_channel_stock_worker":::

在此样本中, 频道命名空间是 `live-stock-ticker` , 密钥是 <xref:System.Guid.Empty>, 所以每个匹配的 GUID 键紧用户类型 grain 都会在 grain 身份上接收信件。 使用客户、 租户、 设备或其他域名密钥来瞄准相应的身份 。

<a id="broadcast-channels-compared-with-streams"></a>

## 与流比的广播频道

<a id="broadcast-channels-vs-streams"></a>
<a id="when-to-use-broadcast-channels"></a>
<a id="when-to-use-streams"></a>

| 能力 | 广播频道 | Orleans 流流 |
|---|---|---|
| Fan-out | 一个 grain 匹配的用户每个身份 grain 类型类型 | 每项明示和默示的订阅 |
| 订阅模式 | 仅隐含 grain 元数据 | 明示和默示 |
| 事件持续 | 无无无无 | Provider-dependent |
| 订阅的持久性 | 无需 | 明确记录取决于 `PubSubStore` |
| 重放播放 | 无 | Provider-dependent |
| 出版出版完成完成 | 默认的火和火; 可选择等待回召 | 接受供货商,而不是消费者完成一般消费 |
| 外部经纪人的一体化 | 无 | 通过流源供应商提供 |

选择指南见 [选择 Orleans 短信抽象](streams-why.md) 。
