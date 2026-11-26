---
title: "东道主主 Orleans 日期 时 Kubernetes"
description: "配置网络、生命周期、探测和推出 Orleans 组群一一 Kubernetes."
ms.date: 08/02/2026
ms.topic: how-to
ms.custom: devops
---

<a id="host-orleans-on-kubernetes"></a>

# 东道主主 Orleans 日期 时 Kubernetes

Kubernetes 能够主机 Orleans 当播客具有直接网络连接,而且应用程序使用生产集群供应商时。 Orleans 不需要 Kubernetes- 特定的主机软件包。推荐的方法是配置每个 silo 由 IP 提供的 pod 名称和 cod IP 明确 Kubernetes [[下向 API](https://kubernetes.io/docs/concepts/workloads/pods/downward-api/).

Azure 联网、工作量识别、可用区、节点集合、自动升级、管理下的升级和 Azure 监测指南见 [东道主主 Orleans 日期 时 Azure Kubernetes 服务处](azure-kubernetes-service.md)。

<a id="configure-the-silo"></a>

## 配置 silo

引用 `Microsoft.Orleans.Server` 和一个生产组群提供商软件包。 配置 pod IP 作为广告地址, 监听所有 pod 接口, 并使用 pod 名称作为 silo 名称 :

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="kubernetes_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_kubernetes_silo":::

所有 silos 和客户必须使用相同的服务编号、组群识别和集群提供者。示例明确使用了 silo 港口 `11111` 和网关港口 `30000`。

<a id="apply-a-production-baseline"></a>

## 适用生产基准

以下基线有意省略集群提供商的证书和应用程序。

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dictionary-app
spec:
  replicas: 3
  minReadySeconds: 30
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 0
      maxSurge: 1
  selector:
    matchLabels:
      app.kubernetes.io/name: dictionary-app
  template:
    metadata:
      labels:
        app.kubernetes.io/name: dictionary-app
    spec:
      automountServiceAccountToken: false
      terminationGracePeriodSeconds: 150
      containers:
        - name: app
          image: registry.example.com/dictionary-app:10.0.0
          imagePullPolicy: IfNotPresent
          ports:
            - name: http
              containerPort: 8080
              protocol: TCP
            - name: silo
              containerPort: 11111
              protocol: TCP
            - name: gateway
              containerPort: 30000
              protocol: TCP
          env:
            - name: POD_NAME
              valueFrom:
                fieldRef:
                  fieldPath: metadata.name
            - name: POD_IP
              valueFrom:
                fieldRef:
                  fieldPath: status.podIP
            - name: ORLEANS_SERVICE_ID
              value: dictionary-app
            - name: ORLEANS_CLUSTER_ID
              value: production
          startupProbe:
            httpGet:
              path: /health/startup
              port: http
            periodSeconds: 5
            timeoutSeconds: 2
            failureThreshold: 60
          readinessProbe:
            httpGet:
              path: /health/ready
              port: http
            periodSeconds: 5
            timeoutSeconds: 2
            failureThreshold: 2
          livenessProbe:
            httpGet:
              path: /health/live
              port: http
            periodSeconds: 10
            timeoutSeconds: 2
            failureThreshold: 3
          resources:
            requests:
              cpu: 500m
              memory: 512Mi
            limits:
              memory: 1Gi
---
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata:
  name: dictionary-app
spec:
  minAvailable: 2
  selector:
    matchLabels:
      app.kubernetes.io/name: dictionary-app
```

应用专用于或适当共享应用程序的名称空间中的显示器:

```bash
kubectl apply --namespace <namespace> --filename orleans.yaml
```

基准禁用 [服务账户代号正在安装](https://kubernetes.io/docs/tasks/configure-pod-container/configure-service-account/) ,因为 Orleans 不需要调用 Kubernetes API。仅对需要应用功能的应用程序添加服务账户和权限。

<a id="deploy-with-aspire"></a>

## 与 派遣部署

[指定](../host/aspire-integration.md) 能够模拟 Orleans 及其支持资源在 AppHost. The `Aspire.Hosting.Kubernetes` 集成可将该应用模型作为 Helm 图表发布,或将其用于 Kubernetes 由当前列表选择的 `kubectl` 上下文。

提示不会改变本页描述的 Orleans 网络和生命周期要求。 在部署生成的资源之前:

- 在 Orleans 资源模型中配置生产组群供应商和耐久国家供应商。
- 运行多个 silo 重复, 并运行多个失败域的复制。
- 确保每个 silo 都获得自己的舱名和舱名IP,并广告贴出舱名IP。
- 向所需舱位和客户网络展示 silo 和集装箱港口网关。
- 添加应用程序自有启动、准备状态和活性探测。
- 设定资源请求、干扰预算、推出政策以及从计量行为中得出的终止宽限期。

在应用之前先审查生成的 Helm 图表。使用 Kubernetes 当产生的工作量不包括这些工作量时,资源自定制的API Orleans- 具体要求。

所支持的API和部署命令见 [Orleans 和指定集成](../host/aspire-integration.md)、 [指定 Kubernetes 整合](https://aspire.dev/integrations/compute/kubernetes/)和 [与 派遣部署](https://aspire.dev/deployment/deploy-with-aspire/)。

<a id="optional-kubernetes-hosting-package"></a>

## 可选 Kubernetes 主机包

[`Microsoft.Orleans.Hosting.Kubernetes`](https://www.nuget.org/packages/Microsoft.Orleans.Hosting.Kubernetes) 软件包是可选的,一般不推荐。 仅考虑一个简单的表层, 其中一 Kubernetes `Deployment` 对象拥有一个 Orleans 集。

> [!IMPORTANT]
> 不要用包包当 Orleans 由多个 `Deployment` 物体, StatefulSets,自定义控制器,或蓝绿色或金丝雀工作量,共享集束特性。而明确配置端点。

对于支持的简单地表学来说 <xref:Orleans.Hosting.KubernetesHostingExtensions.UseKubernetesHosting*> 配置 silo IP 名称、 IP 名称、 监听端点、 服务 ID 和 Cdom 元数据中的集号 。 它还呼吁 Kubernetes API 调节和对账 Orleans 与相配的播客组。 它补充生产集群提供商; 它不替换一个。

使用它:

1. 参考 `Microsoft.Orleans.Hosting.Kubernetes` ,拨打 `UseKubernetesHosting()`。
1. 将 `orleans/serviceId` 和 `orleans/clusterId` 标签添加到 `Deployment` 舱位模板中。
1. 供应 `POD_NAME`, `POD_NAMESPACE`, `POD_IP`, `ORLEANS_SERVICE_ID`,和 `ORLEANS_CLUSTER_ID` 下至 API.
1. 挂载一个专用服务账户代号, 并授予此命名空间范围作用 :

```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: orleans-hosting
rules:
  - apiGroups: [""]
    resources: ["pods"]
    verbs: ["get", "list", "watch", "patch"]
```

将这一作用绑在工作量专用服务账户中。 如果您明确启用了 <xref:Orleans.Hosting.Kubernetes.KubernetesHostingOptions.DeleteDefunctSiloPods>, 也准予使用 `delete`。 保持该选项的功能性, 除非已经审查其操作后果 。

<a id="network-requirements"></a>

## 网络需求

允许直接的缓冲点IP TCP 流量 :

- 每一 silo pod 移动到端口 `11111` 上每 silo pod.
- 每一 Orleans 到客户端端口 `30000` 上每 silo pod.
- 每个 silo 和集群提供者的客户。
- 只有当启用了可选主机包时,Silo 舱才切换到 Kubernetes API 。

不要将 Kubernetes `Service` 虚拟IP放在 <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress>。 Orleans 广告每个播客IP,以便同侪能够联系到特定的 silo。一个 `Service` 可以暴露应用 HTTP 的入侵,但它不能取代 Orleans 成员资格或直接连接 silo 。

如果服务网格拦截 TCP, 验证长寿命连接、 缓冲地址保护、 相互的 TLS 政策、 关闭命令和重试。 如果网格无法保存所需的语义, 则排除 Orleans 港口被截获 。

<a id="health-probes"></a>

## 健康检测

舱中的三个道路, [启动、准备和活性探测](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/) 申请必须依照 [健康和可观察性](health-and-observability.md):

- silo 加入该组并完成所需的启动完成后,启动成功。
- 关闭前准备就绪是假的,只要申请无法安全地接受新的交通。
- 生命只进行本地的前瞻性检查,不呼叫 grains 或依赖关系。

5分钟启动允许是一个例子, 不是通用的默认。 从测量的启动和恢复时间设置它。 侵略性探测可以将供应商的延迟或CPU压力转换为整个集束范围的重新启动循环 。

<a id="resource-requests-and-limits"></a>

## 资源要求和限额

设定 [要求和限制](https://kubernetes.io/docs/concepts/configuration/manage-resources-containers/) 包括 a 和 a 等具有代表性载量下的测量数据,包括 silo 损失和滚动部署。 Kubernetes 使用时间和用途限制请求,以限制运行中的集装箱;它们不是可互换的能力环境。

- **CPU 请求** 如果请求太少, Kubernetes 位置可放置太多 silos 在一个节点上,在连续或失败期间引起争论。
- **CPU 限制** 能够加速 .NET 即使在节点有备用 CPU 时, 程序也会受到冲击。 尾部延缓、 会员探测器、 垃圾收集以及激活恢复都会受到冲击。 基准有意指定 CPU 请求没有CPU 限制 。
- **内存请求** 应涵盖一套具有代表性的工作,包括启动、缓冲、序列缓冲和预期的不相容增长。
- 内存限制**通过在容器超过组限时终止容器** 加以执行。离开会议室,可以连续和故障;不要依赖管理良好的 <xref:System.OutOfMemoryException> 或优雅的关闭。

监控 CPU 的抽压、 工作设置、 分配率、 垃圾收集、 排程延迟、 激活计数和缓存重新启动。 当 grain 状态、 位置、 流量、 运行时间或节点大小发生变化时, 重现资源设置 。

命名空间 [`ResourceQuota`](https://kubernetes.io/docs/concepts/policy/resource-quotas/) 和 [`LimitRange`](https://kubernetes.io/docs/concepts/policy/limit-range/) 策略可以拒绝缓存或输入默认值。 进入后验证有效的缓存规格,而不是假设提交的舱单是运行的 。

工作量大小、超载防护和缩放信号见 [能力规划和规模](capacity-planning.md)。

<a id="shutdown-rollouts-and-scaling"></a>

## 关闭、推出和升级

Kubernetes 发送 `SIGTERM` 并开始播客舱终止宽限期。 .NET 主机必须观察信号,报告未准备好,并获得足够时间优雅地停止 Orleans 。 例如,150秒的播客宽限期比120秒的主机停机超时长。

保留 `maxUnavailable: 0` 和一些快速增援能力 当集群无法忍受失去一个准备 silo 时段展开。 PodDisruptionBudget 保护不受自愿中断,而不是节点故障的影响。

逐步按比例。在删除更多的播客之前,核查会籍是否稳定且剩余 silos 具有能力。见 [优优优停机和升级](upgrades.md)。

<a id="troubleshoot-kubernetes-hosting"></a>

## 排除 Kubernetes 主机

对于明确的端点配置,比较 `POD_NAME`、 `POD_IP`、服务编号、组群识别和公布的会员端点。

如果选择主机软件包报告丢失 `KUBERNETES_SERVICE_HOST` 或 `KUBERNETES_SERVICE_PORT`, 请确认该程序是在一个舱内运行的, 而服务环境链接没有被禁用。 如果 API 返回 `403 Forbidden`, 请检查舱的服务账户、 角色约束命名空间 和所需的舱动词 。

更广泛的分类见 [故障解决部署](troubleshooting-deployments.md)。目标比较和共同生产覆盖矩阵见 [选择部署目标](choose-deployment-target.md)。
