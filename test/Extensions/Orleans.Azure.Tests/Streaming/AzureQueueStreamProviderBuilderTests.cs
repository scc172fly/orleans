using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Xunit;

namespace Tester.AzureUtils;

/// <summary>
/// 测试 Azure Queue 流提供程序配置生成器，验证各种配置场景。
/// </summary>
[TestCategory("AzureStorage"), TestCategory("Streaming"), TestCategory("BVT")]
[TestSuite("BVT")]
[TestProvider("AzureStorage")]
[TestArea("Streaming")]
public class AzureQueueStreamProviderBuilderTests
{
    /// <summary>
    /// 验证缺少连接字符串时 QueueServiceClient 为 null。
    /// </summary>
    [Fact]
    public void Missing_ConnectionString()
    {
        string json = """
		{
			"Orleans": {
				"Streaming": {
					"AzureQueueProvider": {
						"ProviderType": "AzureQueueStorage",
						"QueueNames": [
							"q1"
						]
					}
				}
			}
		}
		""";

        var queueOptions = ConfigureSilo(json).Services.BuildServiceProvider().GetOptionsByName<AzureQueueOptions>(null!);

        Assert.Null(queueOptions.QueueServiceClient);
    }

    /// <summary>
    /// 验证仅提供最低限度的必需配置即可使用默认设置创建有效的 QueueServiceClient。
    /// </summary>
    [Fact]
    public void Minimal_Configuration()
    {
        string json = """
		{
			"Orleans": {
				"Streaming": {
					"AzureQueueProvider": {
						"ProviderType": "AzureQueueStorage",
						"ConnectionString": "UseDevelopmentStorage=true",
						"QueueNames": [
							"q1"
						]
					}
				}
			}
		}
		""";

        var queueOptions = ConfigureSilo(json).Services.BuildServiceProvider().GetOptionsByName<AzureQueueOptions>(null!);

        Assert.NotNull(queueOptions.QueueServiceClient);
        Assert.Equal("devstoreaccount1", queueOptions.QueueServiceClient.AccountName);
        Assert.Equal(["q1"], queueOptions.QueueNames);
        Assert.Null(queueOptions.MessageVisibilityTimeout);
    }

    /// <summary>
    /// 验证所有配置选项均被正确解析和应用。
    /// </summary>
    [Fact]
    public void Full_Configuration()
    {
        string json = """
		{
			"Orleans": {
				"Streaming": {
					"AzureQueueProvider": {
						"ProviderType": "AzureQueueStorage",
						"ConnectionString": "UseDevelopmentStorage=true",
						"MessageVisibilityTimeout": "00:00:37",
						"QueueNames": [
							"q1",
							"q2"
						]
					}
				}
			}
		}
		""";

        var queueOptions = ConfigureSilo(json).Services.BuildServiceProvider().GetOptionsByName<AzureQueueOptions>(null!);

        Assert.NotNull(queueOptions.QueueServiceClient);
        Assert.Equal("devstoreaccount1", queueOptions.QueueServiceClient.AccountName);
        Assert.Equal(["q1", "q2"], queueOptions.QueueNames);
        Assert.Equal(TimeSpan.FromSeconds(37), queueOptions.MessageVisibilityTimeout);
    }

    /// <summary>
    /// 验证无效的消息可见性超时会导致选项解析失败。
    /// </summary>
    [Fact]
    public void Invalid_MessageVisibilityTimeout_Throws()
    {
        string json = """
		{
			"Orleans": {
				"Streaming": {
					"AzureQueueProvider": {
						"ProviderType": "AzureQueueStorage",
						"MessageVisibilityTimeout": "not-a-timespan",
						"QueueNames": [
							"q1"
						]
					}
				}
			}
		}
		""";

        var services = ConfigureSilo(json).Services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => services.GetOptionsByName<AzureQueueOptions>(null!));
        Assert.Contains("at 'Orleans:Streaming:AzureQueueProvider:MessageVisibilityTimeout'", exception.Message);
        Assert.Contains($"type '{typeof(TimeSpan)}'", exception.Message);
    }

    static TestSiloBuilder ConfigureSilo(string json)
    {
        var siloBuilder = new TestSiloBuilder(json);
        var aqsBuilder = new AzureQueueStreamProviderBuilder();
        aqsBuilder.Configure(siloBuilder, null, siloBuilder.Configuration.GetSection("Orleans:Streaming:AzureQueueProvider"));
        return siloBuilder;
    }

    class TestSiloBuilder(string json) : ISiloBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();

        public IConfiguration Configuration { get; } = GetConfig(json);
    }

    static IConfigurationRoot GetConfig(string json) => new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
}
