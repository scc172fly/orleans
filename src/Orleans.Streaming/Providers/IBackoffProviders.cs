using Orleans.Internal;
using Orleans.Streams;

namespace Orleans.Runtime.Providers;

/// <summary>
/// 确定连续几次发送信息之间 <see cref="IPersistentStreamPullingAgent"/> 将等待多久的职能。
/// </summary>
public interface IMessageDeliveryBackoffProvider : IBackoffProvider { }

/// <summary>
/// 确定在连续尝试从队列中读取消息之间 <see cref="IPersistentStreamPullingAgent"/> 要等待多久 的功能。
/// </summary>
public interface IQueueReaderBackoffProvider : IBackoffProvider { }
