---
title: 客户端诊断事件
description: 查找当前 Orleans 客户端事件 ID，并使用基于症状的可观测性指南。
ms.date: 08/02/2026
ms.topic: reference
---

<a id="client-diagnostic-events"></a>

# 客户端诊断事件

生成的 <xref:Orleans.ErrorCode> API 参考反映运行时当前的客户端事件定义。[`main` 分支中的错误码源码](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core.Abstractions/Logging/ErrorCodes.cs) 展示正在开发的定义。在日志后端保留结构化的 <xref:Microsoft.Extensions.Logging.EventId>、类别、异常、跟踪 ID 和跨度 ID 字段。

在 <xref:Orleans.ErrorCode> 中搜索结构化事件 ID，然后根据其类别、异常及相关症状选择处理方向：

| 诊断领域 | 处理方法 |
|---|---|
| 网关发现、连接、TLS 或重连 | [连接与网关故障](troubleshooting-symptom-catalog.md#connection-and-gateway-failures) |
| 请求超时或延迟 | [请求超时](troubleshooting-symptom-catalog.md#request-timeouts) |
| Silo 不可用或消息被拒绝 | [Silo 不可用或消息被拒绝](troubleshooting-symptom-catalog.md#silo-unavailable-or-message-rejected) |
| 网关繁忙、负载削减或拒绝 | [网关或集群过载](troubleshooting-symptom-catalog.md#gateway-or-cluster-overload) |
| 序列化或编解码器 | [版本变更后的序列化故障](troubleshooting-symptom-catalog.md#serialization-failures-after-a-version-change) |

基于与事件 ID 及运行时信号关联的持续服务症状设置告警。遵循[解读 Orleans 可观测性信号](signals.md) 中的指南。
