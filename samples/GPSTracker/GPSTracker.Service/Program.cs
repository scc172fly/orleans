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
        // 设置服务名称。
        tracing.SetResourceBuilder(
            ResourceBuilder.CreateDefault()
                .AddService(serviceName: "GPSTracker", serviceVersion: "1.0"));

        tracing.AddSource("Microsoft.Orleans.Runtime");
        tracing.AddSource("Microsoft.Orleans.Application");

#pragma warning disable CS0618 // 在移除导出器之前保留示例的 Zipkin 端点。
        tracing.AddZipkinExporter(zipkin => zipkin.Endpoint = new Uri("http://localhost:9411/api/v2/spans"));
#pragma warning restore CS0618
    });

builder.Host.UseOrleans((ctx, siloBuilder) => {

    // 为了支持多个宿主组成集群，它们必须侦听不同的端口。
    // 使用 --InstanceId X 选项启动后续宿主。
    int instanceId = ctx.Configuration.GetValue<int>("InstanceId");
    siloBuilder.UseLocalhostClustering(
        siloPort: 11111 + instanceId,
        gatewayPort: 30000 + instanceId,
        primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, 11111));

    siloBuilder.AddActivityPropagation();
});
builder.WebHost.UseKestrel((ctx, kestrelOptions) =>
{
    // 为避免端口冲突，每个 Web 服务器必须监听不同的端口。
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
