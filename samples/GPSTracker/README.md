---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans GPS device tracker sample"
urlFragment: "orleans-gps-device-tracker-sample"
description: "Orleans 样本表明如何使用全球定位系统设备跟踪器。"
---

<a id="orleans-gps-device-tracker-sample-app"></a>

# Orleans GPS 设备追踪器样本应用

该样本显示在地图上跟踪GPS装备的 IoT 装置的服务。设备位置使用 SignalR 近实时更新,因此该样本显示了将 Orleans 与 SignalR合并的一种方法。设备更新来自一个 *装置网关*,实施时采用了一个单独的程序,它连接主服务,模拟了以假冒方式在旧金山附近移动的若干装置。

![GPS 跟踪器的地图视图](screenshot.jpeg)

整个系统的数据流动情况如下:

* 一个设备网关向每个设备发送定期位置更新 `DeviceGrain`
* `DeviceGrain` 更新单吨 `PushNotifierGrain` , 并更新其位置
* 下 `PushNotifierGrain` 将这些更新集成批次,并推至 `IRemoteLocationHub` 定期投票发现单吨 `HubListGrain`
* `HubListGrain` 维持一份载有 `IRemoteLocationHub` 连接到 Orleans 组群
* 每个主机都有一个 `HubListUpdater` 实例,用于执行 [`BackgroundService`](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services#backgroundservice-base-class) 并定期更新 `HubListGrain` ,并参照其本地 `IRemoteLocationHub` 参考。
* 下 `RemoteLocationHub` 执行工具的类 `IRemoteLocationHub` 具有 [`IHubContext<LocationHub>`](https://docs.microsoft.com/aspnet/core/signalr/hubcontext) 将信息输入到其构建器中。 这样它就可以向连接到 `LocationHub`.

以下图表是上述说明的缩写:

![显示整个系统数据流的图表](./dataflow.png)

下面的例子说明了这个方法如何与多个主机和浏览器一起看。 `PushNotifierGrain` 和 `HubListGrain` 单吨为单吨,因此其中每个实例都有一个。 grains 与集共享。 grains 中 Orleans 只有一个 grain 访问特定类型,例如总是用键调用实例 `0` 或 `Guid.Empty` 或某些其他固定价值,视 grain 是 `IGrainWithIntegerKey` 或 `IGrainWithGuidKey`例如,每个 `DeviceGrain` 获得一个实例 `IPushNotifierGrain` 与id 和 `0`:

```csharp
var notifier = GrainFactory.GetGrain<IPushNotifierGrain>(0);
```

![显示多个主机的图表, 上面分布着粮食](./example.png)

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. 打开解压后的示例目录，再打开其中的 C# 项目文件（`.csproj`）。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 直接运行。
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 在命令行,类型 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

打开三个终端窗口。在第一个终端窗口中,命令提示时运行以下操作:

```dotnetcli
dotnet run --project GPSTracker.Service
```

在第二个终端中,再发射一个主机实例,通过传递以下 `InstanceId` 值来说明它是第二个主机:

```dotnetcli
dotnet run --project GPSTracker.Service -- --InstanceId 1
```

现在打开一个 `http://localhost:5001/index.html`的网络浏览器。 到此点, 在地图周围将没有移动的点 。

在第三个终端窗口中,在命令提示处运行以下操作以启动模拟设备:

```dotnetcli
dotnet run --project GPSTracker.FakeDeviceGateway
```

点将出现在地图上的 Web 浏览器中,并随机在区域周围移动。

<a id="orleans-observability"></a>

## Orleans 易观测

如果你对可观察性感兴趣, 这个样本包括一些选择性的伐木、测量和分布式追踪。

进口 `grafana_dash.json` 启动小组和 grain- 每一排放实例的类型系列:当前人口使用测量速记片,而生命周期的吞吐量则使用反比率或反增长。 Grains 以卡尼基调计的当前气相测量 `grain_type`显示的年长 Orleans 使用CLR的量度 `type` 和名称 `legacy CLR` 用于支持样本已公布的软件包和混合版本升级的图例。 grain- 将这两个方案合并成单一类型总数之前的登记类型。

```docker
docker run -p 9090:9090 --mount type=bind,source="${PWD}/prometheus.yml",target=/etc/prometheus/prometheus.yml prom/prometheus
```

```docker
docker run -d --name jaeger -e COLLECTOR_ZIPKIN_HOST_PORT=:9411 -p 5775:5775/udp -p 6831:6831/udp -p 6832:6832/udp -p 5778:5778 -p 16686:16686 -p 14268:14268 -p 14250:14250 -p 9411:9411 jaegertracing/all-in-one:1.22
```

OR 拉链金:

```docker
docker run -p 9411:9411 openzipkin/zipkin
```
