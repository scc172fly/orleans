using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// 配置 Orleans。
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.UseInMemoryReminderService();
    siloBuilder.AddMemoryGrainStorageAsDefault();

    // 添加 Dashboard。
    siloBuilder.AddDashboard();
});

var app = builder.Build();

// 在根路径映射 Dashboard 端点。
app.MapOrleansDashboard();

app.Run();
