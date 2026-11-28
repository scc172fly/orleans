---
title: 序列化安全
description: 限制 Orleans 序列化器可接受的类型范围，并验证从客户端及存储接收的数据。
ms.date: 08/17/2026
ms.topic: concept-article
---

<a id="serialization-security"></a>

# 序列化安全

Orleans 对 Grain 调用、响应、请求上下文、持久化状态、提醒器及流数据进行序列化。序列化机制根据另一个进程或提供程序提供的字节重建应用对象。TLS 和提供程序身份验证确定数据来源的身份，应用策略授权操作，领域验证维护不变量，而提供程序控制措施保护已存储的字节。

使用[网络控制](networking.md)、[TLS](../host/transport-layer-security.md)、提供程序身份验证及应用授权限制能够提供序列化输入的主体。然后尽可能缩小每个边界可接受的类型和数据范围。

<a id="keep-type-name-resolution-restricted"></a>

## 限制类型名称解析

Orleans 生成的序列化器使用应用已知的类型清单。外部序列化器可以支持包含运行时类型名称的类型，尤其是在多态契约中。注册外部序列化器会选择编解码器，而类型策略决定该编解码器可以解析哪些 CLR 类型。

<xref:Orleans.Serialization.Configuration.TypeManifestOptions.AllowAllTypes?displayProperty=nameWithType> 默认为 `false`。当任一已连接客户端、流、队列或存储系统可能受到较低信任主体的影响时，应保留默认值。启用该选项会绕过 Orleans 的类型名称验证，允许任何可解析的类型。

开放类型解析范围会让输入选择应用已声明消息契约之外的类型所包含的代码路径。根据已配置的序列化器和可用类型，反序列化利用链行为可能产生非预期副作用或导致代码执行。允许列表可以缩小攻击面，而每个获准类型及序列化器都必须对相应数据来源是安全的。

使用适用范围最窄的机制：

1. 对应用拥有的类型优先采用生成的序列化器和显式 Grain 契约。
1. 使用 <xref:Orleans.Serialization.Configuration.TypeManifestOptions.AddAllowedType*> 逐个添加多态类型。
1. 允许其相关类型属于同一信任边界的程序集。
1. 当信任要求明确策略时，实现 <xref:Orleans.Serialization.ITypeNameFilter> 或 <xref:Orleans.Serialization.ITypeFilter>。

已注册类型名称筛选器的拒绝决定优先于程序集信任。构造泛型类型需要已授权的泛型定义和获准的类型实参；数组元素类型独立检查。已解析类型筛选器对封闭泛型类型的显式授权允许使用该构造泛型类型，但在已解析类型检查过程中，对实参的拒绝决定仍然优先。有关支持的 API 和示例，参阅[配置序列化](../host/configuration-guide/serialization-configuration.md#authorize-type-name-resolution)。

<a id="treat-serializer-extensions-as-security-sensitive"></a>

## 将序列化器扩展视为安全敏感组件

自定义编解码器和通用编解码器在 Orleans 进程内执行。应按处理不可信数据的应用代码对其进行审查：

- 将 `IsSupportedType` 谓词限制在预期契约范围内，并将基于命名空间前缀的选择与显式类型策略结合使用。
- 拒绝格式错误、截断、超限或语义无效的值，并保留此前的状态。
- 在编解码器或应用边界限制集合大小、嵌套深度、内存分配及 CPU 工作量。
- 避免使用会将接受范围扩大到配置策略之外的类型名称处理、反射、回调或构造函数。
- 在处理相应契约的每个 Silo 和客户端上配置相同且兼容的序列化器策略。

[序列化自定义指南](../host/configuration-guide/serialization-customization.md) 介绍扩展点。第三方序列化器或自定义编解码器的安全属性仍由该组件及其配置负责。

<a id="validate-application-data-after-deserialization"></a>

## 在反序列化后验证应用数据

成功反序列化意味着字节符合已配置的格式及类型策略。在更改 Grain 状态或调用依赖项之前，验证标识符、长度、取值范围、状态转换、租户归属及其他领域不变量。

使用只包含各 Grain 操作所需字段的专用请求数据传输类型。将提供程序 SDK 对象、凭据、服务容器及范围广泛的领域对象图保留在其所属进程中；将机密保存在受保护的存储内，确保其不出现在异常消息、请求上下文、Grain 键、遥测或仪表板可见字段中。

持久化状态及排队的流数据遵循提供程序的信任边界。使用遵循最小权限的提供程序身份、平台提供的加密与完整性控制措施，并为需要隔离的环境或租户使用独立存储。序列化器允许列表可以缩小类型解析范围，而提供程序身份验证及完整性控制措施能够确定已存储数据的来源。

有关序列化 Grain 调用的调用方及操作控制，参阅[客户端与 Grain 调用安全](authentication-authorization.md)。
