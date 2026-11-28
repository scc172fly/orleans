using DurableOrderProcessing;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OrderingOptions>(builder.Configuration.GetSection("Ordering"));
builder.Services.AddOrderProcessingApi();

// 订单工作流通过 HTTP 调用模拟支付网关，该网关由此应用在 /gateway 下提供。
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
