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
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// AWS SDK的一些基本公用事业方法
    /// </summary>
    internal static class AWSUtils
    {
        internal static RegionEndpoint GetRegionEndpoint(string zone = "")
        {
            //
            // 将订单从 RegionEndpoint 中保留,这样就更容易维持。
            // 默认值为us- 西West-2
            //

            return RegionEndpoint.GetBySystemName(zone) ?? RegionEndpoint.USWest2;
        }

        /// <summary>
        /// 校验 DynamoDB PartitionKey。
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
        /// 校验 DynamoDB RowKey。
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
