using Orleans.Internal;
using Orleans.Streams;

namespace Orleans.Runtime.Providers;

/// <summary>
/// 提供确定 <see cref="IPersistentStreamPullingAgent"/> 连续尝试投递消息之间等待时长的功能。
/// </summary>
public interface IMessageDeliveryBackoffProvider : IBackoffProvider { }

/// <summary>
/// 提供确定 <see cref="IPersistentStreamPullingAgent"/> 连续尝试从队列读取消息之间等待时长的功能。
/// </summary>
public interface IQueueReaderBackoffProvider : IBackoffProvider { }
