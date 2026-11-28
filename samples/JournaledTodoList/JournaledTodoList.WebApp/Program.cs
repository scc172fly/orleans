using JournaledTodoList.WebApp.Components;
using JournaledTodoList.WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedAzureTableServiceClient("clustering");
builder.AddKeyedAzureBlobServiceClient("grain-state");
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddLogStorageBasedLogConsistencyProviderAsDefault();
    siloBuilder.AddStateStorageBasedLogConsistencyProvider(name: Constants.StateStorageProviderName);
});
builder.Services.AddScoped<TodoListService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.MapDefaultEndpoints();

// 配置 HTTP 请求管道。
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // 默认 HSTS 值为 30 天。生产环境可能需要调整此值，请参阅 https://aka.ms/aspnetcore-hsts。
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
