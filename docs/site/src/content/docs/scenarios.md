---
title: "Orleans 设想情景和使用情况"
description: "决定 Orleans 虚拟行为体模式是否符合申请的工作量。"
ms.date: 08/17/2026
ms.topic: conceptual
---

<a id="orleans-scenarios-and-use-cases"></a>

# Orleans 设想情景和使用情况

Orleans 支持许多独立可处理实体的应用程序,这些实体的状态和工作分布因身份而异。 A grain 拥有一个实体的行为和国家,运行时间管理其启动、安置、路由和回合执行。

根据身份、国家所有制、货币和调用模式选择 grains 。将其与数据库、队列、流处理器合并,并根据每个工作量计算服务。

<a id="signals-that-orleans-is-a-strong-fit"></a>

## 信号 Orleans 功能强

当其中若干陈述描述申请时,考虑 Orleans :

- 域实体具有稳定的身份,例如玩家、装置、账户、订单、房间、租户或会话。
- 每个实体拥有州或协调工作。
- 要求一个实体执行按顺序排列的转手执行,使该实体受益。
- 工作量包含许多实体,可以将交通量分布在关键部门。
- 各实体需要时间、提醒、流、持久性或向其他实体发出呼吁。
- 调用者使用稳定的逻辑身份,而运行时间则解决实体目前的主机。
- 应用程序已经是一个分布式 .NET 服务,或预计将发展成一个服务。

缩放性来自 grain 边界、钥匙分布、呼叫模式、储存和外部依赖。

<a id="common-scenarios"></a>

## 共同设想情景

<a id="ai-agents-and-conversational-sessions"></a>

### AI 代理和谈话会

AI代理会话是一种自然的 grain 特性。一个会话 grain 可以拥有对话状态、模式和工具配置、待办工作以及与其他代理或服务的协调。 Orleans 可按需启动会话,取消闲置激活,将每项请求引向当前激活,并默认地一次翻转一个进程。这提供生命周期管理,并同时进行会话更新。

模型推理和工具电话通常是非同步的外部操作。 grain 可以高效地等待,在一个现场使用时使用 [响应流流](grains/response-streaming.md) 将产生的标牌或进步还原,并使用计时器、提醒或耐久工作来安排日后的工作。 具有保留和重播能力的流源提供商或者有耐久存储功能的流提供者可以保留输出以进行重新连接和以后的消耗。

在 silo 失败后,成员资格会合之后,后来的请求可以恢复会议,恢复健康 silo,并且 [grain 持久性](grains/grain-persistence/index.md) 将国家恢复到持久储存。 明确的持久性点、无能工具呼叫、约束式重整以及和解保持了所有失败的应用水平结果。

<a id="fraud-protection-risk-and-low-latency-decisioning"></a>

### 欺诈保护、风险和低延迟决策

欺诈保护、金融服务、精算系统和受监管的在线赌博经常从迅速变化的国家作出决定。 grain 能够保持低长度 *软状态*:从耐久记录、事件流、市场反馈或模型产出中得出的可重建的模拟工作数据。

账户、支付工具、商家、政策、投资组合、市场和赌博活动 grains 能够跟踪近期的活动、风险信号、限制、暴露和当前情况。 Grain 呼吁协调相关实体之间的决策,同时流分配不断变化的投入和产出。 耐用商店和活动记录保留了重建工作状态和支持审计的权威性历史。

同样的模式支持实时定价、拍卖、库存分配、建议和其他决定,将实体当前背景与即将发生的事件结合起来。 平行计算和数据平台可以产生模型或大型分析结果,在网上决策中应用 grains 。

[抽样库存](https://github.com/dotnet/orleans/tree/main/samples/Stocks) 显示每个符号临时缓存和定期更新。 [银行账户交易抽样](https://github.com/dotnet/orleans/tree/main/samples/BankAccount) 显示各账户协调的状态变化。

<a id="internet-of-things-and-digital-twins"></a>

### 物与数字双胞人互联网

因特网(互联网)IoT装置、车辆、传感器、工业资产、机器人、建筑物和边缘网关具有稳定的身份,并处于不断变化的状态。 grain 每个资产可以保持已知的最后状态,按顺序应用命令,管理配置,评价当地规则,协调定期或排定的工作。 Grain 身份让打电话者直接处理每个资产, Orleans 解决了目前的宿主,而坚持在恢复活动后恢复明确书面状态。

Grains 可以消耗选定的遥测事件,并拥有每个资产的真实行为。 经纪人、时间序列仓库和流处理系统为周围遥测管道提供大量摄取、长期留存、全机队范围的分析和模型培训。

[GPS跟踪器样本](https://github.com/dotnet/orleans/tree/main/samples/GPSTracker) 模型 IoT 设备为 grains ,并将 Orleans 与 ASP.NET 核心 SignalR合并。

<a id="monitoring-resource-governance-and-job-orchestration"></a>

### 监测、资源治理和工作协调

服务器、服务、集装箱、集群、租户、配额、职位和硬件资源图自然地向 grains。资源 grain 可以将心跳、健康信号、理想配置、能力、保留和补救状态结合起来。 计时器检测到陈旧的卫生报告,提醒催复程序,以及一次性的持久工作时间表后续行动。

工作 grains 可以拥有提交状态、依赖性、回溯、进度和完成。资源 grains 可以拥有硬件可用性和分配。分隔调度器 grains 将职位与符合资格的资源匹配,外部工人通过 grain 电话或流执行指定的工作和报告状况。Stail grain 身份让操作者和自动化者在整个生命周期处理相同的工作或资源。

这一模式支持车队健康监测、自动化补救、配额执行、工作量安排、批发和建设农场以及跨专业硬件的管弦。 [异异 silos](host/heterogeneous-silos.md) 和 [职位布置过滤](grains/grain-placement-filtering.md) 还将 Orleans grain 的工作量直接分配给具有特定能力的东道国。

<a id="multiplayer-and-online-games"></a>

### 多人游戏和网上游戏

玩家、游戏会议、会议室、政党、比赛、锦标赛和领头板具有自然特性,通常拥有可变状态。 Grains 可以将每个实体的业务序列化,通过 grain 电话协调互动,并通过观察员或流向相关客户通报。

使用 Orleans 来进行权威游戏和社会状态、配对协调、存在、进步和会话周期。 实时模拟、制导和其他CPU密集型循环可以在专门计算服务中运行,而 grains 则可以协调耐久和互动状态。

见 [冒险游戏](tutorials-and-samples/adventure.md) 解释和 [存在服务抽样](https://github.com/dotnet/orleans/tree/main/samples/Presence)。

<a id="commerce-reservations-and-customer-services"></a>

### 商业、预订和客户服务

购物车、订单、账户、预订、订阅和客户简介往往都有明确的所有权界限和规则,必须持有一个钥匙。 一个 grain 可以保持这些规则,只要它们保护国家,故意改变,并使用提醒或持久工作来做未来的工作。

用户 grains 可以保持偏好、届会背景、应享权利、通知状态、配额以及最近个人反应性互动。 建议和搜索服务可以提供排名结果,而 grain 与当前用户和商业状况相结合。

Orleans 支持分散的ACID交易,用于跨越多个实体之间支助的交易状态的业务。

见 [购物墨盒抽样](https://github.com/dotnet/orleans/tree/main/samples/ShoppingCart) 和 [Orleans 交易](grains/transactions.md)。

<a id="collaboration-social-applications-and-live-sessions"></a>

### 协作、社会应用和直播会议

会议室、频道、文件、用户、社会概况和会议可以独立处理,并可在客户要求之间保留行为。 Grain 观察员适合向相关客户进行临时回调,而 Orleans 流则适合多种播报活动流量和订阅,其担保取决于选定的提供者。

中间商和存储系统提供耐久的信息保留、竞争的消费者、大型广播断层以及整个事件历史的分析。 Grains 对选定的活动适用每个实体的状况和行为,并协调现场互动。

[聊天室样本](https://github.com/dotnet/orleans/tree/main/samples/ChatRoom) 将每个频道的 grain 与 Orleans 流合并。 [螺旋取样](https://github.com/dotnet/orleans/tree/main/samples/Chirper) 模式是一个社交网络,用户 grains、持久性和观察者都有。

<a id="per-entity-orchestration"></a>

### 单位- 单位间管弦

诸如机器人对话、用户工作流程、订阅或预定运动等长期过程,在每个情况都有其身份和独立进展时可以映射到 grains 。 Grain 州记录进展情况, grain 呼吁明确协调,以及计时器、提醒或持久工作触发以后的工作。

Orleans 表达 grain 代码中的方案、每个实体的管弦。工作流程引擎提供视觉过程作者、人类审批队列和可查询的审计历史,并且可以与 grains 整合,后者拥有域状态。

<a id="choose-the-architecture-by-workload"></a>

## 按工作量选择架构

将主要工作量与为执行这一任务而设计的架构和州模式相匹配:

- **无国籍者请求处理或直接的CRUD。** A 音 A ASP.NET 由数据库支持的核心服务提供请求处理、协调和持久性。
- **几个大型CPU约束或数据平行的工作。** 平行计算、批量或工作处理工具分布大量计算。 Grain 转动仍然短小且不同步。
- **散装分析、ETL或宣示性流处理。** 数据库和数据流引擎的设计用于扫描、连接、窗口以及大型数据集的共享转换。
- **一个全球协调的资源或永久热键。** 域分割分配工作,而专门用于单一资源协调的系统管理集中存取模式。
- **共享模拟或硬实时处理。** 本地同时运行的时间提供共享模拟访问,硬实时平台提供确定性时间安排和约束性延缓时间。
- **与竞争消费者一起工作。** 竞争式的消费者队列将每个项目指派给一名工人。 Orleans 流将每个项目发送到逻辑流上的每一个订阅对象。

Orleans 通常拥有与HTTP API、数据库、藏匿处、经纪人、计算工人和分析系统一样的国有实体子系统。

<a id="evaluate-the-model-against-the-workload"></a>

## 对照工作量评价模式

在承诺设计之前,确定候选人 grain 键并测试最繁忙的道路:

1. 确定哪个实体拥有每个变量和运行。
1. 估计活动键的数量和它们之间的交通分布。
1. 寻找热键、全球协调员、聊天呼叫链和大信息。
1. 决定哪个州必须生存下去 什么时候写必须完成
1. 定义每个外部操作的重试、 剩余能力、超时和回收行为 。
1. 测试具有代表性的钥匙分布和生产提供者类型。

方案拟定模式的利弊权衡,见 [为什么 Orleans](benefits.md)。设计和操作指导见 [Orleans 最佳做法](resources/best-practices.md) 和 [生产准备状态清单](deployment/production-readiness.md)。
