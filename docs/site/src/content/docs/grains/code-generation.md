---
title: "Orleans 源产生"
description: "理解 grains 和序列化的构建时间代码生成 Orleans。"
ms.date: 09/01/2026
ms.topic: concept-article
---

<a id="orleans-source-generation"></a>

# Orleans 源产生

Orleans 生成 grain 代理器、 方法发送代码、 序列器和复印机, 用于构建时。 没有运行时间或初始化时间生成程序代码的应用程序代码工作流程 。

<a id="reference-the-sdk"></a>

## 参考 SDK

使用匹配项目角色的软件包 :

- 客户申请的[Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client) 。
- [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server) 用于 silo 应用程序。
- [Microsoft.Orleans.Sdk](https://www.nuget.org/packages/Microsoft.Orleans.Sdk) 适用于含有 grain 合同、执行或可连号类型的类图书馆。

这些包包括源生成器和分析器。

<a id="grain-contracts"></a>

## Grain 合同

发电机发现 grain 接口及其 Orleans 基准界面。 它报告为不支持的签名、 不可访问类型、 多取消代号参数和其他合同错误建立诊断。

支持的 grain 方法返回类型为 <xref:System.Threading.Tasks.Task>、 <xref:System.Threading.Tasks.Task`1>、 <xref:System.Threading.Tasks.ValueTask>和 <xref:System.Threading.Tasks.ValueTask`1>。生成器为这些方法提供了强烈的输入参考和招工类别。

<a id="serializable-types"></a>

## 分级类型

标记应用程序数据跨越 grain 边界,或由 Orleans 以 <xref:Orleans.GenerateSerializerAttribute>储存。

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="serializable_purchase_order":::
ID是电线和存储合同的一部分。 不要在部署后重新使用或重编号它们。 在需要固定序列化别名时使用 <xref:Orleans.AliasAttribute> , 且不需使用 CLR 名称 。

<a id="hot-reload-for-serializable-members"></a>

## 用于可连序成员的热重充载

开发中启用热再载生成形状, 建立:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Debug'">
  <OrleansHotReload>true</OrleansHotReload>
</PropertyGroup>
```

发电机提供序列机和复印机场稳定的身份,并在现有生成的系统首次使用时解决新添加的具体编码和相继性。 因此,运行中的应用程序可以应用 .NET 添加强键入的热重充热重新加更新 `[Id]` 成员加入成员 `[GenerateSerializer]` 包括具体可连号类型被添加在同一更新中的成员。

每个保存 `[Id]` 增加成员更新保留现有的电线合同。 grain 界面, 并生成的可引用类型 .NET 热装货和 Orleans 重新开始在更新后重建序列器清单,更新后通过多形态声明(例如: `object` 或界面,或通过容器元件类型。

当 `OrleansHotReload` 被卸载或 `false`时,生成器会发出只读的字段,且对正常应用程序进行急切初始化,从而建立。

<a id="generate-code-for-external-types"></a>

## 生成外部类型代码

当项目必须生成可获取类型在别处申报的序列器时,使用 <xref:Orleans.GenerateCodeForDeclaringAssemblyAttribute>:

:::code language="csharp" source="../snippets/compiled/Grains/GeneralSnippets.cs" id="generate_external_contract":::
尽可能以该类型优先拥有序列说明。 产生外部申报组件会扩大兼容面,并可以增加构建输出。

<a id="other-net-languages"></a>

## 其他 .NET 语文

端对端的插件示例见 [Orleans F# 样本](https://learn.microsoft.com/samples/dotnet/samples/orleans-fsharp-sample/) 和 [Orleans 视觉基本样本](https://learn.microsoft.com/samples/dotnet/samples/orleans-vb-sample/)。

<a id="inspect-diagnostics-and-output"></a>

## 检查诊断和产出

将 Orleans 分析器和发电机诊断作为合同错误处理。 Orleans 标记生成了执行类型,给符合此元数据的编辑提供 `EditorBrowsable(Never)` 。生成的文件保留其源位置进行诊断和调试,并通过编译者产生的文件工具以及直接对生成符号进行定义导航,仍然可用。

Roslyn荣誉级别 `EditorBrowsable` 成员完成、相同组装型的完成和执行搜索包括产生的符号,而其执行树参与汇编。

发电机登记了Rassizer、复印机、活动者、代理人、职业助手、运行时元数据以及Roslyn `RegisterImplementationSourceOutput`的序列式自动传输系统登记机构。最小的序列器-文本申报使用正常源产出完成编辑类型检查的部分作者类型,包括执行产出被省略的项目消费者。主机选择是否将实施产出纳入其汇编;普通建筑包括完全产生的实施。背景形状和辅助合同诊断使用常规输出。要求生产者生成编码或复制元数据的诊断需要生产者制作的编码或复制元数据伴随执行产出。

XML文件包含已撰写的 API 评论。文档网站的 API 参考过滤器生成了执行类型和成员使用生成代码属性,保存了生成背景等部分类型的作者。

任选 [Orleans 合同兼容性分析器](grain-versioning/contract-compatibility-analyzer.md) 记录记录 grain 区域PC身份和签名 `OrleansContracts.txt` 以便合同的变动在滚动升级之前可以审查。

定义自定义 grain- 调用返回类型可以扩展生成的代理和可引用请求模式。 见 [自定义 Orleans 序列代码生成](code-generation-customization.md)。

序列规则和版本耐受度见 [Orleans 序号](../host/configuration-guide/serialization.md)。
