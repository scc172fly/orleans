using System.Net;

await Host.CreateDefaultBuilder(args)
    .UseOrleans((ctx, siloBuilder) =>
    {
        // 为了支持多个宿主组成集群，它们必须侦听不同的端口。
        // 使用 --InstanceId X 选项启动后续宿主。
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
            // 为避免端口冲突，每个 Web 服务器必须监听不同的端口。
            var instanceId = ctx.Configuration.GetValue<int>("InstanceId");
            kestrelOptions.ListenLocalhost(5000 + instanceId, listenOptions => listenOptions.UseHttps());
        });
    })
    .RunConsoleAsync();
