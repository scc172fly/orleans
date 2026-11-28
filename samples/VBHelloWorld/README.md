---
languages:
- vb
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Visual Basic 示例应用"
urlFragment: "orleans-vb-sample"
description: "使用 Visual Basic 编写的 Orleans 示例应用。"
---

<a id="visual-basic-sample-app"></a>

# Visual Basic 示例应用

本示例演示如何使用 Visual Basic 编写 Grain 代码。示例由三个项目组成：

* _HelloWorldInterfaces_：包含 Grain 接口 `IHelloGrain` 的 C# 项目。
* _Grains_：实现 `IHelloGrain` 的 Visual Basic 项目。
* _HelloWorld_：托管 Grain 的 C# 项目。

`Microsoft.Orleans.Sdk` 包不支持生成 Visual Basic 代码，但支持分析 Visual Basic 程序集并生成 C# 代码。因此，本示例指示代码生成器在为 `HelloWorld` 项目生成代码时分析 `Grains` 项目。为此，在 `HelloWorld` 的 `Program.cs` 文件中使用以下指令：

```csharp
[assembly: GenerateCodeForDeclaringAssembly(typeof(HelloGrain))]
[assembly: GenerateCodeForDeclaringAssembly(typeof(IHelloGrain))]
```

设置上述特性后，代码生成器会分析 Visual Basic 程序集，并将生成的 C# 代码输出到 `HelloWorld` 项目。

<a id="sample-prerequisites"></a>

## 示例先决条件

本示例使用 C# 和 Visual Basic 编写，目标框架为 .NET 10。需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

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

<a id="running-the-sample"></a>

## 运行示例

打开终端窗口，在命令提示符下执行以下命令以运行示例：

```dotnetcli
dotnet run --project HelloWorld
```
