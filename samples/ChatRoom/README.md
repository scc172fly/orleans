---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans 聊天室示例"
urlFragment: "orleans-chat-room-sample"
description: "Orleans 聊天室应用示例。"
---

<a id="orleans-chat-room-sample"></a>

# Orleans 聊天室示例

![聊天客户端截图](screenshot.png)

本示例使用 [Orleans 流](https://learn.microsoft.com/dotnet/orleans/streaming) 构建一个基础聊天应用。在此应用中，各客户端可以：

* 设置自己的名称
* 加入和离开频道
* 在该频道中发送和接收消息
* 列出频道成员
* 显示频道聊天历史

每个聊天频道都有对应的 `ChannelGrain`（以频道名称标识）和一个流（以该 Grain 生成的 `Guid` 标识）。客户端连接到频道的 `ChannelGrain`，然后订阅由 `IChannelGrain.Join` 调用返回的 `Guid` 所标识的流。

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

<a id="running-the-sample"></a>

## 运行示例

首先，在一个终端窗口中执行以下命令启动服务端：

```PowerShell
dotnet run --project .\ChatRoom.Service\
```

服务端启动后，打开一个或多个终端窗口，并在各窗口中执行以下命令：

```PowerShell
dotnet run --project .\ChatRoom.Client\
```

客户端使用 Spectre.Console 显示预渲染的标志、文本标题及命令说明。

<a id="regenerating-the-terminal-logo"></a>

## 重新生成终端标志

`ChatRoom.Client\logo.png` 是原始图稿。`logo.markup.gz` 包含宽度为 1 至 25 像素、经过 gzip 压缩的 UTF-8 Spectre.Console 标记，不同尺寸使用换页符（`\f`）分隔。这些尺寸使标志在较窄的终端中仍能保持外观。

修改 PNG 后，如需重新生成标志，请将以下内容保存为临时目录中的 `RenderLogo.cs`，并使用 .NET 10 SDK 运行：

```PowerShell
dotnet run <path-to-RenderLogo.cs> -- .\ChatRoom.Client\logo.png .\ChatRoom.Client\logo.markup.gz
```

这个一次性转换工具使用原始 ImageSharp 渲染器，并适用其许可证条款。客户端使用 Spectre.Console 和 .NET 内置的 gzip 解压功能。

```csharp
#:package Spectre.Console.ImageSharp@0.54.0
#:package SixLabors.ImageSharp@4.1.2

using System.IO.Compression;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

var image = new CanvasImage(args[0]) { MaxWidth = 25 };
var options = new RenderOptions(AnsiConsole.Profile.Capabilities, new Size(50, 25));
using var file = File.Create(args[1]);
using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
using var writer = new StreamWriter(gzip);

for (var width = 1; width <= 25; width++)
{
    if (width > 1)
    {
        writer.Write('\f');
    }

    var markup = new StringBuilder();
    string? previousStyle = null;
    foreach (var segment in ((IRenderable)image).Render(options, width * 2))
    {
        var style = segment.IsLineBreak ? null : segment.Style.ToMarkup().Replace("default on ", "on ");
        if (style != previousStyle)
        {
            if (!string.IsNullOrEmpty(previousStyle))
            {
                markup.Append("[/]");
            }

            if (!string.IsNullOrEmpty(style))
            {
                markup.Append('[').Append(style).Append(']');
            }

            previousStyle = style;
        }

        markup.Append(segment.IsLineBreak ? "\n" : Markup.Escape(segment.Text));
    }

    writer.Write(markup.ToString().TrimEnd('\n'));
}
```
