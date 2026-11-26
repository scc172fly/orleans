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
    /// 静列队列的 PLUTY 功能。
    /// </summary>
    public class AzureQueueStreamProviderUtils
    {
        /// <summary>
        /// 生成默认的“ zure ” 队列名称
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
        /// 测试助手方法。 删除指定流提供者使用的所有队列 。
        /// </summary>
        /// <param name="loggerFactory">用于使用</param>的对帐厂
        /// <param name="azureQueueNames">将删除顺序队列名称。</param>
        /// <param name="storageConnectionString">结结存连接字符串。</param>
        public static async Task DeleteAllUsedAzureQueues(ILoggerFactory loggerFactory, List<string> azureQueueNames, string storageConnectionString)
        {
            ArgumentNullException.ThrowIfNull(azureQueueNames);

            var options = new AzureQueueOptions();
            options.QueueServiceClient = new(storageConnectionString);
            await DeleteAllUsedAzureQueues(loggerFactory, azureQueueNames, options);
        }

        /// <summary>
        /// 测试助手方法。 删除指定流提供者使用的所有队列 。
        /// </summary>
        /// <param name="loggerFactory">用于使用</param>的对帐厂
        /// <param name="azureQueueNames">将删除顺序队列名称。</param>
        /// <param name="queueOptions">顺流储存选项。</param>
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
        /// 辅助测试方法。 清除指定流提供者使用的所有队列中的全部信息 。
        /// </summary>
        /// <param name="loggerFactory">用于使用</param>的对帐厂
        /// <param name="azureQueueNames">流提供者所在的部署代号。</param>
        /// <param name="storageConnectionString">结结存连接字符串。</param>
        public static async Task ClearAllUsedAzureQueues(ILoggerFactory loggerFactory, List<string> azureQueueNames, string storageConnectionString)
        {
            ArgumentNullException.ThrowIfNull(azureQueueNames);

            var options = new AzureQueueOptions();
            options.QueueServiceClient = new(storageConnectionString);
            await ClearAllUsedAzureQueues(loggerFactory, azureQueueNames, options);
        }

        /// <summary>
        /// 辅助测试方法。 清除指定流提供者使用的所有队列中的全部信息 。
        /// </summary>
        /// <param name="loggerFactory">用于使用</param>的对帐厂
        /// <param name="azureQueueNames">流提供者所在的部署代号。</param>
        /// <param name="queueOptions">顺流储存选项。</param>
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
