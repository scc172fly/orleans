---
title: 构建并恢复 Orleans 流式应用程序
description: 在本地运行生产者和隐式消费者，然后配置持久化流提供程序并验证恢复。
ms.date: 08/11/2026
ms.topic: tutorial
---

<a id="build-and-recover-an-orleans-streaming-application"></a>

# 构建并恢复 Orleans 流式应用程序

本演练使用 [Simple Streaming 示例](https://github.com/dotnet/orleans/tree/main/samples/Streaming/Simple)，追踪事件从生产者 Grain 到隐式消费者的过程。首先使用内存提供程序运行，再切换到 Azure Event Hubs 并验证恢复。

<a id="run-the-local-path"></a>

## 运行本地方案

从空目录开始：

```powershell
git clone https://github.com/dotnet/orleans.git
cd orleans\samples\Streaming\Simple
dotnet build .\Streaming.sln
```

启动 Silo，然后在另一个终端启动客户端：

```powershell
dotnet run --project .\SiloHost
dotnet run --project .\Client
```

默认本地配置在两个主机上注册命名内存流提供程序。客户端请求生产者 Grain 发布整数事件。流标识决定由哪个隐式订阅的消费者 Grain 接收各个事件。

在 Silo 日志中观察以下状态变化：

1. 第一个匹配事件到达时，`ConsumerGrain` 激活。
1. Orleans 提供 <xref:Orleans.Streams.Core.IStreamSubscriptionHandleFactory>。
1. Grain 使用其观察者恢复订阅。
1. `OnNextAsync` 记录事件及其序列令牌。

<xref:Orleans.ImplicitStreamSubscriptionAttribute> 将流命名空间和标识映射到消费者 Grain 标识，并在匹配事件到达时激活该消费者。

<a id="trace-the-code"></a>

## 追踪代码

依次阅读以下文件：

1. [`SiloHost/Program.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/SiloHost/Program.cs) 注册 `PubSubStore` 和流提供程序。
1. [`Grains/ProducerGrain.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/Grains/ProducerGrain.cs) 获取强类型流并发布事件。
1. [`Grains/ConsumerGrain.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/Grains/ConsumerGrain.cs) 声明隐式订阅并附加观察者。
1. [`Client/Program.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/Client/Program.cs) 驱动整个场景。

`PubSubStore` 这一名称具有特殊意义。持久化流提供程序使用它来存储订阅元数据；生产集群中应以此名称注册持久化存储。

<a id="switch-to-a-persistent-provider"></a>

## 切换到持久化提供程序

在非生产订阅中创建 Azure Event Hubs 命名空间、名为 `my-path` 的事件中心、名为 `my-group` 的消费者组，以及一个 Azure Storage 帐户。在示例目录中创建 `Secrets.json`，内容如下，并将两个值替换为连接字符串。不要将 `Secrets.json` 纳入源代码管理。

```json
{
  "DataConnectionString": "<Azure Storage connection string>",
  "EventHubConnectionString": "<Event Hubs namespace connection string>"
}
```

重新启动 Silo 和客户端。启动日志应显示提供程序已从内存流切换为 Azure Event Hubs。Silo 配置如下：

- 用于 `PubSubStore` 的 Azure Table Grain 存储；
- Event Hubs 流提供程序；
- Azure Table 检查点管理器；以及
- 已配置的 Event Hubs 消费者组。

对于已部署的应用程序，应使用托管标识或平台的机密存储替换本地机密，并仅授予必需的数据平面权限。

<a id="verify-recovery"></a>

## 验证恢复

在开始此练习之前，将 `Client/Program.cs` 中的 `Guid.NewGuid()` 替换为固定 GUID，使客户端每次运行时均向同一流发布事件。

1. 正常运行 Silo。
1. 使用 `dotnet run --project .\Client -- --pause-after-checkpoint` 运行客户端。
1. 客户端等待两个 10 秒的检查点间隔，然后停止流拉取代理，而生产者 Grain 继续发布事件。
1. 记录暂停时消费者的序列令牌，以及 Silo 日志中生产者随后报告的十个事件编号。这些事件构成已保存检查点之后的积压。
1. 在拉取代理仍处于暂停状态时终止 Silo 进程，然后停止客户端。保持 Event Hubs 和 Azure Storage 运行。
1. 使用相同的服务 ID、集群 ID、提供程序名称、事件中心和消费者组重新启动 Silo。拉取代理在启动过程中加载已保存的检查点。
1. 验证消费者从积压中收到先前记录的重启前事件编号。它们的序列令牌推进至暂停时令牌之后。
1. 积压处理完毕后，重新启动客户端以恢复生产。

投递保证取决于提供程序。在此 Event Hubs 场景中，消费者可能在故障前后观察到重复事件，因此生产处理程序应具有幂等性，或使用应用程序自有的事件标识进行去重。序列令牌表示提供程序的投递位置；应用程序自有的事件标识则用于业务去重。

<a id="test-failure-boundaries"></a>

## 测试故障边界

在突然终止 Silo 的情况下重复恢复检查。监视队列滞后、投递重试、重复处理、有害事件处理及检查点距今时长。然后使用[流式处理运维](../streaming/streaming-operations.md)，为所选提供程序定义告警和恢复流程。

有关提供程序的选择与保证，请继续阅读[流提供程序](../streaming/stream-providers.md)、[投递语义](../streaming/delivery-semantics.md)和[发布-订阅存储](../streaming/pubsub-storage.md)。
