---
title: "逐步化安全"
description: "限制 Orleans 序列器类型表面,并验证从客户和储存处收到的数据。"
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="serialization-security"></a>

# 逐步化安全

Orleans 序列 grain 电话、回复、请求背景、持续状态、提醒和流数据。序列化从另一个程序或供应商提供的字节重建应用对象。TLS和提供者认证确定源身份,应用政策授权操作,域验证强制执行变量,以及供应商控制保护存储的字节。

使用 [网络控制](networking.md)、 [TLS 电信](../host/transport-layer-security.md)、提供者认证和应用授权,限制谁能够提供序列输入。然后将每一边界接受的一组类型和数据尽量缩小。

<a id="keep-type-name-resolution-restricted"></a>

## 保持类型名称分辨率限制

Orleans- 生成的序列器使用应用程序已知类型显示。外部序列器可以支持包括运行时型号名称的类型,特别是在多式合同中。注册外部序列器可选择编码器,而类型政策则决定了代码器可能解密的CLR类型。

<xref:Orleans.Serialization.Configuration.TypeManifestOptions.AllowAllTypes?displayProperty=nameWithType> 默认为 `false`。 保存在任何连接客户端、 流、 队列或存储系统受信任度较低的一方影响时的默认值。 启用它可以绕过 Orleans 类型名称验证并允许任何可解开的类型 。

开放类型分辨率表面可以输入来自应用程序已声明信件合同之外类型的代码路径。 根据配置的序列器和可用类型, 消毒工具行为可能导致意外副作用或代码执行。 允许列表会减少攻击表面, 而每个允许的类型和序列器必须安全到数据源中。

使用最狭义的适用机制:

1. 更倾向于生成序列器和明确 grain 合同,用于申请方所属类型的。
1. 添加 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddAllowedType*>的单个多形态类型。
1. 允许其相关类型属于同一信托边界内的集会。
1. 在信任需要明确政策时,执行 <xref:Orleans.Serialization.ITypeNameFilter> 或 <xref:Orleans.Serialization.ITypeFilter> 。

注册类型名称过滤器的拒绝优先于组装信任。 构建通用类型需要授权定义和批准参数; 矩阵元素类型独立检查。 对于封闭式通用类型,明确解决型过滤器授予了该工程,而争论拒绝则在确定型类型的检查中仍居于优先地位。 支持的 API 和示例见 [配置序列序列化](../host/configuration-guide/serialization-configuration.md#authorize-type-name-resolution) 。

<a id="treat-serializer-extensions-as-security-sensitive"></a>

## 处理器序列分机扩展号具有安全敏感性

自定义和通用编码在 Orleans 进程内执行。将其作为处理不信任数据的应用编码审查:

- 将 `IsSupportedType` 前提限制在预定合同的范围内,并采用明确类型政策选择对称空间前缀。
- 拒绝错误、短跑、超大或语义无效的价值观,并维护先前状态。
- 边界的收集大小、嵌套、分配和CPU在编码或应用边界的工作。
- 避免类型名称处理、反射、回击或构造器将已接受表面扩展到配置的策略之外。
- 配置对处理合同的每个 silo 和客户的相同的兼容序列器保单。

[定序序列定制指导](../host/configuration-guide/serialization-customization.md) 描述扩展点,第三方序列器或海关编码器的安全特性仍由该构成部分及其配置负责。

<a id="validate-application-data-after-deserialization"></a>

## 解除空气循环后验证应用数据

成功去行化确认字节符合配置的格式和类型政策。 在改变 grain 状态或援引依赖性之前,验证识别符号、长度、范围、州过渡、租户所有权和其他域名。

使用每项 grain 作业所需的字段的专用请求数据传输类型; 将SDK供应商的物体、证书、服务集装箱和广域图保存在其拥有过程中,并将秘密保存在受保护商店中,这些商店不包含例外信息、请求背景、 grain 密钥、遥测和仪表板可视域。

固定状态和队列流数据遵循供应商的信任边界。使用平台提供的最优惠供应商身份、加密和完整性控制,并为环境或需要隔离的租户分别储存。 连环器允许列表可以减少类型分辨率表面,而供应商认证和完整性控制则能确定存储数据的出处。

有关按序号 grain 调用电话的调用和操作控制,见 [和客户端 grain- 调用安全](authentication-authorization.md)。
