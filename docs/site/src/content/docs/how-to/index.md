---
title: Orleans 操作指南
description: 用于配置、部署及运行 Orleans 应用的面向任务的操作方案。
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="orleans-how-to-guides"></a>

# Orleans 操作指南

需要完成特定任务时，使用这些操作方案。每篇指南重点介绍前置条件、最短的受支持路径及确认变更生效的检查方法。

如果从空目录开始学习 Orleans，请先阅读[教程与分步演练](../tutorials-and-samples/index.md)。有关这些任务背后的概念，参阅 [Orleans 概念](../overview.md)及[为什么选择 Orleans？](../benefits.md)。有关建议背后的运行时行为，参阅[架构与内部实现](../implementation/index.md)。

<a id="configure-hosting-and-providers"></a>

## 配置托管及提供程序

- [配置 Silo](../host/configuration-guide/server-configuration.md)
- [配置外部客户端](../host/configuration-guide/client-configuration.md)
- [选择典型配置](../host/configuration-guide/typical-configurations.md)
- [配置 ADO.NET 提供程序](../host/configuration-guide/configuring-ado-dot-net-providers.md)
- [配置 Consul 集群成员资格](../host/configuration-guide/clustering/consul.md)
- [配置序列化](../host/configuration-guide/serialization-configuration.md)
- [配置 TLS](../host/transport-layer-security.md)
- [配置 Aspire 集成](../host/aspire-integration.md)

<a id="deploy-and-operate"></a>

## 部署与运维

- [检查生产就绪情况](../deployment/production-readiness.md)
- [配置拓扑、网络及集群成员资格](../deployment/networking.md)
- [部署到 Kubernetes](../deployment/kubernetes.md)
- [部署到 Azure App Service](../deployment/deploy-to-azure-app-service.md)
- [部署到 Azure Container Apps](../deployment/deploy-to-azure-container-apps.md)
- [规划容量及扩缩容](../deployment/capacity-planning.md)
- [执行优雅升级](../deployment/upgrades.md)
- [规划灾难恢复](../deployment/disaster-recovery.md)

<a id="use-orleans-features"></a>

## 使用 Orleans 功能

- [持久化 Grain 状态](../grains/grain-persistence/index.md)
- [配置实验性 Journaling 功能](../grains/journaling/configuration.md)
- [实现消息工作流](../grains/messaging-recipes.md)
- [运行及维护消息传递](../grains/messaging-operations.md)
- [使用 Grain 定时器调度激活实例作用域内的工作](../grains/timers.md)
- [使用提醒器调度持久化工作](../grains/reminders.md)
- [实现长期运行的提醒器](long-running-reminders.md)
- [配置 Amazon DynamoDB 提醒器](../grains/reminders/dynamodb.md)
- [配置流](../streaming/stream-providers.md)
- [使用响应流式传输](../grains/response-streaming.md)
- [使用无状态工作者 Grain](../grains/stateless-worker-grains.md)
- [测试 Orleans 应用](../grains/testing.md)

<a id="diagnose-and-recover"></a>

## 诊断与恢复

- [排查部署故障](../deployment/troubleshooting-deployments.md)
- [排查 Orleans 故障事件](../host/monitoring/troubleshooting.md)
- [监控 Silo 及客户端错误码](../host/monitoring/silo-error-code-monitoring.md)
- [配置信号与告警](../host/monitoring/signals.md)
- [处理故障及结果不确定的操作](../deployment/handling-failures.md)
- [排查实验性 Orleans Journaling 功能故障](../grains/journaling/operations.md)

有关公共类型、默认值、异常及重载，使用 [C# API 参考](https://dotnet.github.io/orleans/docs/api/csharp/)。参考条目提供精确契约；上述操作方案展示如何应用这些契约。
