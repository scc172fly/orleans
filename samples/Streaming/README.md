---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Streaming sample apps"
urlFragment: "orleans-streaming-samples"
description: "一组 Orleans 样本应用软件,展示流能力。"
---

<a id="orleans-streaming-samples"></a>

# Orleans 流式样品

该文件夹包含两个样本, 演示 Orleans 流 。

第一个样本,在 `Simple` 文件夹中,使用 Azure 事件枢纽和一个隐含消费者来显示阴道/子集。

第二个样本,在 `CustomDataAdapter` 文件夹中,展示了如何配置自定义数据适配器,以便 Orleans 能够消耗并非来自 Orleans的流信息。

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. Navigate to the folder that holds the unzipped sample code, and open the C# project (.csproj) file.
    3. Choose the <kbd>F5</kbd> key to run with debugging, or <kbd>Ctrl</kbd>+<kbd>F5</kbd> keys to run the project without debugging.
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 在命令行,类型 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。
