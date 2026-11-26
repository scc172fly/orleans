---
title: "Orleans 如何指导"
description: "配置、部署和操作 Orleans 应用程序的面向任务的方法。"
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="orleans-how-to-guides"></a>

# Orleans 如何指导

当您有具体任务要完成时使用这些配方。 每份指南都侧重于先决条件、最短支持路径以及确认修改有效的检查。

如果您正在从空目录中学习 Orleans ,请从 [教程和徒步](../tutorials-and-samples/index.md)开始。这些任务背后的想法见 [Orleans 概念](../overview.md) 和 [为什么 Orleans?](../benefits.md)。关于建议后面的运行时行为,请见 [建筑和内部](../implementation/index.md)。

<a id="configure-hosting-and-providers"></a>

## 配置主机和提供者

- [配置 silo](../host/configuration-guide/server-configuration.md)
- [配置外部客户端](../host/configuration-guide/client-configuration.md)
- [选择一个典型配置](../host/configuration-guide/typical-configurations.md)
- [配置 ADO.NET 提供者](../host/configuration-guide/configuring-ado-dot-net-providers.md)
- [配置 Conser 群集](../host/configuration-guide/clustering/consul.md)
- [配置序列序列化](../host/configuration-guide/serialization-configuration.md)
- [配置 TLS](../host/transport-layer-security.md)
- [配置 aspire 集成](../host/aspire-integration.md)

<a id="deploy-and-operate"></a>

## 部署和作业

- [审查生产准备状态](../deployment/production-readiness.md)
- [配置地形、联网和集群](../deployment/networking.md)
- [部署到 Kubernetes](../deployment/kubernetes.md)
- [部署到 Azure 服务处](../deployment/deploy-to-azure-app-service.md)
- [部署到 Azure 集装箱应用程序](../deployment/deploy-to-azure-container-apps.md)
- [计划能力及规模](../deployment/capacity-planning.md)
- [执行优雅升级](../deployment/upgrades.md)
- [计划灾后恢复](../deployment/disaster-recovery.md)

<a id="use-orleans-features"></a>

## 使用 Orleans 功能

- [常持久性 grain 状态](../grains/grain-persistence/index.md)
- [配置实验性日记](../grains/journaling/configuration.md)
- [实施信息短信工作流程](../grains/messaging-recipes.md)
- [运行发送信息](../grains/messaging-operations.md)
- [与 grain 计时器一起的启动范围工程](../grains/timers.md)
- [安排有催复通知的持久工作](../grains/reminders.md)
- [执行长期催复通知](long-running-reminders.md)
- [配置亚马逊 DynamoDB 提醒](../grains/reminders/dynamodb.md)
- [配置流](../streaming/stream-providers.md)
- [使用响应流](../grains/response-streaming.md)
- [使用无国籍工人 grains](../grains/stateless-worker-grains.md)
- [测试 Orleans 应用程序](../grains/testing.md)

<a id="diagnose-and-recover"></a>

## 诊断和恢复

- [解决部署中的麻烦](../deployment/troubleshooting-deployments.md)
- [排除故障 Orleans 事件](../host/monitoring/troubleshooting.md)
- [监视器 silo 和客户端错误代码](../host/monitoring/silo-error-code-monitoring.md)
- [配置信号和警报](../host/monitoring/signals.md)
- [处理失败和不确定结果](../deployment/handling-failures.md)
- [排除故障实验 Orleans 日记](../grains/journaling/operations.md)

对于公共类型、默认、例外和超载,请使用 [C# API 参考](https://dotnet.github.io/orleans/docs/api/csharp/)。参考条目提供精确的合同;上面的配方显示如何应用它。
