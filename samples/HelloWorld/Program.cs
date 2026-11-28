using HelloWorld;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 配置宿主。
using var host = new HostBuilder()
    .UseOrleans(builder => builder.UseLocalhostClustering())
    .Build();

// 启动宿主。
await host.StartAsync();

// 获取 Grain 工厂。
var grainFactory = host.Services.GetRequiredService<IGrainFactory>();

// 获取键为 "friend" 的 HelloGrain 引用。
var friend = grainFactory.GetGrain<IHelloGrain>("friend");

// 调用 Grain 并将结果输出到控制台。
var result = await friend.SayHello("Good morning!");
Console.WriteLine($"""

    {result}

    """);

Console.WriteLine("Orleans is running.\nPress Enter to terminate...");
Console.ReadLine();
Console.WriteLine("Orleans is stopping...");

await host.StopAsync();
