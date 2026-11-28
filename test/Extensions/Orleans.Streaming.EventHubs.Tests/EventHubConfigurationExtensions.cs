using Azure.Identity;
using Orleans.Configuration;
using Tester.AzureUtils;
using TestExtensions;

namespace ServiceBus.Tests
{
    public static class EventHubConfigurationExtensions
    {
        public static EventHubOptions ConfigureTestDefaults(this EventHubOptions options, string eventHubName, string consumerGroup)
        {
            if (TestDefaultConfiguration.UseAadAuthentication)
            {
                options.ConfigureEventHubConnection(TestDefaultConfiguration.EventHubFullyQualifiedNamespace!, eventHubName, consumerGroup, TestDefaultConfiguration.TokenCredential); // AAD 测试配置提供 Event Hubs 命名空间。
            }
            else
            {
                options.ConfigureEventHubConnection(TestDefaultConfiguration.EventHubConnectionString!, eventHubName, consumerGroup); // 连接字符串测试配置提供此值。
            }

            return options;
        }

        public static AzureTableStreamCheckpointerOptions ConfigureTestDefaults(this AzureTableStreamCheckpointerOptions options)
        {
            options.TableServiceClient = AzureStorageOperationOptionsExtensions.GetTableServiceClient();
            return options;
        }
    }
}
