<a id="basic-clustering"></a>

# 基本集群

此样本开始两个 Orleans silos, 以 Redis 组成员资格加入它们,
和调用a grain 两者之后 silos 中活动。它只侧重于
一个过程运行 Orleans 并部署生产组。

<a id="what-the-sample-demonstrates"></a>

## 样本显示的样品

- `Aspire.Hosting.Orleans` 供应每种复制品,有一个独 silo 终点和
  共有群集特性。
- Redis 提供集群成员和网关发现。
- `.WithReplicas(2)` 启动同一 silo 项目的两个实例。
- Orleans 将两个呼叫者都引向同一 grain 启动,而不管哪个
  silo 托管主机 。

`UseLocalhostClustering()` 和 `UseDevelopmentClustering()` 是现代的
前 `MembershipTableGrain` 方法的模拟等同物。
仅开发,要求手动协调多个端点时的终点
在一个机器运行的处理程序。此样本使用相同的外部
生产组群作为成员供应者模式,而保养公司则在当地提供
Redis 容器和特定工艺的终点。

<a id="run-the-sample"></a>

## 运行样本

安装 .NET 10 SDK、 占位器 CLI和与多克兼容的容器
运行时。 从此目录运行 :

```powershell
aspire run --apphost BasicClustering.AppHost/BasicClustering.AppHost.csproj
```

在星象仪表板中,打开结构化日志,供 `silo` 复制。
在两种复制合并后,每份报告都报告两个活动 silos 和答复,例如:

```text
The two-silo cluster is ready. Hello from the cluster. Grain 0 is running on S10.0.0.1:11111:...
```

停止在仪表板上复制任何一种, 以查看其余 silo 观察
成员变化。 重新开始返回到silo 组中 。

<a id="production-guidance"></a>

## 生产指南

下 Redis 由 AppHost 用于地方发展。
生产,配置有担保的、高度可用管理的 Redis 服务或
另一得到支持的集群管理提供商,保持集群和服务身份识别稳定
在环境中运行 silo 重复功能,在失败域之间进行复制。
