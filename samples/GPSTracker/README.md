---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans GPS 设备追踪示例"
urlFragment: "orleans-gps-device-tracker-sample"
description: "演示如何使用 GPS 设备追踪器的 Orleans 示例。"
---

<a id="orleans-gps-device-tracker-sample-app"></a>

# Orleans GPS 设备追踪示例应用

本示例演示在地图上追踪配备 GPS 的物联网设备的服务。设备位置使用 SignalR 近乎实时地更新，因此本示例也演示 Orleans 与 SignalR 集成的一种方式。设备更新来自*设备网关*，该网关由独立进程实现，连接主服务并模拟多个设备在旧金山的某个区域内以伪随机方式移动。

![GPS 追踪器的地图视图](screenshot.jpeg)

数据在系统内按以下方式流动：

* 设备网关定期向每个设备对应的 `DeviceGrain` 发送位置更新
* `DeviceGrain` 将其位置更新到单例 `PushNotifierGrain`
* `PushNotifierGrain` 将这些更新收集成批次，并推送至通过定期轮询单例 `HubListGrain` 发现的 `IRemoteLocationHub` 实例
* `HubListGrain` 维护一个列表，包含每个连接至 Orleans 集群的宿主的 `IRemoteLocationHub` 引用
* 每个宿主拥有一个 `HubListUpdater` 实例，它实现 [`BackgroundService`](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services#backgroundservice-base-class)，并定期将本地 `IRemoteLocationHub` 引用更新到 `HubListGrain`。
* 实现 `IRemoteLocationHub` 的 `RemoteLocationHub` 类通过构造函数注入 [`IHubContext<LocationHub>`](https://docs.microsoft.com/aspnet/core/signalr/hubcontext) 实例，使其能够向连接到 `LocationHub` 的 Web 客户端发送消息。

下图展示了上述描述：

![系统内的数据流向示意图](./dataflow.png)

以下示例展示多个宿主和许多浏览器时可能的情况。注意，`PushNotifierGrain` 和 `HubListGrain` 是单例，因此集群共享各自唯一的 Grain 实例。单例 Grain 是 Orleans 中的一种模式，即对给定类型只访问一个 Grain，例如始终调用键为 `0`、`Guid.Empty` 或其他固定值的实例，具体取决于 Grain 是 `IGrainWithIntegerKey` 还是 `IGrainWithGuidKey`。例如，每个 `DeviceGrain` 获取 ID 为 `0` 的 `IPushNotifierGrain` 实例：

```csharp
var notifier = GrainFactory.GetGrain<IPushNotifierGrain>(0);
```

![多个宿主及分布于其间的 Grain 示意图](./example.png)

<a id="sample-prerequisites"></a>

## 示例先决条件

本示例使用 C# 编写，以 .NET 10 为目标框架。需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

<a id="building-the-sample"></a>

## 构建示例

若要下载并运行示例，请执行以下步骤：

1. 下载并解压示例。
2. 在 Visual Studio（2022 或更高版本）中：
    1. 在菜单栏中选择**文件** > **打开** > **项目/解决方案**。
    2. 导航到解压后的示例代码所在文件夹，并打开 C# 项目（.csproj）文件。
    3. 按 <kbd>F5</kbd> 进行调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 无调试运行项目。
3. 通过命令行：
   1. 导航到解压后的示例代码所在文件夹。
   2. 在命令行中输入 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

打开三个终端窗口。在第一个终端窗口的命令提示符中运行以下命令：

```dotnetcli
dotnet run --project GPSTracker.Service
```

在第二个终端中启动另一个宿主实例，按如下方式传入 `InstanceId` 值，将其指定为第二个宿主：

```dotnetcli
dotnet run --project GPSTracker.Service -- --InstanceId 1
```

现在通过 Web 浏览器打开 `http://localhost:5001/index.html`。此时地图上还没有移动的点。

在第三个终端窗口的命令提示符中运行以下命令，开始模拟设备：

```dotnetcli
dotnet run --project GPSTracker.FakeDeviceGateway
```

Web 浏览器中的地图上会出现圆点，并开始在该区域内随机移动。

<a id="orleans-observability"></a>

## Orleans 可观测性

如果关注可观测性，本示例包含可选的日志记录、指标和分布式跟踪功能。

将 `grafana_dash.json` 导入 Grafana 以查看 Prometheus 指标。激活面板按每个发出指标的实例汇总 Grain 类型时间序列：当前存量使用仪表快照，生命周期吞吐量使用计数器速率或增量。Grains 面板按规范 `grain_type` 对当前架构的指标分组。它还展示旧版 Orleans 指标，使用其 CLR `type` 名称和 `legacy CLR` 图例，支持本示例已发布的包及混合版本升级。在将这两种指标架构合并为单个类型级总量之前，请将旧版 CLR 名称映射到规范的 Grain 类型注册。

```docker
docker run -p 9090:9090 --mount type=bind,source="${PWD}/prometheus.yml",target=/etc/prometheus/prometheus.yml prom/prometheus
```

```docker
docker run -d --name jaeger -e COLLECTOR_ZIPKIN_HOST_PORT=:9411 -p 5775:5775/udp -p 6831:6831/udp -p 6832:6832/udp -p 5778:5778 -p 16686:16686 -p 14268:14268 -p 14250:14250 -p 9411:9411 jaegertracing/all-in-one:1.22
```

或使用 Zipkin：

```docker
docker run -p 9411:9411 openzipkin/zipkin
```
