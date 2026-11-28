---
title: Orleans 中的序列化自定义
description: 了解如何在 .NET Orleans 中自定义序列化。
ms.date: 07/03/2024
uid: orleans-serialization-customization
---

<a id="serialization-customization-in-orleans"></a>

# Orleans 中的序列化自定义

Orleans 的一个重要特性是支持自定义序列化。序列化是将对象或数据结构转换为可存储、可传输、之后可还原的格式的过程。它使开发人员能够控制数据在系统各部分之间发送时的编码和解码方式。自定义序列化可以用于优化性能、互操作性和安全性。

<a id="serialization-providers"></a>

## 序列化提供程序

Orleans 提供两种序列化器实现：

- [Microsoft.Orleans.Serialization.SystemTextJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.SystemTextJson)
- [Microsoft.Orleans.Serialization.NewtonsoftJson](https://nuget.org/packages/Microsoft.Orleans.Serialization.NewtonsoftJson)

有关如何配置任一程序包，请参阅[Orleans 中的序列化配置](serialization-configuration.md)。

<a id="custom-serializer-implementation"></a>

## 自定义序列化器实现

创建自定义序列化器实现通常包含几个步骤。必须实现若干接口，然后将序列化器注册到 Orleans 运行时。以下各节详细说明这些步骤。

首先实现以下 Orleans 序列化接口：

- <xref:Orleans.Serialization.Serializers.IGeneralizedCodec>：支持多种类型的编解码器。
- <xref:Orleans.Serialization.Cloning.IGeneralizedCopier>：提供复制多种类型对象的功能。
- <xref:Orleans.Serialization.ITypeFilter>：允许加载类型并让这些类型参与序列化和反序列化的功能。

请参考以下自定义序列化器实现示例：

:::code language="csharp" source="../../snippets/compiled/Host/HostSnippets.cs" id="custom_serializer":::

在上述示例实现中：

- 显式实现各个接口，以避免方法名称解析冲突。
- 每个方法都会引发 <xref:System.NotImplementedException>，表示该方法尚未实现。需要实现每个方法以提供所需功能。

下一步是将序列化器注册到 Orleans 运行时。通常通过扩展 <xref:Orleans.Serialization.ISerializerBuilder> 并公开自定义的 `AddCustomSerializer` 扩展方法来实现。以下示例演示典型模式：

:::code language="csharp" source="../../snippets/compiled/Host/SerializationRegistrationSnippets.cs" id="register_custom_serializer":::

还可考虑公开一个接受此自定义实现专用序列化选项的重载。这些选项可以在构建器中注册序列化器时一并配置，并通过依赖注入传递给自定义序列化器实现。
