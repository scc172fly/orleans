---
languages:
- vb
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Visual Basic sample app"
urlFragment: "orleans-vb-sample"
description: "以视觉基本内容为字的 Orleans 样本应用程序示例。"
---

<a id="visual-basic-sample-app"></a>

# 基本视觉样本应用

本样本演示如何使用“视觉基本”来写入 grain 代码。 样本包括三个项目:

* _Hello WorldFaterfaces_:一个C#项目,包含 grain 接口 `IHelloGrain`。
* _Granins_:执行视觉基础项目 `IHelloGrain`。
* :一个C#项目,主办 grains。

下 `Microsoft.Orleans.Sdk` 软件包不支持排放视觉基本代码。 但是, 它支持分析视觉基本组件和 C# 代码。 因此, 这个样本工作通过指示代码生成器分析 `Grains` 项目在为 `HelloWorld` 工程。 `HelloWorld`'s `Program.cs` 文件 :

```csharp
[assembly: GenerateCodeForDeclaringAssembly(typeof(HelloGrain))]
[assembly: GenerateCodeForDeclaringAssembly(typeof(IHelloGrain))]
```

有了上述属性,代码生成器分析视觉基本组装,并将C#代码发布到 `HelloWorld` 项目中。

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C#和视觉基本和目标 .NET 10 写成。 它要求 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后填写。

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

<a id="running-the-sample"></a>

## 运行样本

通过打开终端窗口运行样本,并在命令提示处执行以下操作:

```dotnetcli
dotnet run --project HelloWorld
```
