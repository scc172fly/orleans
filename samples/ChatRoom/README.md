---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Chat Room sample"
urlFragment: "orleans-chat-room-sample"
description: "A 音 A Orleans 聊天室试样应用软件。"
---

<a id="orleans-chat-room-sample"></a>

# Orleans 聊天室样品

![聊天客户端的截图](screenshot.png)

此样本使用 [Orleans 流](https://learn.microsoft.com/dotnet/orleans/streaming) 构建基本的聊天应用程序。 在此应用程序中, 每个客户端可以:

* 设置名称
* 加入并离开一个频道
* 在该频道发送和接收信件
* 列出频道成员
* 显示频道聊天历史

每个聊天频道都有相应的 `ChannelGrain` 由频道名称和流经 `Guid` 由该 grain客户连接到 `ChannelGrain` 频道,然后订阅 `Guid` 从 `IChannelGrain.Join` 电话

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. 打开解压后的示例目录，再打开其中的 C# 项目文件（`.csproj`）。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 直接运行。
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 在命令行,类型 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

<a id="running-the-sample"></a>

## 运行样本

首先,在一个终端窗口中启动服务器,执行以下操作:

```PowerShell
dotnet run --project .\ChatRoom.Service\
```

然后,服务器一旦启动,打开一个或多个终端窗口并在每个窗口中执行以下内容:

```PowerShell
dotnet run --project .\ChatRoom.Client\
```

客户使用 Spectre.Console显示预先提供的标识、文本标题和指令。

<a id="regenerating-the-terminal-logo"></a>

## 重新制作终端标志

`ChatRoom.Client\logo.png` 是源艺术。 `logo.markup.gz` 包含宽度为1-25 像素的gzip- 压缩 UTF-8 Spectre.Console 标记, 以窗体种子(`\f`) 分隔。 这些大小将保护徽标在狭窄终端中的外观 。

在改变巴布亚新几内亚之后再生,在临时目录中将以下内容作为 `RenderLogo.cs` 保留下来,然后与 .NET 10 SDK一起运行:

```PowerShell
dotnet run <path-to-RenderLogo.cs> -- .\ChatRoom.Client\logo.png .\ChatRoom.Client\logo.markup.gz
```

该一次性转换器使用原始 ImageSharp 制成器,其许可证条款适用。客户使用 Spectre.Console 和内置 .NET gzip decompression。

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
