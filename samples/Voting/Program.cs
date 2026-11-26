using Orleans.Dashboard;
using Voting.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseOrleans((ctx, orleansBuilder) => orleansBuilder
        .UseLocalhostClustering()
        .AddMemoryGrainStorage("votes")
        .AddDashboard());

// 增加集装箱服务。
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<PollService>();
builder.Services.AddScoped<DemoService>();

var app = builder.Build();

// 配置 HTTP 请求管道 。
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // 默认 HSTS 值为 30 天。 您可能想要更改此选项用于生产方案, 见 https://aka.ms/aspnetcore-hsts。
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapRazorPages();
app.MapOrleansDashboard("/dashboard");
app.MapFallbackToPage("/_Host");
app.Run();
