<a id="basic-clustering"></a>

# 基础集群

本示例启动两个 Orleans Silo，通过 Redis 集群成员资格使它们加入同一集群，
并在两个 Silo 都处于活动状态后调用 Grain。它仅关注从单进程运行 Orleans
到部署生产集群之间的过渡步骤。

<a id="what-the-sample-demonstrates"></a>

## 示例演示的内容

- `Aspire.Hosting.Orleans` 为每个副本提供唯一的 Silo 终结点和
  共享的集群标识。
- Redis 提供集群成员资格及网关发现。
- `.WithReplicas(2)` 启动同一 Silo 项目的两个实例。
- 无论哪个 Silo 托管该激活实例，Orleans 都会将两个调用方的调用
  路由到同一个 Grain 激活实例。

`UseLocalhostClustering()` 和 `UseDevelopmentClustering()` 是原先
`MembershipTableGrain` 方式的现代内存实现。它们仅供开发使用，
当多个进程在同一台计算机上运行时，需要手动协调终结点。
本示例改为使用与生产集群相同的外部成员资格提供程序模型，
同时由 Aspire 提供本地 Redis 容器及
各进程专用的终结点。

<a id="run-the-sample"></a>

## 运行示例

安装 .NET 10 SDK、Aspire CLI 和兼容 Docker 的容器运行时。
在当前目录中运行：

```powershell
aspire run --apphost BasicClustering.AppHost/BasicClustering.AppHost.csproj
```

在 Aspire 仪表板中，打开任意一个 `silo` 副本的结构化日志。
两个副本加入后，各自会报告两个活动 Silo，并输出类似如下的响应：

```text
The two-silo cluster is ready. Hello from the cluster. Grain 0 is running on S10.0.0.1:11111:...
```

在仪表板中停止任意一个副本，观察剩余 Silo 检测到成员资格变化。
重新启动该副本，即可恢复为双 Silo 集群。

<a id="production-guidance"></a>

## 生产环境指导

AppHost 创建的 Redis 容器用于本地开发。在生产环境中，
应配置安全且高可用的托管 Redis 服务或其他受支持的集群提供程序，
保持同一环境内的集群 ID 与服务 ID 稳定，
并跨故障域运行 Silo 副本。
