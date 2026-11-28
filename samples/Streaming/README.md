---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans 流示例应用"
urlFragment: "orleans-streaming-samples"
description: "演示流处理能力的一组 Orleans 示例应用。"
---

<a id="orleans-streaming-samples"></a>

# Orleans 流示例

此文件夹包含两个演示 Orleans Streams 的示例。

第一个示例位于 `Simple` 文件夹中，演示使用 Azure Event Hub 和隐式消费者的发布/订阅模式。

第二个示例位于 `CustomDataAdapter` 文件夹中，演示如何配置自定义数据适配器，使 Orleans 能够消费并非源自 Orleans 的流消息。

<a id="sample-prerequisites"></a>

## 示例先决条件

本示例使用 C# 编写，目标框架为 .NET 10。需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

<a id="building-the-sample"></a>

## 构建示例

按照以下步骤下载并运行示例：

1. 下载并解压示例。
2. 在 Visual Studio（2022 或更高版本）中：
    1. 在菜单栏中选择 **文件** > **打开** > **项目/解决方案**。
    2. 导航到存放已解压示例代码的文件夹，并打开 C# 项目（.csproj）文件。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 在不调试的情况下运行项目。
3. 从命令行运行：
   1. 导航到存放已解压示例代码的文件夹。
   2. 在命令行输入 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。
