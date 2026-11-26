using DurableOrderProcessing;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OrderingOptions>(builder.Configuration.GetSection("Ordering"));
builder.Services.AddOrderProcessingApi();

// 订单工作流程称为模拟付款网关,该应用程序在/门道下使用,高于HTTP。
builder.UseOrleans(silo => silo
    .UseLocalhostClustering()
    .AddOrderProcessing(payments => payments.ConfigureHttpClient(client =>
        client.BaseAddress = new Uri(builder.Configuration["Payments:BaseAddress"]!)))
    .AddDashboard());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapOrderProcessing();

app.Run();
