using System;

namespace Orleans.Streaming.Redis;

/// <summary>
/// 供应方在 Redis 流动业务失败时投放的例外。
/// </summary>
[GenerateSerializer]
public class RedisStreamingException : Exception
{
    /// <summary>
    /// 初始化 <see cref="RedisStreamingException"/>新实例。
    /// </summary>
    public RedisStreamingException()
    {
    }

    /// <summary>
    /// 初始化 <see cref="RedisStreamingException"/>新实例。
    /// </summary>
    /// <param name="message">解释例外原因的错误信息。</param>
    public RedisStreamingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 初始化 <see cref="RedisStreamingException"/>新实例。
    /// </summary>
    /// <param name="message">解释例外原因的错误信息。</param>
    /// <param name="inner">造成目前例外的原因,或者如果没有具体说明内部例外,则无效引用(《视觉基本》中没有任何内容)。</param>
    public RedisStreamingException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
