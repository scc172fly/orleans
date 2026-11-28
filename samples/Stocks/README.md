---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Stocks 示例应用"
urlFragment: "orleans-stocks-sample-app"
description: "演示实时股票行情的 Orleans 示例应用。"
---

<a id="orleans-stocks-sample-app"></a>

# Orleans Stocks 示例应用

此应用通过 `HttpClient` 从远程服务获取股票价格，将其缓存在 Grain 中并显示在屏幕上。

<a id="demonstrates"></a>

## 演示内容

* 如何在 [`BackgroundService`](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services#backgroundservice-base-class) 中使用 Orleans。
* 如何在 Grain 中使用定时器
* 如何使用 .NET 的 `HttpClient` 调用外部服务并将结果缓存在 Grain 中。

一个 [`BackgroundService`](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services#backgroundservice-base-class) 定期向对应的 `StockGrain` 实例请求多种股票的价格。
每个 `StockGrain` 以股票代码标识，例如字符串 `"MSFT"`。

要让示例显示包含的所有股票，需要将 `StockGrain.cs` 中的 `ApiKey` 常量替换为从 <https://www.alphavantage.co/support/#api-key.> 获取的 API 密钥。
无需替换密钥也能运行示例，但可能会输出警告消息，说明如何获取 API 密钥。

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
