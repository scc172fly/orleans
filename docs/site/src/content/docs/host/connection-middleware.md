---
title: "信件传输中器"
description: "装饰 Orleans 电文传输连接器和听众,并具有交叉连接行为。"
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="message-transport-middleware"></a>

# 信件传输中器

Orleans 建立 silo-silo 和客户端对端通道连接,通过电文传输连接器和收听器。运输中继器在创建或接受连接之前先对这些部件进行装饰。

<xref:Orleans.Connections.Transport.IMessageTransportConnectorMiddleware> applies to outbound connectors. <xref:Orleans.Connections.Transport.IMessageTransportListenerMiddleware> applies to inbound listeners. Orleans applies every registered middleware instance in dependency-injection registration order.

Orleans 使用运输中器来应用 TLS。 内建安全配置见 [与 TLS 连接的安全 Orleans](transport-layer-security.md) 。

<a id="register-middleware"></a>

## 注册中器

注册连接器和收听器中继器作为单子服务:

:::code language="csharp" source="./snippets/connection-middleware/ConnectionMiddlewareExamples.cs" id="RegisterMiddleware":::

中件实例可以被多个连接器或听众引用,因此在返回的装饰器或由此产生的 <xref:Orleans.Connections.Transport.MessageTransport>中保持每个连接状态。

<a id="decorate-outbound-connectors"></a>

## 装饰出外连接器

连接器中继器接收下一个 <xref:Orleans.Connections.Transport.MessageTransportConnector> 并返回应使用的连接器 Orleans 。 设计师可以观察连接尝试, 选择其他运输方式, 或者包扎回电文传输方式 :

:::code language="csharp" source="./snippets/connection-middleware/ConnectionMiddlewareExamples.cs" id="ConnectorDecorator":::

代表 <xref:Orleans.Connections.Transport.MessageTransportConnector.Features>、 `IsValid`和处置,除非中器故意改变这些保证。

Orleans 拥有由中器返回的连接器装饰器。在主机处置过程中,连接厂关闭了连接建立许可,等待承认尝试,并处理装饰连接器链。每个设计师处置自己的资源,将代表的处置权交给内部连接器。服务供应商拥有注册运输连接器并将其单独处置。

<a id="decorate-inbound-listeners"></a>

## 装饰入境听众

听众中继器接收下一个 <xref:Orleans.Connections.Transport.MessageTransportListener> ,并返回应约束和接受的听众 Orleans :

:::code language="csharp" source="./snippets/connection-middleware/ConnectionMiddlewareExamples.cs" id="ListenerDecorator":::

保留听众姓名 Orleans 选项可选择 silo 以及按角色划分的听众。 `null` 调自 `AcceptAsync` 当心听者停止, 与倾听者合同一致的时候。

<a id="migrate-byte-stream-middleware"></a>

## 移动流字节流中中器

先前通过 `ClientConnectionOptions`、 `SiloConnectionOptions`或 `IConnectionMiddleware` 注册的自定义, 或迁移到上面显示的连接器和收听器设计器。 登记出局行为为连接器中件, 输入行为为听众中件。 监听器设计师可以使用听众姓名独立配置 `silo` 和 `gateway` 角色。

信息运输请求合同拥有设计、完成和缓冲寿命。 装饰者在使用每次连接行为的同时保存这些合同。

<a id="see-also"></a>

## 另请参阅

- <xref:Orleans.Connections.Transport.MessageTransport>
- <xref:Orleans.Connections.Transport.MessageTransportConnector>
- <xref:Orleans.Connections.Transport.MessageTransportListener>
- [与 TLS 连接的安全 Orleans](transport-layer-security.md)
- [服务器配置](configuration-guide/server-configuration.md)
- [客户配置](configuration-guide/client-configuration.md)
