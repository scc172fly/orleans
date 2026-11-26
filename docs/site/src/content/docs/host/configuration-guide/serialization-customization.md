---
title: "Orleans中序号定制"
description: "学习如何自定义 .NET Orleans中的序列。"
ms.date: 07/03/2024
uid: orleans-serialization-customization
---

<a id="serialization-customization-in-orleans"></a>

# Orleans中序号定制

Orleans 的一个重要方面是支持对序列化的定制,即将对象或数据结构转换成一种可以储存或传输的格式,然后再重建。这样开发者就可以控制数据在系统不同部分之间发送时如何编码和解码。 序列化定制对于优化性能、互操作性和安全可能有用。

<a id="serialization-providers"></a>

## 分级供应者

Orleans 提供两个序列器执行:

- [Microsoft.Orleans.Serialization.SystemTextJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson)
- [Microsoft.Orleans.Serialization.NewtonsoftJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.NewtonsoftJson)

要配置这些软件包中的任何一个,请参见 [Orleans中的序列配置](serialization-configuration.md)。

<a id="custom-serializer-implementation"></a>

## 自定义序列器执行

要创建自定义序列器执行, 需要采取一些共同的步骤。 您必须安装多个接口, 然后将您的序列器注册到 Orleans 运行时。以下章节更详细地描述步骤。

启动程序,先安装以下 Orleans 序列化接口:

- <xref:Orleans.Serialization.Serializers.IGeneralizedCodec>: A codec which supports multiple types.
- <xref:Orleans.Serialization.Cloning.IGeneralizedCopier>: Provides functionality for copying objects of multiple types.
- <xref:Orleans.Serialization.ITypeFilter>: Functionality for allowing types to be loaded and to participate in serialization and deserialization.

考虑以下定制序列器实施实例:

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="custom_serializer":::

在前一实例的实施中:

- 每个接口都明确安装,以避免与方法名称解决办法发生冲突。
- 每个方法都投出 <xref:System.NotImplementedException> 来表示该方法没有被执行。 您需要应用每种方法来提供所需的功能 。

下一步是将您的序列器注册为 Orleans 运行时间。通常通过扩展 <xref:Orleans.Serialization.ISerializerBuilder> 和暴露自定义的 `AddCustomSerializer` 扩展方法来实现这一点。以下示例显示了典型模式:

:::code language="csharp" source="../../snippets/compiled/Host/SerializationRegistrationSnippets.cs" id="register_custom_serializer":::

额外的考虑是暴露超负荷, 从而接受与您定制的安装相关的自定义序列选项。 这些选项可以与在构建器中的注册一同配置。 这些选项可以在您的自定义序列仪实施中注入依赖性 。
