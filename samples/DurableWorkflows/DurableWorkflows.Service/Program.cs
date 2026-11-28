using Azure.Storage.Blobs;
using DurableWorkflows;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Orleans.Configuration;
using Orleans.Durable.Journaling;
using Orleans.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedRedisClient("clustering");
builder.AddAzureBlobServiceClient("durable-state");
builder.Services.AddOptions<ExternalEffectOptions>().Bind(builder.Configuration.GetSection("ExternalEffects"));
builder.Services.AddExternalEffectRecipes();
builder.Services.AddHealthChecks()
    .AddCheck("self", static () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"]);

builder.UseOrleans(siloBuilder =>
{
    // UseAzureBlobDurableJobs 注册默认的 Azure Blob 日志提供程序（AddAzureBlobJournalStorage），
    // Grain 日志和 Durable Jobs 共享该提供程序，因此每个副本都能恢复相同的任务状态及延迟操作。其
    // BlobServiceClient 由下面的 AddOptions 调用提供。Orleans 二进制日志格式用于存储示例中
    // 带有 [GenerateSerializer] 特性的状态类型。
    siloBuilder
        .AddJournaling()
        .UseAzureBlobDurableJobs(options => options.ContainerName = "durable-workflows")
        .Configure<JournaledStateManagerOptions>(options => options.JournalFormatKey = "orleans-binary")
        .AddDurableTasks(options => options.ResultRetentionPeriod = TimeSpan.FromHours(24));
});

builder.Services.AddOptions<AzureBlobJournalStorageOptions>()
    .Configure<BlobServiceClient>((options, blobServiceClient) => options.BlobServiceClient = blobServiceClient);

var app = builder.Build();

app.MapHealthChecks("/health");
app.MapHealthChecks("/alive", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});

app.MapDurableWorkflowEndpoints();
app.MapExternalEffectEndpoints();

await app.RunAsync();
