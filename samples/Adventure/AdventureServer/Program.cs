using System.Reflection;
using AdventureSetup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
var mapFileName = Path.Combine(path, "AdventureMap.json");

switch (args.Length)
{
    default:
        Console.WriteLine("*** Invalid command line arguments.");
        return -1;
    case 0:
        break;
    case 1:
        mapFileName = args[0];
        break;
}

if (!File.Exists(mapFileName))
{
    Console.WriteLine("*** File not found: {0}", mapFileName);
    return -2;
}

// 配置宿主。
using var host = Host.CreateDefaultBuilder(args)
    .UseOrleans(siloBuilder => siloBuilder.UseLocalhostClustering())
    .Build();

// 启动宿主。
await host.StartAsync();

Console.WriteLine("Map file name is '{0}'.", mapFileName);
Console.WriteLine("Setting up Adventure, please wait ...");

// 初始化游戏世界。
var client = host.Services.GetRequiredService<IGrainFactory>();
var adventure = new AdventureGame(client);
await adventure.Configure(mapFileName);

Console.WriteLine("Setup completed.");
Console.WriteLine("Now you can launch the client.");

// 按任意键退出。
Console.WriteLine("Press any key to exit.");
Console.ReadKey();
await host.StopAsync();

return 0;
