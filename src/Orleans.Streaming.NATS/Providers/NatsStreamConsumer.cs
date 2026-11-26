using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Orleans.Streaming.NATS;

/// <summary>
/// 围绕一个NATS JetStream 消费者包装
/// </summary>
internal sealed partial class NatsStreamConsumer(
    ILoggerFactory loggerFactory,
    NatsJSContext context,
    string provider,
    string stream,
    uint partition,
    int batchSize,
    INatsDeserialize<NatsStreamMessage> serializer)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<NatsStreamConsumer>();

    private readonly ConsumerConfig _config = new($"orleans-{provider}-{stream}-{partition}")
    {
        FilterSubject = $"{provider}.{partition}.>",
        MaxBatch = batchSize,
        DeliverPolicy = ConsumerConfigDeliverPolicy.All,
        MaxAckPending = batchSize
    };

    private INatsJSConsumer? _consumer;
    private int _consecutiveInitFailures;

    // 第一次尝试的纪录, 然后每100次 以避免洪水在~100米的投票。
    private const int InitLogInterval = 100;

    public async Task<(NatsStreamMessage[] Messages, int Count)> GetMessages(int messageCount = 0,
        CancellationToken cancellationToken = default)
    {
        if (this._consumer is null)
        {
            // 懒惰的重新尝试:尝试在每个投票周期中重新恢复原样。
            // 此操作处理初始化( ) 时的瞬态失败
            // (领导人选举、超时、网络停机)
            var shouldLog = this._consecutiveInitFailures % InitLogInterval == 0;
            try
            {
                if (shouldLog)
                {
                    this.LogConsumerReinitializationAttempt(this._consecutiveInitFailures + 1, provider, stream, partition);
                }

                await Initialize(cancellationToken);
                if (this._consecutiveInitFailures > 0)
                {
                    this.LogConsumerReinitialized(this._consecutiveInitFailures, provider, stream, partition);
                }

                this._consecutiveInitFailures = 0;
            }
            catch (Exception ex)
            {
                this._consecutiveInitFailures++;
                if (shouldLog)
                {
                    this.LogConsumerReinitializationFailed(ex, this._consecutiveInitFailures, provider, stream, partition);
                }

                return ([], 0);
            }

            // 如果再试后仍无效,保释(下一轮投票将再次重新)
            if (this._consumer is null)
            {
                return ([], 0);
            }
        }

        var batchCount = messageCount > 0 && messageCount < batchSize ? messageCount : batchSize;
        var messages = ArrayPool<NatsStreamMessage>.Shared.Rent(batchCount);

        var i = 0;

        await foreach (var msg in this._consumer.FetchNoWaitAsync(
                               new NatsJSFetchOpts { MaxMsgs = batchCount, Expires = TimeSpan.FromSeconds(10) },
                               serializer: serializer,
                               cancellationToken: cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            var streamMessage = msg.Data;
            if (streamMessage is null)
            {
                this.LogUnableToDeserializeMessage(msg.Subject);
                continue;
            }

            messages[i] = streamMessage;
            messages[i].ReplyTo = msg.ReplyTo;
            if (msg.Metadata.HasValue)
            {
                messages[i].Sequence = msg.Metadata.Value.Sequence.Stream;
            }

            i++;
        }

        return (messages, i);
    }

    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        var consumer =
            await context.CreateOrUpdateConsumerAsync(stream, this._config, cancellationToken);

        this._consumer = consumer;
    }

    #region Logging

    [LoggerMessage(1, LogLevel.Warning, "NATS consumer is not initialized. Attempting re-initialization on attempt {Attempt} for provider '{Provider}', stream '{Stream}', and partition '{Partition}'.")]
    private partial void LogConsumerReinitializationAttempt(int attempt, string provider, string stream, uint partition);

    [LoggerMessage(2, LogLevel.Information, "NATS consumer re-initialized successfully after {Attempts} failed attempt(s) for provider '{Provider}', stream '{Stream}', and partition '{Partition}'.")]
    private partial void LogConsumerReinitialized(int attempts, string provider, string stream, uint partition);

    [LoggerMessage(3, LogLevel.Warning, "NATS consumer re-initialization failed on attempt {Attempt} for provider '{Provider}', stream '{Stream}', and partition '{Partition}'. Will retry on the next poll.")]
    private partial void LogConsumerReinitializationFailed(Exception exception, int attempt, string provider, string stream, uint partition);

    [LoggerMessage(4, LogLevel.Warning, "Unable to deserialize the NATS message for subject '{Subject}'. Ignoring it.")]
    private partial void LogUnableToDeserializeMessage(string subject);

    #endregion Logging
}
