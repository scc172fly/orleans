using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置由 Amazon SQS 支持的持久化流提供程序。
    /// </summary>
    public class SqsOptions
    {
        /// <summary>
        /// 指定用于连接 SQS 的连接字符串。
        /// </summary>
        /// <example>
        /// AWS 示例：Service=eu-west-1;AccessKey=XXXXXX;SecretKey=XXXXXX;SessionToken=XXXXXX;
        /// </example>
        /// <example>
        /// LocalStack 示例：Service=http://localhost:4566
        /// </example>
        [Redact]
        public string ConnectionString { get; set; } = null!;

        /// <summary>
        /// 指定随每条消息获取的 Amazon SQS 系统属性。
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists",
            Justification = "Retains the shipped List<string> option property type for source and binary compatibility.")]
        public List<string> ReceiveMessageSystemAttributes { get; set; } = [];

        /// <summary>
        /// 指定随每条消息获取的应用程序自定义消息属性。
        /// </summary>
        [SuppressMessage("Design", "CA1002:Do not expose generic lists",
            Justification = "Retains the shipped List<string> option property type for source and binary compatibility.")]
        public List<string> ReceiveMessageAttributes { get; set; } = [];

        /// <summary>
        /// 长轮询等待新 SQS 消息的可选时长。
        /// </summary>
        public int? ReceiveWaitTimeSeconds { get; set; }

        /// <summary>
        /// 当 Amazon SQS 返回消息时，可见性超时开始计时。
        /// 在此期间，消费者处理并删除消息。
        /// 但如果消费者在删除消息前失败，且系统未在可见性超时结束前对该消息调用 DeleteMessage 操作，
        /// 该消息将重新对其他消费者可见，并再次被接收。
        /// 如果要求消息仅被接收一次，消费者应在可见性超时结束前删除该消息。
        /// </summary>
        public int? VisibilityTimeoutSeconds { get; set; }

        /// <summary>
        /// 配置提供程序使用 Amazon SQS FIFO 队列，并保留各流内的顺序。
        /// </summary>
        public bool FifoQueue { get; set; }
    }
}
