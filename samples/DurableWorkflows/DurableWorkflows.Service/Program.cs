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
    // UseAzureBlobDurableJobs 登记默认 Azure 布球日记账提供者(AddAzureBlobJournalStorage),该服务
    // grain 以及《持久工作》杂志的发行量, 所以每份复制都恢复了同样的任务状态和延迟。
    // BlobServiceClient 来自于下面的 AddOptions 调用。 Orleans 二进制日记格式存储样本
    // GenerateSerializer国家类型。
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
