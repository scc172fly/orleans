using System.Net;

await Host.CreateDefaultBuilder(args)
    .UseOrleans((ctx, siloBuilder) =>
    {
        // 为了支持多个东道主组成一个集群,它们必须倾听不同港口的声音。
        // 使用 --InstanceId X 选项启动后续主机。
        var instanceId = ctx.Configuration.GetValue<int>("InstanceId");
        var port = 11_111;
        siloBuilder.UseLocalhostClustering(
            siloPort: port + instanceId,
            gatewayPort: 30000 + instanceId,
            primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, port));
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseStartup<Startup>();
        webBuilder.ConfigureKestrel((ctx, kestrelOptions) =>
        {
            // 为了避免端口冲突,每个网络服务器都必须在不同的端口监听。
            var instanceId = ctx.Configuration.GetValue<int>("InstanceId");
            kestrelOptions.ListenLocalhost(5000 + instanceId, listenOptions => listenOptions.UseHttps());
        });
    })
    .RunConsoleAsync();
