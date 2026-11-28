<a id="orleans-samples"></a>

# Orleans 示例

本目录是 Orleans 官方示例的权威来源。

<!-- Generated from gallery.json by Update-Readme.ps1. -->

从其他 Microsoft 仓库导入的示例在索引和 `gallery.json` 中保留原仓库地址，原始许可证随示例一并保留。

<a id="build-and-validate"></a>

## 构建和验证

在仓库根目录运行以下命令：

```powershell
pwsh ./samples/Validate-Samples.ps1
```

该命令检查示例清单，并构建 `Samples.slnx` 中的所有项目。使用外部云服务的示例仅在运行时需要这些服务，编译时不需要。

<a id="featured-samples"></a>

## 精选示例

| 示例 | 说明 | 原始来源 |
| --- | --- | --- |
| [文字冒险游戏](Adventure) | 演示 Grain、外部客户端和应用程序建模的文字冒险游戏。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [Chirper 社交网络](Chirper) | 使用持久化、观察者和可重入 Grain 的社交网络示例。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [持久化工作流](DurableWorkflows) | 由 Aspire 托管的应用，演示持久化任务工作流：扇出、带截止时间的信号、协作式取消、Saga 和副本恢复。 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [GPS 跟踪器](GPSTracker) | 集成 Orleans 与 ASP.NET Core SignalR 的物联网位置跟踪器。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [Hello World](HelloWorld) | 用于定义和调用 Grain 的最小完整 Orleans 应用。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [日志持久化待办事项列表](JournaledTodoList) | 由 Aspire 托管的 Web 应用，演示基于日志的持久化 Grain 状态。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [在线状态服务](Presence) | 利用观察者和多个协作 Grain 实现的游戏在线状态服务。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [购物车](ShoppingCart) | 使用 Orleans 持久化及 Azure Storage 的 Blazor 购物车。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [Azure App Service 部署](Deployment/AzureAppService) | 适用于 Windows 和 Linux 的 Azure App Service 部署，包含托管标识、Silo 专用网络、健康检查、Easy Auth、Bicep 和 OIDC。 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [Azure Container Apps 部署](Deployment/AzureContainerApps) | 适用于 Azure Container Apps 的托管标识 Orleans 集群，包含客户端、Dashboard、缩放器及 Bicep 部署。 | [Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps) |

<a id="all-samples"></a>

## 全部示例

| 示例 | 说明 | 语言 | 标签 | 原始来源 |
| --- | --- | --- | --- | --- |
| [文字冒险游戏](Adventure) | 演示 Grain、外部客户端和应用程序建模的文字冒险游戏。 | C# | games, clients, grains | [dotnet/samples](https://github.com/dotnet/samples) |
| [AWS Kinesis 与 DynamoDB](AWS/KinesisDynamoDB) | 托管于 AWS 的 Orleans 应用，使用 DynamoDB 实现集群成员资格管理、持久化、提醒器及 Kinesis 检查点存储。 | C# | aws, kinesis, dynamodb, streaming | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [银行账户](BankAccount) | 模拟银行转账，演示跨有状态 Grain 的 ACID 事务。 | C# | transactions, persistence | [dotnet/samples](https://github.com/dotnet/samples) |
| [基础集群](BasicClustering) | 由 Aspire 托管的最小 Orleans 集群，具有两个 Silo 副本，使用 Redis 管理成员资格。 | C# | clustering, aspire, redis, getting-started | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [Blazor Server](Blazor/BlazorServer) | 由 Orleans Grain 提供后端支持的交互式 Blazor Server 应用。 | C#, Razor | blazor, aspnet-core, web | [dotnet/samples](https://github.com/dotnet/samples) |
| [Blazor WebAssembly](Blazor/BlazorWasm) | 托管式 Blazor WebAssembly 应用，服务器以 Orleans 为后端。 | C#, Razor | blazor, webassembly, web | [dotnet/samples](https://github.com/dotnet/samples) |
| [聊天室](ChatRoom) | 演示 Orleans Streams 的终端聊天室应用。 | C# | streaming, client, terminal | [dotnet/samples](https://github.com/dotnet/samples) |
| [Chirper 社交网络](Chirper) | 使用持久化、观察者和可重入 Grain 的社交网络示例。 | C# | observers, persistence, reentrancy | [dotnet/samples](https://github.com/dotnet/samples) |
| [自定义 Grain 调用返回类型](CustomGrainCallReturnType) | 使用可等待的 GrainCall<T> 扩展，演示生成的请求基类、代理适配及故障传播。 | C# | serialization, code-generation, grains | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [持久化工作流](DurableWorkflows) | 由 Aspire 托管的应用，演示持久化任务工作流：扇出、带截止时间的信号、协作式取消、Saga 和副本恢复。 | C# | durable-tasks, workflows, aspire, redis, azure-storage | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [F# Hello World](FSharpHelloWorld) | 使用 F# 实现 Grain 的最小 Orleans 应用。 | F#, C# | getting-started, fsharp | [dotnet/samples](https://github.com/dotnet/samples) |
| [GPS 跟踪器](GPSTracker) | 集成 Orleans 与 ASP.NET Core SignalR 的物联网位置跟踪器。 | C#, JavaScript | iot, signalr, web | [dotnet/samples](https://github.com/dotnet/samples) |
| [Google Cloud Firestore](GoogleFirestore) | 使用 Firestore 实现集群成员资格管理、Grain 目录、持久化和提醒器的 Orleans 应用。 | C# | google-cloud, firestore, clustering, persistence, reminders | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [Hello World](HelloWorld) | 用于定义和调用 Grain 的最小完整 Orleans 应用。 | C# | getting-started, grains | [dotnet/samples](https://github.com/dotnet/samples) |
| [日志持久化待办事项列表](JournaledTodoList) | 由 Aspire 托管的 Web 应用，演示基于日志的持久化 Grain 状态。 | C#, Razor | journaling, aspire, web | [dotnet/samples](https://github.com/dotnet/samples) |
| [持久化作业提供程序迁移](DurableJobsMigration) | 在 Azure Blob 提供程序 B 接收新作业的同时，排空提供程序 A 中的现有作业，并执行进程重启及全量清单检查。 | C# | durable-jobs, journaling, azure-storage, aspire | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [消息传递：库存预留](Messaging) | 独立的订单与库存工作流，演示类型化主题、稳定的命令 ID、收件箱去重以及经日志确认的回复。 | C# | messaging, journaling, grains | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [使用 Azure Blob JSON 的日志持久化](JournalingAzureBlobJson) | Aspire 示例，使用 Orleans 日志持久化，将 JSON 事件存储到 Azure Blob Storage。 | C# | journaling, azure-storage, aspire | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [在线状态服务](Presence) | 利用观察者和多个协作 Grain 实现的游戏在线状态服务。 | C# | gaming, observers, services | [dotnet/samples](https://github.com/dotnet/samples) |
| [购物车](ShoppingCart) | 使用 Orleans 持久化及 Azure Storage 的 Blazor 购物车。 | C#, Razor | blazor, persistence, azure-storage | [dotnet/samples](https://github.com/dotnet/samples) |
| [股票行情](Stocks) | 使用 Grain 定时器、HTTP 调用和临时缓存的股票价格应用。 | C#, Razor | timers, http, caching | [dotnet/samples](https://github.com/dotnet/samples) |
| [流式处理自定义数据适配器](Streaming/CustomDataAdapter) | Event Hubs 流式处理示例，从非 Orleans 发布者消费数据。 | C# | streaming, event-hubs, azure | [dotnet/samples](https://github.com/dotnet/samples) |
| [简单流式处理](Streaming/Simple) | 使用 Orleans Streams 的精简生产者与消费者示例。 | C# | streaming, getting-started | [dotnet/samples](https://github.com/dotnet/samples) |
| [井字棋](TicTacToe) | 基于 Web 的游戏，演示游戏大厅及 ASP.NET Core 集成。 | C#, JavaScript | games, aspnet-core, web | [dotnet/samples](https://github.com/dotnet/samples) |
| [传输层安全性](TransportLayerSecurity) | 为所有网络通信配置双向 TLS 的 Hello World 集群。 | C# | security, tls, networking | [dotnet/samples](https://github.com/dotnet/samples) |
| [Visual Basic Hello World](VBHelloWorld) | 使用 Visual Basic 编写 Grain 契约和实现的最小 Orleans 应用。 | Visual Basic, C# | getting-started, visual-basic | [dotnet/samples](https://github.com/dotnet/samples) |
| [投票](Voting) | 面向 Kubernetes 的投票 Web 应用，包含 Orleans Dashboard。 | C#, Razor | kubernetes, dashboard, web | [dotnet/samples](https://github.com/dotnet/samples) |
| [Azure App Service 部署](Deployment/AzureAppService) | 适用于 Windows 和 Linux 的 Azure App Service 部署，包含托管标识、Silo 专用网络、健康检查、Easy Auth、Bicep 和 OIDC。 | C#, Razor, Bicep, YAML | deployment, azure, app-service, windows, linux | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [Azure Container Apps 部署](Deployment/AzureContainerApps) | 适用于 Azure Container Apps 的托管标识 Orleans 集群，包含客户端、Dashboard、缩放器及 Bicep 部署。 | C#, Bicep, YAML | deployment, azure, container-apps, managed-identity, networking | [Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps) |
