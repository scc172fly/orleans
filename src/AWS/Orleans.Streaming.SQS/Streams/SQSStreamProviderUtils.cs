using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Streams;
using OrleansAWSUtils.Storage;

namespace OrleansAWSUtils.Streams
{
    /// <summary>
    /// SQS 实用函数。
    /// </summary>
    public class SQSStreamProviderUtils
    {
        /// <summary>
        /// 异步删除指定提供程序与 clusterId 使用的所有队列。
        /// </summary>
        /// <returns>表示此异步方法的 Task 对象。</returns>
        public static Task DeleteAllUsedQueues(string providerName, string clusterId, string storageConnectionString, ILoggerFactory loggerFactory)
            => DeleteAllUsedQueues(providerName, clusterId, storageConnectionString, loggerFactory, fifoQueue: false);

        /// <summary>
        /// 删除提供程序及集群使用的全部队列。
        /// </summary>
        public static async Task DeleteAllUsedQueues(string providerName, string clusterId, string storageConnectionString, ILoggerFactory loggerFactory, bool fifoQueue)
        {
            if (clusterId != null)
            {
                var queueMapper = new HashRingBasedStreamQueueMapper(new HashRingStreamQueueMapperOptions(), providerName);
                List<QueueId> allQueues = queueMapper.GetAllQueues().ToList();

                var sqsOptions = new SqsOptions
                {
                    ConnectionString = storageConnectionString,
                    FifoQueue = fifoQueue,
                };

                var deleteTasks = new List<Task>();
                foreach (var queueId in allQueues)
                {
                    var manager = new SQSStorage(loggerFactory, queueId.ToString(), sqsOptions, clusterId);
                    await manager.InitQueueAsync();
                    deleteTasks.Add(manager.DeleteQueue());
                }

                await Task.WhenAll(deleteTasks);
            }
        }
    }
}
