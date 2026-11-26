using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Xunit;

namespace Tester.AzureUtils;

/// <summary>
/// Azure 队列供应方配置构建器测试,验证各种配置情景。
/// </summary>
[TestCategory("AzureStorage"), TestCategory("Streaming"), TestCategory("BVT")]
[TestSuite("BVT")]
[TestProvider("AzureStorage")]
[TestArea("Streaming")]
public class AzureQueueStreamProviderBuilderTests
{
    /// <summary>
    /// 证明缺失的连接字符串结果为无效 QueueServiceClient。
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
    /// 证明最起码所需的配置在默认设置下创建了有效的 QueueServiceClient 。
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
    /// 证明所有配置选项都经过适当分析和应用。
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
    /// 证明无效的信息可见度超时失败了选项解决方案。
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
