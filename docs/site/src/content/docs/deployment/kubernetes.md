---
title: 在 Kubernetes 上托管 Orleans
description: 为 Kubernetes 上的 Orleans 集群配置网络、生命周期、探针及滚动发布。
ms.date: 08/02/2026
ms.topic: how-to
ms.custom: devops
---

<a id="host-orleans-on-kubernetes"></a>

# 在 Kubernetes 上托管 Orleans

当 Pod 之间具有直接网络连通性且应用程序使用生产级集群提供程序时，Kubernetes 可以托管 Orleans。Orleans 不要求使用 Kubernetes 专用的托管包。推荐的做法是根据 Kubernetes [Downward API](https://kubernetes.io/docs/concepts/workloads/pods/downward-api/) 提供的 Pod 名称和 Pod IP，显式配置每个 Silo。

有关 Azure 网络、工作负载标识、可用区、节点池、自动扩缩容、托管升级及 Azure Monitor 的指南，请参阅[在 Azure Kubernetes Service 上托管 Orleans](azure-kubernetes-service.md)。

<a id="configure-the-silo"></a>

## 配置 Silo

引用 `Microsoft.Orleans.Server` 和生产级集群提供程序包。将 Pod IP 配置为通告地址，监听 Pod 的所有网络接口，并使用 Pod 名称作为 Silo 名称：

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="kubernetes_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_kubernetes_silo":::

所有 Silo 和客户端必须使用相同的服务 ID、集群 ID 和集群提供程序。示例显式使用 Silo 端口 `11111` 和网关端口 `30000`。

<a id="apply-a-production-baseline"></a>

## 应用生产环境基线配置

以下基线配置有意省略集群提供程序凭据和应用程序入口流量配置。请通过工作负载标识及提供程序专用配置提供这些资源。

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

在专用于该应用程序或与该应用程序适当共享的命名空间中应用清单：

```bash
kubectl apply --namespace <namespace> --filename orleans.yaml
```

基线配置禁用了[服务账户令牌挂载](https://kubernetes.io/docs/tasks/configure-pod-container/configure-service-account/)，因为 Orleans 无需调用 Kubernetes API。仅为确有此需求的应用程序功能添加服务账户和权限。

<a id="deploy-with-aspire"></a>

## 使用 Aspire 部署

[Aspire](../host/aspire-integration.md) 可以在 AppHost 中对 Orleans 应用程序及其依赖资源建模。`Aspire.Hosting.Kubernetes` 集成可以将该应用程序模型发布为 Helm Chart，或将其部署到当前 `kubectl` 上下文选定的 Kubernetes 集群。

Aspire 不会改变本页介绍的 Orleans 网络及生命周期要求。在部署生成的资源之前：

- 在 Orleans 资源模型中配置生产级集群提供程序和持久化状态提供程序。
- 跨故障域运行多个 Silo 副本。
- 确保每个 Silo 都获得自己的 Pod 名称和 Pod IP，并通告该 Pod IP。
- 向所需的 Pod 网络和客户端网络开放 Silo 与网关的容器端口。
- 添加由应用程序负责实现的启动、就绪及存活探针。
- 根据实测行为设置资源请求量、中断预算、滚动发布策略和终止宽限期。

应用生成的 Helm Chart 之前，请先审查其内容。如果生成的工作负载未包含这些 Orleans 特定要求，请使用 Kubernetes 集成的资源自定义 API。

有关受支持的 API 和部署命令，请参阅 [Orleans 与 Aspire 集成](../host/aspire-integration.md)、[Aspire Kubernetes 集成](https://aspire.dev/integrations/compute/kubernetes/)和[使用 Aspire 部署](https://aspire.dev/deployment/deploy-with-aspire/)。

<a id="optional-kubernetes-hosting-package"></a>

## 可选的 Kubernetes 托管包

[`Microsoft.Orleans.Hosting.Kubernetes`](https://www.nuget.org/packages/Microsoft.Orleans.Hosting.Kubernetes) 包是可选的，通常不推荐使用。仅在恰好一个 Kubernetes `Deployment` 对象管理恰好一个 Orleans 集群的简单拓扑中考虑使用它。

> [!IMPORTANT]
> 如果 Orleans 集群由多个 `Deployment` 对象、StatefulSet、自定义控制器组成，或者蓝绿、金丝雀工作负载共享同一集群标识，请勿使用此包。应改为显式配置终结点。

对于受支持的简单拓扑，<xref:Orleans.Hosting.KubernetesHostingExtensions.UseKubernetesHosting*> 根据 Pod 元数据配置 Silo 名称、Pod IP、监听终结点、服务 ID 和集群 ID。它还会调用 Kubernetes API，使 Orleans 成员资格与匹配的 Pod 保持一致。它是生产级集群提供程序的补充，而不是替代品。

使用方法：

1. 引用 `Microsoft.Orleans.Hosting.Kubernetes` 并调用 `UseKubernetesHosting()`。
1. 将 `orleans/serviceId` 和 `orleans/clusterId` 标签添加到 `Deployment` 的 Pod 模板中。
1. 通过 Downward API 提供 `POD_NAME`、`POD_NAMESPACE`、`POD_IP`、`ORLEANS_SERVICE_ID` 和 `ORLEANS_CLUSTER_ID`。
1. 挂载专用服务账户令牌并授予以下命名空间范围内的角色权限：

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

将该角色绑定到工作负载的专用服务账户。如果显式启用 <xref:Orleans.Hosting.Kubernetes.KubernetesHostingOptions.DeleteDefunctSiloPods>，还应授予 `delete` 权限。除非已审查该选项带来的运维影响，否则应保持禁用。

<a id="network-requirements"></a>

## 网络要求

允许使用 Pod IP 的直接 TCP 通信：

- 每个 Silo Pod 到每个 Silo Pod 的 `11111` 端口。
- 每个 Orleans 客户端到每个 Silo Pod 的 `30000` 端口。
- 每个 Silo 和客户端到集群提供程序。
- 仅在启用可选托管包时，允许 Silo Pod 访问 Kubernetes API。

不要将 Kubernetes `Service` 的虚拟 IP 配置到 <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress>。Orleans 会通告各 Pod 的 IP，以便其他节点可以连接到特定的 Silo。`Service` 可以暴露应用程序的 HTTP 入口，但不能替代 Orleans 成员资格机制或 Silo 间的直接连通性。

如果服务网格拦截 TCP 通信，请验证长连接、Pod 地址保留、双向 TLS 策略、关闭顺序及重试行为。如果服务网格无法保留所需语义，请将 Orleans 端口排除在拦截范围之外。

<a id="health-probes"></a>

## 健康探针

清单中的三个路径对应[启动探针、就绪探针及存活探针](https://kubernetes.io/docs/tasks/configure-pod-container/configure-liveness-readiness-startup-probes/)，应用程序必须按照[健康检查与可观测性](health-and-observability.md)中的要求实现：

- Silo 加入集群并完成必要的初始化后，启动探针才成功。
- 关闭前以及应用程序无法安全接受新流量时，就绪状态应变为 false。
- 存活探针只执行本地运行进展检查，不调用 Grain 或依赖服务。

五分钟的启动容许时间只是示例，而非通用默认值。应根据实测启动和恢复时间设置。过于激进的探针可能使提供程序延迟或 CPU 压力演变成整个集群的循环重启。

<a id="resource-requests-and-limits"></a>

## 资源请求量与限制

根据代表性负载下的实测结果设置[容器资源请求量与限制](https://kubernetes.io/docs/concepts/configuration/manage-resources-containers/)，测试应包含 Silo 丢失和滚动部署场景。Kubernetes 使用请求量进行调度，使用限制约束运行中的容器；二者不是可以互换的容量设置。

- **CPU 请求量**预留调度容量。如果请求量过低，Kubernetes 可能在同一节点放置过多 Silo，导致突发流量或故障转移期间发生资源争用。
- **CPU 限制**可能在节点仍有空闲 CPU 时对 .NET 进程进行节流。尾部延迟、成员资格探测、垃圾回收及激活恢复都可能因节流而受到影响。基线配置特意设置 CPU 请求量，但不设置 CPU 限制。
- **内存请求量**应覆盖代表性的工作集，包括激活实例、缓存、序列化缓冲区及预期的故障转移增长。
- **内存限制**通过在容器超过 cgroup 限额时终止容器来强制执行。为突发负载和故障转移预留余量；不要依赖托管的 <xref:System.OutOfMemoryException> 或优雅关闭。

监控 CPU 节流、工作集、分配速率、垃圾回收、调度器延迟、激活实例数量和 Pod 重启次数。当 Grain 状态、放置策略、流量、运行时或节点规格发生变化时，重新评估资源设置。

命名空间的 [`ResourceQuota`](https://kubernetes.io/docs/concepts/policy/resource-quotas/) 和 [`LimitRange`](https://kubernetes.io/docs/concepts/policy/limit-range/) 策略可能拒绝 Pod 或注入默认值。应在准入后验证实际生效的 Pod 规范，而不要假定提交的清单就是实际运行的配置。

有关工作负载容量估算、过载保护及扩缩容信号，请参阅[容量规划与扩缩容](capacity-planning.md)。

<a id="shutdown-rollouts-and-scaling"></a>

## 关闭、滚动发布与扩缩容

Kubernetes 发送 `SIGTERM` 并开始计算 Pod 的终止宽限期。.NET 主机必须观察到该信号、报告未就绪状态，并获得充足时间以优雅地停止 Orleans。示例中的 Pod 宽限期为 150 秒，长于主机 120 秒的关闭超时。

当集群无法承受滚动发布期间丢失一个就绪 Silo 时，请保持 `maxUnavailable: 0` 并预留一定的额外副本容量。PodDisruptionBudget 防范的是主动中断，而非节点故障。

逐步缩容。在移除更多 Pod 之前，请验证成员资格已趋于稳定且剩余 Silo 具有足够容量。参阅[优雅关闭与升级](upgrades.md)。

<a id="troubleshoot-kubernetes-hosting"></a>

## 排查 Kubernetes 托管故障

对于显式配置的终结点，请比较 `POD_NAME`、`POD_IP`、服务 ID、集群 ID 和成员资格中通告的终结点。

如果可选托管包报告缺少 `KUBERNETES_SERVICE_HOST` 或 `KUBERNETES_SERVICE_PORT`，请验证进程是否在 Pod 中运行，以及服务环境变量链接是否被禁用。如果 API 返回 `403 Forbidden`，请检查 Pod 的服务账户、角色绑定所在命名空间和所需的 Pod 操作权限。

有关更全面的故障初步诊断，请参阅[排查部署故障](troubleshooting-deployments.md)。有关部署目标比较及通用生产能力覆盖矩阵，请参阅[选择部署目标](choose-deployment-target.md)。
