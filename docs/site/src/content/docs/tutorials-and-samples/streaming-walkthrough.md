---
title: "构建并恢复 Orleans 流流应用程序"
description: "在当地运行生产商和隐含消费者,然后配置一个持续流供应商并核查回收情况。"
ms.date: 08/11/2026
ms.topic: tutorial
---

<a id="build-and-recover-an-orleans-streaming-application"></a>

# 构建并恢复 Orleans 流流应用程序

此“ 步进” 使用 [简单流流样本](https://github.com/dotnet/orleans/tree/main/samples/Streaming/Simple) 来跟踪从生产者 grain 到隐含消费者的事件。 您先与一个内线供应商运行, 然后切换到 Azure 事件枢纽并核实回收情况 。

<a id="run-the-local-path"></a>

## 运行本地路径

从空目录中 :

```powershell
git clone https://github.com/dotnet/orleans.git
cd orleans\samples\Streaming\Simple
dotnet build .\Streaming.sln
```

启动 silo,然后客户端在另一个终端中:

```powershell
dotnet run --project .\SiloHost
dotnet run --project .\Client
```

默认本地配置在两个主机上都登记名为模拟流提供商的名号。 客户端要求制片人发布整数事件 grain 。 流身份决定了哪些暗含订阅的消费者 grain 接收每个事件 。

在 silo 日志中观察这些转换:

1. `ConsumerGrain` 当第一次匹配事件到达时启动 。
1. Orleans 供应 <xref:Orleans.Streams.Core.IStreamSubscriptionHandleFactory>。
1. grain 继续与其观察员订阅。
1. `OnNextAsync` 记录事件及其序列符号。

<xref:Orleans.ImplicitStreamSubscriptionAttribute> maps the stream namespace and identity to the consumer grain identity and activates that consumer when matching events arrive.

<a id="trace-the-code"></a>

## 追踪代码

阅读这些文件以下列方式排列:

1. [`SiloHost/Program.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/SiloHost/Program.cs) 登记 `PubSubStore` 和流源提供者。
1. [`Grains/ProducerGrain.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/Grains/ProducerGrain.cs) 获得打印流并发布事件。
1. [`Grains/ConsumerGrain.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/Grains/ConsumerGrain.cs) 宣布默示签署并附观察员。
1. [`Client/Program.cs`](https://github.com/dotnet/orleans/blob/main/samples/Streaming/Simple/Client/Program.cs) 驱动着设想情景。

`PubSubStore` 名称很重要。 持续流源供应商使用该名称来订阅元数据;在生产组群中登记以该名称储存的耐久性。

<a id="switch-to-a-persistent-provider"></a>

## 切换到持续提供商

创建 Azure 事件枢纽命名空间、 名为 `my-path`的事件枢纽、 名为 `my-group`的消费者组和在非生产订阅中创建 Azure 存储账户。 在样本目录中创建 `Secrets.json` , 以此内容替换连接字符串中的这两个值。 保持 `Secrets.json` 外部源控制 。

```json
{
  "DataConnectionString": "<Azure Storage connection string>",
  "EventHubConnectionString": "<Event Hubs namespace connection string>"
}
```

重新启动 silo 和客户端。 启动日志应该报告供应商从模拟流向切换到 Azure 事件枢纽。 silo 配置 :

- Azure 表表表一表格 grain 储存用于 `PubSubStore`;
- 活动枢纽流提供者;
- Azure 表表检查器;
- 配置的事件枢纽消费组。

对于已部署的应用,用管理下的身份或平台的秘密储存来取代当地机密,只给予所需的数据飞机许可。

<a id="verify-recovery"></a>

## 核查回收

在这次演习之前,替换 `Guid.NewGuid()` 内 `Client/Program.cs` 使用固定 GUID , 这样每个客户端都向同一流发布 。

1. 通常运行 silo 。
1. 用 `dotnet run --project .\Client -- --pause-after-checkpoint`运行客户端。
1. 客户允许两段10秒的检查间隔,然后停止流流拉动剂,而生产者 grain 则继续出版。
1. 记录生产商在 silo 日志中报告的冻结的消费者序列符号和下10个事件编号。这些事件构成储存检查站以外的积压。
1. 在拖拉器暂停时终止 silo 进程, 然后停止客户端。 休假事件枢纽和存储 Azure 运行中 。
1. 以相同的服务身份、 CM ID、 供应商名称、 中枢和消费者组重新开始 silo 。 在启动时, 拉动代理器装载存储的检查站 。
1. 验证消费者是否从积压中收到已记录的启动前事件编号。 它们的序列符号会超过冻结的标记。
1. 客户在积压工作耗尽后重新开始生产。

提供担保是特定供应商的。 在这种事件枢纽方案下,消费者可以观察到失败前后重复的情况,因此生产处理者应该拥有一定的能力或使用应用程序所属的事件身份进行复制。序列牌代表提供者交货的位置;应用程序所属的事件身份支持业务的减少。

<a id="test-failure-boundaries"></a>

## 测试失败边界

突然终止 silo 时重复恢复检查。 监视队列滞后、 交付回溯、 重复处理、 毒物事件处理和检查站年龄。 然后使用 [流流业务](../streaming/streaming-operations.md) 来定义选定供应商的提醒和回收程序 。

对于提供商的选择和担保,继续使用 [流流源服务提供商](../streaming/stream-providers.md)、 [交付语义](../streaming/delivery-semantics.md)和 [阴部子项存储](../streaming/pubsub-storage.md)。
