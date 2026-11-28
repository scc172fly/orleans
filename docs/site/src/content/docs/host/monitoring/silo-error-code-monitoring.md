---
title: Silo 诊断事件
description: 查找当前 Orleans Silo 事件 ID，并使用基于症状的可观测性指南。
ms.date: 08/02/2026
ms.topic: reference
---

<a id="silo-diagnostic-events"></a>

# Silo 诊断事件

生成的 <xref:Orleans.ErrorCode> API 参考反映运行时当前的 Silo 事件定义。[`main` 分支中的错误码源码](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core.Abstractions/Logging/ErrorCodes.cs) 展示正在开发的定义。

在 <xref:Orleans.ErrorCode> 中搜索结构化事件 ID，然后根据其日志类别、异常及相关指标选择处理方法：

| 诊断领域 | 处理方法 |
|---|---|
| 成员资格、探测未收到响应、Silo 状态或 Grain 目录 | [成员资格及目录频繁变动](troubleshooting-symptom-catalog.md#membership-and-directory-churn) |
| 消息拒绝、Silo 不可用、连接或网关 | [Silo 不可用或消息被拒绝](troubleshooting-symptom-catalog.md#silo-unavailable-or-message-rejected) |
| 调度器或长时间运行的激活实例执行轮次 | [长时间运行或死锁的 Grain 执行轮次](troubleshooting-symptom-catalog.md#long-running-or-deadlocked-grain-turns) |
| 存储提供程序、状态版本或限流 | [存储一致性故障](troubleshooting-symptom-catalog.md#storage-consistency-failures)和[存储限流](troubleshooting-symptom-catalog.md#storage-throttling) |
| 序列化或编解码器 | [版本变更后的序列化故障](troubleshooting-symptom-catalog.md#serialization-failures-after-a-version-change) |
| 提醒器或定时器 | [提醒器与定时器的触发时序](troubleshooting-symptom-catalog.md#reminder-and-timer-timing) |
| 过载、拒绝、内存或关闭 | [Orleans 症状与信号目录](troubleshooting-symptom-catalog.md) |

基于与事件 ID 及运行时信号关联的持续服务症状设置告警。遵循[解读 Orleans 可观测性信号](signals.md) 中的指南。
