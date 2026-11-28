---
title: 消息传输中间件
description: 通过装饰 Orleans 消息传输连接器和侦听器，实现跨组件的通用连接行为。
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="message-transport-middleware"></a>

# 消息传输中间件

Orleans 通过消息传输连接器和侦听器建立 Silo 之间以及客户端到网关的连接。传输中间件在这些组件创建或接受连接之前对其进行装饰。

<xref:Orleans.Connections.Transport.IMessageTransportConnectorMiddleware> 适用于传出连接器。<xref:Orleans.Connections.Transport.IMessageTransportListenerMiddleware> 适用于传入侦听器。Orleans 按依赖注入注册顺序应用每个已注册的中间件实例。

Orleans 使用传输中间件应用 TLS。有关内置安全配置，请参阅[使用 TLS 保护 Orleans 连接](transport-layer-security.md)。

<a id="register-middleware"></a>

## 注册中间件

将连接器和侦听器中间件注册为单例服务：

:::code language="csharp" source="./snippets/connection-middleware/ConnectionMiddlewareExamples.cs" id="RegisterMiddleware":::

中间件实例可能针对多个连接器或侦听器调用，因此应将每个连接的状态保存在返回的装饰器或最终生成的 <xref:Orleans.Connections.Transport.MessageTransport> 中。

<a id="decorate-outbound-connectors"></a>

## 装饰传出连接器

连接器中间件接收下一个 <xref:Orleans.Connections.Transport.MessageTransportConnector>，并返回 Orleans 应使用的连接器。装饰器可以观察连接尝试、选择其他传输层，或包装返回的消息传输层：

:::code language="csharp" source="./snippets/connection-middleware/ConnectionMiddlewareExamples.cs" id="ConnectorDecorator":::

将 <xref:Orleans.Connections.Transport.MessageTransportConnector.Features>、`IsValid` 和释放操作委托给内部连接器，除非中间件有意改变这些保证。

Orleans 拥有中间件返回的连接器装饰器。宿主释放期间，连接工厂关闭连接建立的准入，等待已获准的尝试完成，并释放装饰后的连接器链。每个装饰器释放自身资源，并将释放操作委托给内部连接器。服务提供程序拥有已注册的传输连接器，并单独释放它。

<a id="decorate-inbound-listeners"></a>

## 装饰传入侦听器

侦听器中间件接收下一个 <xref:Orleans.Connections.Transport.MessageTransportListener>，并返回 Orleans 应绑定并从中接受连接的侦听器：

:::code language="csharp" source="./snippets/connection-middleware/ConnectionMiddlewareExamples.cs" id="ListenerDecorator":::

保留侦听器名称，以便 Orleans 按角色选择 Silo 和网关侦听器。当内部侦听器已停止时返回 `null`（在 `AcceptAsync` 中），以符合侦听器契约。

<a id="migrate-byte-stream-middleware"></a>

## 迁移字节流中间件

先前通过 `ClientConnectionOptions`、`SiloConnectionOptions` 或 `IConnectionMiddleware` 注册的自定义行为，应迁移至上文所示的连接器和侦听器装饰器。将传出行为注册为连接器中间件，将传入行为注册为侦听器中间件。侦听器装饰器可以使用侦听器名称独立配置 `silo` 和 `gateway` 角色。

消息传输请求契约规定了帧格式、完成操作及缓冲区生命周期。装饰器在应用每个连接的行为时应保持这些契约。

<a id="see-also"></a>

## 另请参阅

- <xref:Orleans.Connections.Transport.MessageTransport>
- <xref:Orleans.Connections.Transport.MessageTransportConnector>
- <xref:Orleans.Connections.Transport.MessageTransportListener>
- [使用 TLS 保护 Orleans 连接](transport-layer-security.md)
- [服务器配置](configuration-guide/server-configuration.md)
- [客户端配置](configuration-guide/client-configuration.md)
