using GPSTracker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Net;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
            .AddPrometheusExporter()
            .AddMeter("Microsoft.Orleans"))
    .WithTracing(tracing =>
    {
        // 设置服务名称
        tracing.SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService(serviceName: "GPSTracker", serviceVersion: "1.0"));

        tracing.AddSource("Microsoft.Orleans.Runtime");
        tracing.AddSource("Microsoft.Orleans.Application");

#pragma warning disable CS0618 // Preserve the sample's Zipkin endpoint until the exporter is removed.
        tracing.AddZipkinExporter(zipkin => zipkin.Endpoint = new Uri("http://localhost:9411/api/v2/spans"));
#pragma warning restore CS0618
    });

builder.Host.UseOrleans((ctx, siloBuilder) => {

    // 为了支持多个东道主组成一个集群,它们必须倾听不同港口的声音。
    // 使用 --InstanceId X 选项启动后续主机。
    int instanceId = ctx.Configuration.GetValue<int>("InstanceId");
    siloBuilder.UseLocalhostClustering(
        siloPort: 11111 + instanceId,
        gatewayPort: 30000 + instanceId,
        primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, 11111));

    siloBuilder.AddActivityPropagation();
});
builder.WebHost.UseKestrel((ctx, kestrelOptions) =>
{
    // 为了避免端口冲突,每个网络服务器都必须在不同的端口监听。
    int instanceId = ctx.Configuration.GetValue<int>("InstanceId");
    kestrelOptions.ListenLocalhost(5001 + instanceId);
});
builder.Services.AddHostedService<HubListUpdater>();
builder.Services.AddSignalR().AddJsonProtocol();

WebApplication app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapHub<LocationHub>("/locationHub");
app.MapPrometheusScrapingEndpoint();
app.Run();
