using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// 配置 Orleans
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering();
    siloBuilder.UseInMemoryReminderService();
    siloBuilder.AddMemoryGrainStorageAsDefault();

    // 添加仪表板
    siloBuilder.AddDashboard();
});

var app = builder.Build();

// 根部的地图仪表板端点
app.MapOrleansDashboard();

app.Run();
