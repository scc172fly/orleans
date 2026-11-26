using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置一个 亚马逊 SQS 支持的持久性流提供者。
    /// </summary>
    public class SqsOptions
    {
        /// <summary>
        /// 指定连接到 SQS 的连接字符串。
        /// </summary>
        /// <example>
        /// AWS:服务=e-west-1;AccessKey=XXXXXX;SecretKey=XXXXXX;SessionToken=XXXXXX;
        /// </example>
        /// <example>
        /// LocalStack示例:服务=http://localhost:4566
        /// </example>
        [Redact]
        public string ConnectionString { get; set; } = null!;

        /// <summary>
        /// 指定每个信件中都获取到亚马逊 SQS 系统属性。
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists",
            Justification = "Retains the shipped List<string> option property type for source and binary compatibility.")]
        public List<string> ReceiveMessageSystemAttributes { get; set; } = [];

        /// <summary>
        /// 指定每个信件中要检索到哪些应用程序定义的信件属性。
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists",
            Justification = "Retains the shipped List<string> option property type for source and binary compatibility.")]
        public List<string> ReceiveMessageAttributes { get; set; } = [];

        /// <summary>
        /// 新的 SQS 信息可选择长号持续时间。
        /// </summary>
        public int? ReceiveWaitTimeSeconds { get; set; }

        /// <summary>
        /// 当亚马逊 SQS 返回信息时, 可见度超时便开始。
        /// 在此期间,消费者处理并删除信息。
        /// 但是,如果消费者在删除电文之前失败了,而您的系统在可见度超时到期前不为该电文拨号 DeleteMessage 动作,
        /// 信息为其他消费者所见,再次收到。
        /// 如果只收到一次信息,您的消费者应在可见度超时期间将其删除。
        /// </summary>
        public int? VisibilityTimeoutSeconds { get; set; }

        /// <summary>
        /// 配置提供方使用 Amazon SQS FIFO 队列并保存每条流的顺序。
        /// </summary>
        public bool FifoQueue { get; set; }
    }
}
