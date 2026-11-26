---
title: "Silo 诊断事件"
description: "查找当前 Orleans silo 事件编号,并使用基于症状的观察指导。"
ms.date: 08/02/2026
ms.topic: reference
---

<a id="silo-diagnostic-events"></a>

# Silo 诊断事件

生成的 <xref:Orleans.ErrorCode> API 参考跟踪运行时当前 silo 事件定义。 [`main` 分支错误代码源](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core.Abstractions/Logging/ErrorCodes.cs) 显示正在开发中的定义 。

在 <xref:Orleans.ErrorCode>中查找结构化事件编号,然后使用日志类别、例外和周围的度量来选择补救:

| 诊断区 | 补救措施 |
|---|---|
| 成员、未选中的 pings、 silo 状态或 grain 目录 | [成员和目录](troubleshooting-symptom-catalog.md#membership-and-directory-churn) |
| 拒绝、无法使用 silo、连接或网关 | [Silo 无法或信件被拒绝](troubleshooting-symptom-catalog.md#silo-unavailable-or-message-rejected) |
| 调度或长运行启动转 | [长途或陷入僵局 grain](troubleshooting-symptom-catalog.md#long-running-or-deadlocked-grain-turns) |
| 储存提供商、状态版本或节流 | [存储一致性失灵](troubleshooting-symptom-catalog.md#storage-consistency-failures) 和 [储存节压](troubleshooting-symptom-catalog.md#storage-throttling) |
| 串级化或编码 | [版本更改后发生串级失败](troubleshooting-symptom-catalog.md#serialization-failures-after-a-version-change) |
| 提醒或计时器 | [提醒和计时时间](troubleshooting-symptom-catalog.md#reminder-and-timer-timing) |
| 超载、拒绝、记忆或关闭 | [Orleans 症状和信号目录](troubleshooting-symptom-catalog.md) |

以持续服务症状为根据,与事件识别和运行信号相关联。 [解释 Orleans 可观测信号](signals.md).
