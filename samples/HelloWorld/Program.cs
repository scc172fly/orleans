using HelloWorld;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 配置主机
using var host = new HostBuilder()
    .UseOrleans(builder => builder.UseLocalhostClustering())
    .Build();

// 启动主机
await host.StartAsync();

// 获取 grain 工厂
var grainFactory = host.Services.GetRequiredService<IGrainFactory>();

// 获取密钥“ Friend” 的 HelloGrain grain 引用
var friend = grainFactory.GetGrain<IHelloGrain>("friend");

// 调用 grain 并将结果打印到控制台
var result = await friend.SayHello("Good morning!");
Console.WriteLine($"""

    {result}

    """);

Console.WriteLine("Orleans is running.\nPress Enter to terminate...");
Console.ReadLine();
Console.WriteLine("Orleans is stopping...");

await host.StopAsync();
