<a id="orleans-samples"></a>

# Orleans 样本

此目录是官方 Orleans 样本的金库。

<! -- 以更新方式从 gallery.json 生成,Readme.ps1. >

从其他 Microsoft 储存库进口的样品保留了原来源存放处在索引和 `gallery.json`中的原来源存放处,其来源许可证与进口内容一起保存。

<a id="build-and-validate"></a>

## 构建和验证

从仓库根根, 运行 :

```powershell
pwsh ./samples/Validate-Samples.ps1
```

命令检查画廊列表,并在 `Samples.slnx`中构建每个项目。只有在运行使用外部云的样本时,才需要外部云服务,而不是在编译时。

<a id="featured-samples"></a>

## 特精样品

| 抽样样本 | 说明 | 原始来源 |
| --- | --- | --- |
| [冒险](Adventure) | 文字冒险游戏, 演示 grains、 外部客户端和应用模型。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [螺旋](Chirper) | 使用持久性、观察力和重新加入 grains的社会网络抽样。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [持久流动的劳动力](DurableWorkflows) | 由预报托管的显示持久任务工作流程的应用软件:粉丝式、有最后期限的信号、合作取消、沙沙和复制恢复。 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [GPS 跟踪器](GPSTracker) | IoT 位置跟踪器将 Orleans 与 ASP.NET 核心 SignalR结合。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [你好,世界好](HelloWorld) | 最小的完整 Orleans 申请程序登记和拨用 grain. | [dotnet/samples](https://github.com/dotnet/samples) |
| [《日刊》待办事宜列表](JournaledTodoList) | 由一位精锐人士托管的网络应用程序,显示耐久性日记 grain 状态。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [驻外服务处](Presence) | 利用观察员和多方合作提供博彩服务 grains。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [购物车](ShoppingCart) | 使用 Orleans 持久性和 Azure 储存的布拉佐尔购物车。 | [dotnet/samples](https://github.com/dotnet/samples) |
| [Azure 应用程序处部署](Deployment/AzureAppService) | 视窗和Linux Azure 应用程序服务处部署,具有管理身份、私营 silo 网络、健康检查、易用自用、比塞普和OIDC。 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [Azure 集装箱应用程序部署](Deployment/AzureContainerApps) | 管理下身份 Orleans 群集,客户、仪表板、缩放器和Bicep为 Azure 集装箱应用程序部署。 | [Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps) |

<a id="all-samples"></a>

## 所有样本

| 抽样样本 | 说明 | 语文语言语言 | 标记标记 | 原始来源 |
| --- | --- | --- | --- | --- |
| [冒险](Adventure) | 文字冒险游戏, 演示 grains、 外部客户端和应用模型。 | C# | 游戏、客户、 grains | [dotnet/samples](https://github.com/dotnet/samples) |
| [AWS 基尼西斯和 DynamoDB](AWS/KinesisDynamoDB) | 由WS托管的 Orleans 应用程序,使用 DynamoDB 进行集群、持久性、提醒和Kinesis检查站。 | C# | 水、电离、气旋、流 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [银行账户](BankAccount) | 银行转账模拟模拟,表明ACID交易跨越了所有地点。 grains. | C# | 交易、持续交易、 | [dotnet/samples](https://github.com/dotnet/samples) |
| [基本集群](BasicClustering) | 由小数量 Orleans 组组成,有两家 silo 复制和 Redis 成员。 | C# | 组合、渴望、重新开发、启动 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [Blazor 服务器](Blazor/BlazorServer) | 一个互动的 Blazor 服务器应用程序, 后附 Orleans grains。 | C# #,剃刀 | 闪光、烟雾核心、网 | [dotnet/samples](https://github.com/dotnet/samples) |
| [布拉佐尔 WebAssembly](Blazor/BlazorWasm) | 主办布拉佐 WebAssembly 应用 Orleans- 后卫服务器 | C# #,剃刀 | 网状、网状、网 | [dotnet/samples](https://github.com/dotnet/samples) |
| [聊天室](ChatRoom) | 显示 Orleans 流的终端聊天应用程序。 | C# | 流、客户端、终端 | [dotnet/samples](https://github.com/dotnet/samples) |
| [螺旋](Chirper) | 使用持久性、观察力和重新加入 grains的社会网络抽样。 | C# | 观察员、持久性、再生性 | [dotnet/samples](https://github.com/dotnet/samples) |
| [自定义 grain- 调用返回类型](CustomGrainCallReturnType) | 可等待的 GrainCall<T> 延期,证明请求依据、代理适应和故障传播。 | C# | 序列、代码生成、 grains | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [持久流动的劳动力](DurableWorkflows) | 由预报托管的显示持久任务工作流程的应用软件:粉丝式、有最后期限的信号、合作取消、沙沙和复制恢复。 | C# | 持久任务、工作流程、渴望、再满足、再满足、静流存储 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [# 你好世界](FSharpHelloWorld) | F# 中执行最小 Orleans 应用程序,其中 grains 在F# 中实施。 | F#, C# | 启动, 快速 | [dotnet/samples](https://github.com/dotnet/samples) |
| [GPS 跟踪器](GPSTracker) | IoT 位置跟踪器将 Orleans 与 ASP.NET 核心 SignalR结合。 | C#, JavaScript | iot、信号器、网络 | [dotnet/samples](https://github.com/dotnet/samples) |
| [Google 云烟火炉](GoogleFirestore) | A 音 A Orleans 应用消防商店进行集束, grain 目录、持久性和提醒。 | C# | 凝胶- 球状、 火炉、 集束、 持久性、 提醒 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [你好,世界好](HelloWorld) | 最小的完整 Orleans 申请程序登记和拨用 grain. | C# | 正在启动 grains | [dotnet/samples](https://github.com/dotnet/samples) |
| [《日刊》待办事宜列表](JournaledTodoList) | 由一位精锐人士托管的网络应用程序,显示耐久性日记 grain 状态。 | C# #,剃刀 | 日记、渴望、网络 | [dotnet/samples](https://github.com/dotnet/samples) |
| [提供持久就业的劳动力移徙](DurableJobsMigration) | 现有工作从 Azure Blob提供商A接受新的工作,程序重新启动和全面库存检查。 | C# | 持久工作、日记、收成、储存、追求 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [预定通信物资库存](Messaging) | 独立订单和库存工作流程,展示打字对象、稳定的指令标识、信箱重复以及日记确认的答复。 | C# | 发短信、记号、 grains | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [与 Azure 布线 JSON](JournalingAzureBlobJson) | 使用 Orleans 与 JSON 存储在 Azure 黑洞储藏室 | C# | 日记、宝藏、珍藏、渴望 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [驻外服务处](Presence) | 利用观察员和多方合作提供博彩服务 grains。 | C# | 游戏、观察员、服务 | [dotnet/samples](https://github.com/dotnet/samples) |
| [购物车](ShoppingCart) | 使用 Orleans 持久性和 Azure 储存的布拉佐尔购物车。 | C# #,剃刀 | 火力、持久性、冷冻储存 | [dotnet/samples](https://github.com/dotnet/samples) |
| [储存库存](Stocks) | 使用 grain 计时器、HTTP电话和临时缓存的股票价格应用软件。 | C# #,剃刀 | 时间计, http, 缓存 | [dotnet/samples](https://github.com/dotnet/samples) |
| [流动自定义数据适应器](Streaming/CustomDataAdapter) | 活动枢纽流动样本,从非Orleans 出版商处获取数据。 | C# | 水流、事件枢纽、紫 | [dotnet/samples](https://github.com/dotnet/samples) |
| [简单流流](Streaming/Simple) | 使用 Orleans 流的紧凑生产者和消费者例子。 | C# | 流, 启动 | [dotnet/samples](https://github.com/dotnet/samples) |
| [铁脚](TicTacToe) | 网上游戏,展示游说和 ASP.NET 核心整合。 | C#, JavaScript | 游戏、 亚麻核心、 网络 | [dotnet/samples](https://github.com/dotnet/samples) |
| [运输层安全](TransportLayerSecurity) | 由所有网络通讯的TLS组合成 | C# | 安全、 Tls、 网络 | [dotnet/samples](https://github.com/dotnet/samples) |
| [世界喜好](VBHelloWorld) | 最小 Orleans 应用 grain 《视觉基础》中合同和执行。 | 视觉基础,C## | 开始,视觉基础 | [dotnet/samples](https://github.com/dotnet/samples) |
| [表决表决表决 表决表决 表决表决表决 表决表决表决投票 表决表决表决](Voting) | A Kubernetes- 与 Orleans 弹板 | C# #,剃刀 | 宽网、仪表板、网络 | [dotnet/samples](https://github.com/dotnet/samples) |
| [Azure 应用程序处部署](Deployment/AzureAppService) | 视窗和Linux Azure 应用程序服务处部署,具有管理身份、私营 silo 网络、健康检查、易用自用、比塞普和OIDC。 | C#, Razor, Bicep, YAML | 部署、冷冻、电源服务、窗户、玻璃 | [dotnet/orleans](https://github.com/dotnet/orleans) |
| [Azure 集装箱应用程序部署](Deployment/AzureContainerApps) | 管理下身份 Orleans 群集,客户、仪表板、缩放器和Bicep为 Azure 集装箱应用程序部署。 | C#,比塞普,亚美 | 部署、冷冻、集装箱申请、管理身份、联网 | [Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-Container-Apps) |
