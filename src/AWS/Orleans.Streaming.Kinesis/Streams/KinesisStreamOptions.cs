using System;
using Orleans.Runtime;

namespace Orleans.Streaming.Kinesis
{
    /// <summary>
    /// 配置 Kinesis 持久性流提供者 。
    /// </summary>
    public class KinesisStreamOptions
    {
        /// <summary>
        /// AWS Kinesis 的连接字符串。 格式 : “ service;AccessKey;SecretKey; region” 或“ services” , 用于默认的证书 。
        /// </summary>
        [Redact]
        public string? ConnectionString
        {
            get
            {
                if (!string.IsNullOrEmpty(Service) && !string.IsNullOrEmpty(AccessKey) && !string.IsNullOrEmpty(SecretKey))
                {
                    return $"{Service};{AccessKey};{SecretKey};{Region ?? "us-east-1"}";
                }
                return Service;
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    Service = null;
                    AccessKey = null;
                    SecretKey = null;
                    Region = null;
                    return;
                }

                var parts = value.Split(';');
                if (parts.Length == 1)
                {
                    Service = parts[0];
                }
                else if (parts.Length >= 4)
                {
                    Service = parts[0];
                    AccessKey = parts[1];
                    SecretKey = parts[2];
                    Region = parts[3];
                }
                else
                {
                    throw new ArgumentException("Invalid connection string format. Expected 'Service' or 'Service;AccessKey;SecretKey;Region'.", nameof(value));
                }
            }
        }

        /// <summary>
        /// Kinesis 的可选访问密钥字符串 。
        /// </summary>
        [Redact]
        public string? AccessKey { get; set; }

        /// <summary>
        /// Kinesis 的可选秘密密钥 。
        /// </summary>
        [Redact]
        public string? SecretKey { get; set; }

        /// <summary>
        /// Kinesis 服务端点 URL, 如 "https://kinesis.us-west-2.amazonaws.com" 或开发端点的 URL。
        /// </summary>
        public string? Service { get; set; }

        /// <summary>
        /// AWS地区名称,例如“us-west-2”。
        /// </summary>
        public string? Region { get; set; }

        /// <summary>
        /// 肯尼斯流的名字
        /// </summary>
        public string StreamName { get; set; } = "OrleansTestStream";

        /// <summary>
        /// 获取或设定打给 Kinesis <c>GetRecords</c> 的硬块断面时的最低间隔。
        /// </summary>
        /// <remarks>
        /// Kinesis 允许每个打开的碎片每秒最多5个 <c>GetRecords</c> 交易。
        /// </remarks>
        public TimeSpan GetRecordsInterval { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// 获取或设置检查硬质地形变化之间的间隔。
        /// </summary>
        /// <remarks>
        /// 不支持重新使用活体硬化。 当地形变化时, 接收器停止并要求重新启动提供者 。
        /// </remarks>
        public TimeSpan TopologyCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    }

    internal sealed class KinesisStreamOptionsValidator(KinesisStreamOptions options, string name) : IConfigurationValidator
    {
        private static readonly TimeSpan MinimumGetRecordsInterval = TimeSpan.FromMilliseconds(200);

        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(options.StreamName))
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(KinesisStreamOptions.StreamName)} is required for the Kinesis stream provider '{name}'.");
            }

            if (options.GetRecordsInterval < MinimumGetRecordsInterval)
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(KinesisStreamOptions.GetRecordsInterval)} must be at least {MinimumGetRecordsInterval} for the Kinesis stream provider '{name}'.");
            }

            if (options.TopologyCheckInterval <= TimeSpan.Zero)
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(KinesisStreamOptions.TopologyCheckInterval)} must be greater than zero for the Kinesis stream provider '{name}'.");
            }

            if (string.IsNullOrEmpty(options.AccessKey) != string.IsNullOrEmpty(options.SecretKey))
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(KinesisStreamOptions.AccessKey)} and {nameof(KinesisStreamOptions.SecretKey)} must either both be configured or both be omitted for the Kinesis stream provider '{name}'.");
            }
        }
    }
}
