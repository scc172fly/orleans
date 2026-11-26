using Microsoft.Extensions.Hosting;

namespace Orleans.Docs.Snippets.Aspire.Client;

// 此类包含 Orleans 客户端配置的示例代码, 使用 Aspire
public static class ClientProgram
{
    // <client_basic_config>
    public static void BasicClientConfiguration(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        builder.AddKeyedRedisClient("orleans-redis");

        // 配置 Orleans 客户端 - 自动插入集成配置
        builder.UseOrleansClient();

        builder.Build().Run();
    }
    // </client_basic_config>
}
