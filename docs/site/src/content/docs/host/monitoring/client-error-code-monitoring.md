---
title: "客户诊断活动"
description: "查找当前 Orleans 客户事件编号,并使用基于症状的观察指导。"
ms.date: 08/02/2026
ms.topic: reference
---

<a id="client-diagnostic-events"></a>

# 客户诊断活动

生成的 <xref:Orleans.ErrorCode> API 引用跟踪运行时当前客户端事件定义。 [`main` 分支错误代码源](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core.Abstractions/Logging/ErrorCodes.cs) 显示正在开发中的定义。 保存结构化的 <xref:Microsoft.Extensions.Logging.EventId>, 类别、 例外、 追踪 ID, 并横跨对日志后端的 ID 字段 。

搜索 <xref:Orleans.ErrorCode>中的结构化事件编号,然后依其类别、例外和周围症状选择路线:

| 诊断区 | 补救措施 |
|---|---|
| 网关发现、连接、 TLS 或重新连接 | [连接和网关失败](troubleshooting-symptom-catalog.md#connection-and-gateway-failures) |
| 请求超时或延迟 | [请求超时](troubleshooting-symptom-catalog.md#request-timeouts) |
| Silo 无法或信件被拒绝 | [Silo 无法或信件被拒绝](troubleshooting-symptom-catalog.md#silo-unavailable-or-message-rejected) |
| 网关繁忙、加载剪裁或拒绝 | [网关或集集超载](troubleshooting-symptom-catalog.md#gateway-or-cluster-overload) |
| 串级化或编码 | [版本更改后发生串级失败](troubleshooting-symptom-catalog.md#serialization-failures-after-a-version-change) |

以持续服务症状为根据,与事件识别和运行信号相关联。 [解释 Orleans 可观测信号](signals.md).
