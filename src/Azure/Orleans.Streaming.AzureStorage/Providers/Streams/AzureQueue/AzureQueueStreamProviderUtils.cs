using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.AzureUtils;
using Orleans.Configuration;
using Orleans.Streams;

namespace Orleans.Providers.Streams.AzureQueue
{
    /// <summary>
    /// Azure Queue 持久化流提供程序的实用函数。
    /// </summary>
    public class AzureQueueStreamProviderUtils
    {
        /// <summary>
        /// 生成默认 Azure 队列名称。
        /// </summary>
        /// <param name="serviceId"></param>
        /// <param name="providerName"></param>
        /// <returns></returns>
        public static List<string> GenerateDefaultAzureQueueNames(string serviceId, string providerName)
        {
            var defaultQueueMapper = new HashRingBasedStreamQueueMapper(new HashRingStreamQueueMapperOptions(), providerName);
            return defaultQueueMapper.GetAllQueues()
                .Select(queueName => $"{serviceId}-{queueName}").ToList();
        }

        /// <summary>
        /// 测试辅助方法。删除指定流提供程序使用的所有队列。
        /// </summary>
        /// <param name="loggerFactory">要使用的日志记录器工厂。</param>
        /// <param name="azureQueueNames">要删除的 Azure 队列名称。</param>
        /// <param name="storageConnectionString">Azure Storage 连接字符串。</param>
        public static async Task DeleteAllUsedAzureQueues(ILoggerFactory loggerFactory, List<string> azureQueueNames, string storageConnectionString)
        {
            ArgumentNullException.ThrowIfNull(azureQueueNames);

            var options = new AzureQueueOptions();
            options.QueueServiceClient = new(storageConnectionString);
            await DeleteAllUsedAzureQueues(loggerFactory, azureQueueNames, options);
        }

        /// <summary>
        /// 测试辅助方法。删除指定流提供程序使用的所有队列。
        /// </summary>
        /// <param name="loggerFactory">要使用的日志记录器工厂。</param>
        /// <param name="azureQueueNames">要删除的 Azure 队列名称。</param>
        /// <param name="queueOptions">Azure Storage 选项。</param>
        public static async Task DeleteAllUsedAzureQueues(ILoggerFactory loggerFactory, List<string> azureQueueNames, AzureQueueOptions queueOptions)
        {
            ArgumentNullException.ThrowIfNull(azureQueueNames);

            var deleteTasks = new List<Task>();
            foreach (var queueName in azureQueueNames)
            {
                var manager = new AzureQueueDataManager(loggerFactory, queueName, queueOptions);
                deleteTasks.Add(manager.DeleteQueue());
            }

            await Task.WhenAll(deleteTasks);
        }

        /// <summary>
        /// 测试辅助方法。清空指定流提供程序使用的所有队列中的全部消息。
        /// </summary>
        /// <param name="loggerFactory">要使用的日志记录器工厂。</param>
        /// <param name="azureQueueNames">承载流提供程序的部署 ID。</param>
        /// <param name="storageConnectionString">Azure Storage 连接字符串。</param>
        public static async Task ClearAllUsedAzureQueues(ILoggerFactory loggerFactory, List<string> azureQueueNames, string storageConnectionString)
        {
            ArgumentNullException.ThrowIfNull(azureQueueNames);

            var options = new AzureQueueOptions();
            options.QueueServiceClient = new(storageConnectionString);
            await ClearAllUsedAzureQueues(loggerFactory, azureQueueNames, options);
        }

        /// <summary>
        /// 测试辅助方法。清空指定流提供程序使用的所有队列中的全部消息。
        /// </summary>
        /// <param name="loggerFactory">要使用的日志记录器工厂。</param>
        /// <param name="azureQueueNames">承载流提供程序的部署 ID。</param>
        /// <param name="queueOptions">Azure Storage 选项。</param>
        public static async Task ClearAllUsedAzureQueues(ILoggerFactory loggerFactory, List<string> azureQueueNames, AzureQueueOptions queueOptions)
        {
            ArgumentNullException.ThrowIfNull(azureQueueNames);

            var deleteTasks = new List<Task>();
            foreach (var queueName in azureQueueNames)
            {
                var manager = new AzureQueueDataManager(loggerFactory, queueName, queueOptions);
                deleteTasks.Add(manager.ClearQueue());
            }

            await Task.WhenAll(deleteTasks);
        }
    }
}
