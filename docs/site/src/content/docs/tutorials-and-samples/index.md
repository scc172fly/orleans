---
title: "Orleans 辅导和样品"
description: "通过教程、解释和储存库支持的样本学习 Orleans 。"
ms.date: 08/02/2026
ms.topic: sample
---

<a id="orleans-tutorials-and-samples"></a>

# Orleans 辅导和样品

使用此页面选择正确的学习材料类型 :

- **快速启动** 带领您完成一项重点突出的任务。
- **教程** 逐步教授能力。
- **解释** 说明如何模拟完整应用。
- **样本** 是可建构的应用,您可以检查和运行。

<a id="start-here"></a>

## 从这里开始

对于最起码的应用程序,从 [Orleans 你好世界](hello-world.md)开始。它会主机一个 silo ,并从一个进程调用一个 grain 。

对于较为典型的项目结构, [构建您的第一个 Orleans 应用程序](../quickstarts/build-your-first-orleans-app.md) 使用单独的 grain 合同、执行、 silo和外部客户项目。

<a id="tutorials"></a>

## 教学

| 教学 | 教它教什么 |
| --- | --- |
| [将 Orleans 应用程序安装到 Azure 容器应用软件](production-application.md) | 运行、部署、观察和核查多过程 Orleans 应用程序。 |
| [测试 Orleans 应用程序的结束](testing-walkthrough.md) | 从第一次集束试验到可再使用固定装置和地形变化的进展。 |
| [构建并回收流化应用程序](streaming-walkthrough.md) | 通过真正的供应商跟踪事件,核实从检查站恢复的情况。 |
| [自定义 grain 存储](custom-grain-storage.md) | 实施并登记 <xref:Orleans.Storage.IGrainStorage> 提供者。 |
| [Azure部署和规模](../quickstarts/deploy-scale-orleans-on-azure.md) | 部署 a Orleans 应用到 Azure 集装箱应用和配置共享供应商。 |

<a id="explanations"></a>

## 解释解释

| 第1条 | 为何解释 |
| --- | --- |
| [冒险游戏样板](adventure.md) | 房间、玩家和对象如何映射到 grains 和普通值 。 |
| [为什么 Orleans](../benefits.md) | 虚拟行为体模式的好处和取舍。 |
| [Orleans 建筑设计原则](../resources/orleans-architecture-principles-and-approach.md) | 设计目标,它决定了 Orleans APIs和运行时行为。 |

<a id="samples"></a>

## 样本样本

[`samples` 目录](https://github.com/dotnet/orleans/tree/main/samples) 包含官方 Orleans 样本,其 [样本目录](https://github.com/dotnet/orleans/blob/main/samples/README.md) 来自存储库清单,并与抽样项目一起得到验证。

<a id="fundamentals"></a>

### 基本原理

| 抽样样本 | 演示 |
| --- | --- |
| [你好,世界好](https://github.com/dotnet/orleans/tree/main/samples/HelloWorld) | 单一项目 silo、 grain 合同、执行、参考和呼叫。 |
| [冒险](https://github.com/dotnet/orleans/tree/main/samples/Adventure) | 与 grains 和独立客户进行域名建模。 |
| [螺旋](https://github.com/dotnet/orleans/tree/main/samples/Chirper) | 常住、观察和重新加入 grains。 |
| [简单流流](https://github.com/dotnet/orleans/tree/main/samples/Streaming/Simple) | 溪流生产者、消费者和订阅者。 |

<a id="web-and-application-integration"></a>

### 网络和应用整合

| 抽样样本 | 演示 |
| --- | --- |
| [购物车](https://github.com/dotnet/orleans/tree/main/samples/ShoppingCart) | 布拉佐尔,持久性和多项目应用。 |
| [Blazor 服务器](https://github.com/dotnet/orleans/tree/main/samples/Blazor/BlazorServer) | A Blazor 服务器应用程序, 后附 grains。 |
| [布拉佐尔 WebAssembly](https://github.com/dotnet/orleans/tree/main/samples/Blazor/BlazorWasm) | 主机 WebAssembly 客户端, 带有 Orleans- 支持服务器 。 |
| [GPS 跟踪器](https://github.com/dotnet/orleans/tree/main/samples/GPSTracker) | ASP.NET 核心核心核心核心核心 SignalR 和 IoT- 设备更新风格 |
| [驻外服务处](https://github.com/dotnet/orleans/tree/main/samples/Presence) | 观察员和合作 grains 以游戏的情境而行。 |

<a id="state-scheduling-and-messaging"></a>

### 国家、时间安排和发文

| 抽样样本 | 演示 |
| --- | --- |
| [银行账户](https://github.com/dotnet/orleans/tree/main/samples/BankAccount) | ACID在有声 grains之间进行交易。 |
| [《日刊》待办事宜列表](https://github.com/dotnet/orleans/tree/main/samples/JournaledTodoList) | 事件由 `JournaledGrain`、 日志一致性提供者和 高级主管 进行。 |
| [与 Azure 布线 JSON](https://github.com/dotnet/orleans/tree/main/samples/JournalingAzureBlobJson) | 与 Azure 布洛布存储器一起对API进行实验性日记。 |
| [通信通信](https://github.com/dotnet/orleans/tree/main/samples/Messaging) | 输入对象、稳定的指令身份、日记确认的答复和一份对重复提交文件的库存保留。 |
| [聊天室](https://github.com/dotnet/orleans/tree/main/samples/ChatRoom) | 使用 Orleans 流的终端聊天应用程序。 |
| [储存库存](https://github.com/dotnet/orleans/tree/main/samples/Stocks) | Grain 计时器、 HTTP 呼叫和临时缓存 |

下 Azure 布团 JSON 利用试验样本 `Microsoft.Orleans.Durable` 。《日刊待办事宜列表》使用支持的 [Orleans 事件抛星](../grains/event-sourcing/index.md) 模型。 [日刊样本指南](../grains/journaling/samples.md) 要运行 Azure 和 Redis 耐久状态样本。

下 [通讯食谱](../grains/messaging-recipes.md#run-the-stock-reservation-sample)
显示如何根据本地包装的 Orleans 包进行库存保留抽样。

<a id="deployment-and-operations"></a>

### 部署和行动

| 抽样样本 | 演示 |
| --- | --- |
| [Azure 集装箱套件](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps) | 一组、客户、仪表板、天平仪表和比塞普的部署。 |
| [Azure 服务处](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureAppService) | 申请服务处的多级 Orleans 应用程序。 |
| [运输层安全](https://github.com/dotnet/orleans/tree/main/samples/TransportLayerSecurity) | Orleans 网络通信的相互TLS。 |
| [表决表决表决 表决表决 表决表决表决 表决表决表决投票 表决表决表决](https://github.com/dotnet/orleans/tree/main/samples/Voting) | Kubernetes- 面向方向的部署和 Orleans 机板。 |

存储目录还包括F#、视觉基础、游戏、自定义流适配器和其他重点实例。

<a id="validate-samples-locally"></a>

## 在当地验证样本

在克隆 Orleans 仓库后,从储存库根运行样本验证脚本:

```powershell
pwsh ./samples/Validate-Samples.ps1
```

剧本验证了画廊清单,并在 `samples/Samples.slnx`中构建了每个项目。只需要云证即可运行连接到云服务的样本,而不是编译这些样本。
