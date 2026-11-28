using Amazon;
using System;

#if CLUSTERING_DYNAMODB
namespace Orleans.Clustering.DynamoDB
#elif PERSISTENCE_DYNAMODB
namespace Orleans.Persistence.DynamoDB
#elif REMINDERS_DYNAMODB
namespace Orleans.Reminders.DynamoDB
#elif STREAMING_SQS
namespace Orleans.Streaming.SQS
#elif STREAMING_KINESIS
namespace Orleans.Streaming.Kinesis
#elif AWSUTILS_TESTS
namespace Orleans.AWSUtils.Tests
#elif TRANSACTIONS_DYNAMODB
namespace Orleans.Transactions.DynamoDB
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// AWS SDK 的一些基础实用方法。
    /// </summary>
    internal static class AWSUtils
    {
        internal static RegionEndpoint GetRegionEndpoint(string zone = "")
        {
            //
            // 保留 RegionEndpoint 中的顺序，以便维护。
            // 默认区域为 us-west-2。
            //

            return RegionEndpoint.GetBySystemName(zone) ?? RegionEndpoint.USWest2;
        }

        /// <summary>
        /// 验证 DynamoDB PartitionKey。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string ValidateDynamoDBPartitionKey(string key)
        {
            if (key.Length >= 2048)
                throw new ArgumentException(string.Format("Key length {0} is too long to be an DynamoDB partition key. Key={1}", key.Length, key));

            return key;
        }

        /// <summary>
        /// 验证 DynamoDB RowKey。
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string ValidateDynamoDBRowKey(string key)
        {
            if (key.Length >= 1024)
                throw new ArgumentException(string.Format("Key length {0} is too long to be an DynamoDB row key. Key={1}", key.Length, key));

            return key;
        }
    }
}
