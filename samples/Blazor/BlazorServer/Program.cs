using BlazorServer.Components;
using BlazorServer.Services;
using Orleans.Providers;

var builder = WebApplication.CreateBuilder(args);

// 向容器添加服务。
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Host.UseOrleans((ctx, siloBuilder) =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.AddMemoryGrainStorageAsDefault();
    siloBuilder.AddMemoryStreams<DefaultMemoryMessageBodySerializer>("MemoryStreams");
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
});
builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddSingleton<TodoService>();

var app = builder.Build();

// 配置 HTTP 请求管道。
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // 默认 HSTS 值为 30 天。生产环境可能需要调整此值，请参阅 https://aka.ms/aspnetcore-hsts。
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
