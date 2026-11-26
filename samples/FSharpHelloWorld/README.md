---
languages:
- fsharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans F# sample app"
urlFragment: "orleans-fsharp-sample"
description: "以 F# 写 Orleans 样本应用程序的例子。"
---

<a id="f-sample-app"></a>

# F# 样本应用

本样本演示如何使用 F# 来写入 grain 代码。 样本包括三个项目:

* _Hello WorldFaterfaces_:一个C#项目,包含 grain 接口 `IHelloGrain`。
* _Granins_:一个F#项目,实施 `IHelloGrain`。
* :一个C#项目,主办 grains。

下 `Microsoft.Orleans.Sdk` 软件包不支持排放F#代码。 但是, 它支持分析 F# 组件和 C# 代码。 因此, 此样本工作通过指示代码生成器分析 `Grains` 项目在为 `HelloWorld` 工程。 `HelloWorld`'s `Program.cs` 文件 :

```csharp
[assembly: GenerateCodeForDeclaringAssembly(typeof(Grains.HelloGrain))]
```

有了上述属性,代码生成器分析F#组装并发布C#代码到 `HelloWorld` 项目。

<a id="sample-prerequisites"></a>

## 抽样的前提条件

此样本以 C# 和 F # 及目标写成 .NET 10. 需要: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后。

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
