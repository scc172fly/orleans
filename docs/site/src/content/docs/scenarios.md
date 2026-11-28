---
title: Orleans 应用场景与用例
description: 判断 Orleans 虚拟 Actor 模型是否适合应用的工作负载。
ms.date: 08/17/2026
ms.topic: conceptual
---

<a id="orleans-scenarios-and-use-cases"></a>

# Orleans 应用场景与用例

Orleans 支持拥有大量可独立寻址实体的应用，其状态与工作按标识划分。一个 Grain 拥有一个实体的行为和状态，运行时负责管理其激活、放置、路由及基于执行轮次的执行。

根据标识、状态所有权、并发及调用模式选择 Grain。根据各工作负载的需求，将 Grain 与数据库、队列、流处理器和计算服务结合使用。

<a id="signals-that-orleans-is-a-strong-fit"></a>

## Orleans 非常适用的特征

当应用符合以下多项描述时，可考虑使用 Orleans：

- 领域实体具有稳定标识，例如玩家、设备、账户、订单、房间、租户或会话。
- 每个实体拥有状态，或持续协调工作。
- 同一实体的请求适合串行、基于执行轮次的执行方式。
- 工作负载包含大量实体，且能够将流量分布到不同的键。
- 实体需要定时器、提醒器、流、持久化或对其他实体的调用。
- 调用方使用稳定的逻辑标识，而运行时解析实体当前所在的宿主。
- 应用已经是分布式 .NET 服务，或预计将发展为此类服务。

可伸缩性来自 Grain 边界、键分布、调用模式、存储和外部依赖项。将热点实体划分到多个键，以分散工作负载。

<a id="common-scenarios"></a>

## 常见场景

<a id="ai-agents-and-conversational-sessions"></a>

### AI Agent 与对话会话

AI Agent 会话可以自然地作为 Grain 标识。会话 Grain 可以拥有对话状态、模型与工具配置、待处理工作，以及与其他 Agent 或服务的协调逻辑。Orleans 按需激活会话，移除空闲激活实例，将每个请求路由到当前激活实例，并默认逐个处理执行轮次，从而提供生命周期管理并使并发会话更新串行化。

模型推理和工具调用通常是异步外部操作。Grain 可以高效地等待这些操作，使用[响应流式传输](grains/response-streaming.md)在一次仍在进行的调用中返回生成的 Token 或进度，并使用定时器、提醒器或持久化作业安排后续工作。支持保留与重放的流提供程序或持久化存储可以保存输出，以供重新连接及后续消费。

Silo 故障后的成员资格收敛完成后，后续请求可以在健康的 Silo 上重新激活会话，[Grain 持久化](grains/grain-persistence/index.md)则恢复已写入持久化存储的状态。显式持久化边界、幂等工具调用、有界重试和对账机制有助于在故障发生时维持应用层结果。

<a id="fraud-protection-risk-and-low-latency-decisioning"></a>

### 欺诈防护、风险管理及低延迟决策

欺诈防护、金融服务、精算系统及受监管的在线游戏通常需要根据快速变化的状态作出决策。Grain 可以维护低延迟的*软状态*，即从持久化记录、事件流、市场数据源或模型输出派生、可重新构建的内存工作数据。基于执行轮次的执行方式使每个实体的更新与决策保持有序。

账户、支付工具、商户、保单、投资组合、市场以及游戏事件 Grain 可以跟踪近期活动、风险信号、限额、风险敞口及当前赔率。Grain 调用协调相关实体的决策，而流分发变化的输入和输出。持久化存储和事件日志保留用于重建工作状态及支持审计的权威历史记录。

同一模式还支持实时定价、竞价、库存分配、推荐及其他将实体当前上下文与传入事件结合的决策场景。并行计算和数据平台可生成模型或大型分析结果，由 Grain 在在线决策时应用。

[Stocks 示例](https://github.com/dotnet/orleans/tree/main/samples/Stocks)演示了按股票代码进行临时缓存及定期更新。[Bank Account 事务示例](https://github.com/dotnet/orleans/tree/main/samples/BankAccount)演示了跨账户协调状态变更。

<a id="internet-of-things-and-digital-twins"></a>

### 物联网与数字孪生

物联网（IoT）设备、车辆、传感器、工业资产、机器人、建筑物和边缘网关均具有稳定标识及不断变化的状态。每个资产对应一个 Grain，可以维护最近已知状态、按顺序应用命令、管理配置、评估本地规则以及协调定期或计划工作。Grain 标识允许调用方直接寻址每个资产，而 Orleans 负责解析其当前宿主；持久化则在重新激活后恢复已显式写入的状态。

Grain 可以消费选定的遥测事件，并拥有各资产的有状态行为。消息代理、时序存储及流处理系统为周边遥测管道提供大规模数据摄取、长期保留、全设备群分析和模型训练能力。

[GPS Tracker 示例](https://github.com/dotnet/orleans/tree/main/samples/GPSTracker)将 IoT 设备建模为 Grain，并将 Orleans 与 ASP.NET Core SignalR 集成。

<a id="monitoring-resource-governance-and-job-orchestration"></a>

### 监控、资源治理和作业编排

服务器、服务、容器、集群、租户、配额、作业和硬件资源都可以自然地映射为 Grain。资源 Grain 可以结合心跳、健康信号、期望配置、容量、预留和修复状态。定时器检测过期的健康报告，提醒器触发周期性检查，而持久化作业安排一次性后续操作。

作业 Grain 可以拥有提交状态、依赖项、重试、进度及完成状态。资源 Grain 可以拥有硬件可用性和分配状态。分区调度器 Grain 将作业与符合条件的资源匹配，外部工作者执行分配的工作并通过 Grain 调用或流报告状态。稳定的 Grain 标识使运维人员和自动化系统能够在整个生命周期中寻址同一作业或资源。

此模型支持设备群健康监控、自动修复、配额强制执行、工作负载放置、批处理与构建集群，以及跨专用硬件的编排。[异构 Silo](host/heterogeneous-silos.md)和[放置筛选](grains/grain-placement-filtering.md)还可将 Orleans Grain 工作负载导向具备特定能力的宿主。

<a id="multiplayer-and-online-games"></a>

### 多人及在线游戏

玩家、游戏会话、房间、队伍、对局、锦标赛和排行榜具有自然标识，通常拥有可变状态。Grain 可以串行执行各实体的操作，通过 Grain 调用协调交互，并通过观察者或流通知已连接的客户端。

使用 Orleans 管理权威游戏和社交状态、匹配协调、在线状态、成长进度及会话生命周期。实时模拟、渲染和其他 CPU 密集型循环可以在专用计算服务中运行，而 Grain 负责协调持久化状态和交互状态。

请参阅 [Adventure 游戏](tutorials-and-samples/adventure.md)说明及 [Presence Service 示例](https://github.com/dotnet/orleans/tree/main/samples/Presence)。

<a id="commerce-reservations-and-customer-services"></a>

### 商业交易、预订及客户服务

购物车、订单、账户、预订、订阅和客户资料通常具有清晰的所有权边界，以及必须对单个键成立的规则。Grain 可以将这些规则与其保护的状态放在一起，明确持久化变更，并使用提醒器或持久化作业执行后续工作。

按用户划分的 Grain 可以维护偏好、会话上下文、权益、通知状态、配额及近期交互，以快速提供个性化体验。推荐和搜索服务可以提供排序结果，由 Grain 将这些结果与当前用户及业务状态相结合。

Orleans 支持跨多个实体的受支持事务状态执行分布式 ACID 事务。对于一致性模型允许独立状态转换的工作流，可通过幂等命令、显式协调及对账机制提供支持。

请参阅 [Shopping Cart 示例](https://github.com/dotnet/orleans/tree/main/samples/ShoppingCart)及 [Orleans 事务](grains/transactions.md)。

<a id="collaboration-social-applications-and-live-sessions"></a>

### 协作、社交应用及实时会话

房间、频道、文档、用户、社交资料及会话可以独立寻址，并在客户端请求之间保留行为。Grain 观察者适合对已连接客户端进行临时回调，而 Orleans 流适合多播事件流及订阅，其保证取决于所选提供程序。

消息代理和存储系统提供持久消息保留、竞争消费者、大规模广播扇出及针对完整事件历史的分析。Grain 将每个实体的状态和行为应用于选定事件，并协调实时交互。

[Chat Room 示例](https://github.com/dotnet/orleans/tree/main/samples/ChatRoom)将每频道一个 Grain 与 Orleans 流结合起来。[Chirper 示例](https://github.com/dotnet/orleans/tree/main/samples/Chirper)使用用户 Grain、持久化及观察者对社交网络建模。

<a id="per-entity-orchestration"></a>

### 按实体进行编排

Bot 对话、用户工作流、订阅或计划活动等长期运行过程，如果每个实例均具有标识并独立推进，就可以映射为 Grain。Grain 状态记录进度，Grain 调用表达协调关系，定时器、提醒器或持久化作业触发后续工作。

Orleans 在 Grain 代码中以编程方式表达按实体的编排。工作流引擎提供可视化流程编写、人工审批队列及可查询的审计历史，并可与拥有领域状态的 Grain 集成。

<a id="choose-the-architecture-by-workload"></a>

## 根据工作负载选择架构

将主要工作负载与针对其执行模型和状态模型设计的架构相匹配：

- **无状态请求处理或简单 CRUD。** 由数据库支持的 ASP.NET Core 服务提供请求处理、协调及持久性。
- **少数大型 CPU 密集型或数据并行作业。** 并行计算、批处理或作业处理工具分发大量计算。Grain 执行轮次应保持简短且异步。
- **批量分析、ETL 或声明式流处理。** 数据库及数据流引擎专为大型数据集上的扫描、连接、窗口和共享转换而设计。
- **一个需要全局协调的资源或长期热点键。** 领域分区分散工作，而专门用于单资源协调的系统管理集中式访问模式。
- **共享内存或硬实时处理。** 本地并发运行时提供共享内存访问，硬实时平台提供确定性调度及有界延迟。
- **具有竞争消费者的工作队列。** 竞争消费者队列将每个条目分配给一个工作者。Orleans 流则将每个条目投递给逻辑流上的每个订阅。

Orleans 通常负责有状态实体子系统，并与 HTTP API、数据库、缓存、消息代理、计算工作者及分析系统共同运行。

<a id="evaluate-the-model-against-the-workload"></a>

## 根据工作负载评估模型

确定设计前，先识别候选 Grain 键并测试最繁忙的路径：

1. 定义哪个实体拥有各项不变量和操作。
1. 估算活跃键的数量及键之间的流量分布。
1. 查找热点键、全局协调器、频繁往返的调用链及大消息。
1. 决定哪些状态必须在故障后保留，以及何时必须完成写入。
1. 为各项外部操作定义重试、幂等性、超时和恢复行为。
1. 对具有代表性的键分布以及生产环境使用的提供程序类型进行负载测试。

有关此编程模型的优势与权衡，请参阅[为何选择 Orleans](benefits.md)。有关设计及运维指南，请参阅 [Orleans 最佳实践](resources/best-practices.md)及[生产就绪检查清单](deployment/production-readiness.md)。
