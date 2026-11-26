---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Stocks sample app"
urlFragment: "orleans-stocks-sample-app"
description: "显示实时库存的 Orleans 样本应用软件。"
---

<a id="orleans-stocks-sample-app"></a>

# Orleans 储存样本应用软件

此应用程序使用 `HttpClient`从远程服务获取股票价格, 将其隐藏在 grain中, 在屏幕上显示 。

<a id="demonstrates"></a>

## 演示

* 如何使用 Orleans 内部 [`BackgroundService`](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services#backgroundservice-base-class).
* 如何在 grain中使用计时器
* 如何使用 .NET的 `HttpClient` 打外部服务电话,并在 grain内将结果缓存。

[`BackgroundService`](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services#backgroundservice-base-class) 定期要求相应 `StockGrain` 提供各种库存的存货价格。
每个 `StockGrain` 都用其库存立项符号来识别,例如字符串 `"MSFT"`。

样本要显示所有包括的库存,需要替换 `ApiKey` 常数内 `StockGrain.cs` 和 A 相 API 密钥卡获取自 <https://www.alphavantage.co/support/#api-key.>
样本可以运行而不替换此密钥, 但警告信息可打印, 并附有如何获取 API 密钥的指示 。

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
