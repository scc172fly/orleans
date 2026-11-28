using System;

namespace Orleans.Streaming.Redis;

/// <summary>
/// Redis 流提供程序的操作失败时抛出的异常。
/// </summary>
[GenerateSerializer]
public class RedisStreamingException : Exception
{
    /// <summary>
    /// 初始化 <see cref="RedisStreamingException"/> 的新实例。
    /// </summary>
    public RedisStreamingException()
    {
    }

    /// <summary>
    /// 初始化 <see cref="RedisStreamingException"/> 的新实例。
    /// </summary>
    /// <param name="message">解释异常原因的错误消息。</param>
    public RedisStreamingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="RedisStreamingException"/> 的新实例。
    /// </summary>
    /// <param name="message">解释异常原因的错误消息。</param>
    /// <param name="inner">导致当前异常的异常；如果未指定内部异常，则为空引用（在 Visual Basic 中为 Nothing）。</param>
    public RedisStreamingException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
