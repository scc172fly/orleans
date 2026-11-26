using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Orleans.Runtime
{
    /// <summary>
    /// 可观测到的 grain 生命周期。
    /// </summary>
    /// <remarks>
    /// 此类型通常在 <see cref="ILifecycleParticipant{IGrainLifecycle}"/> 中用作通用参数,
    /// a 参加 grain 激活生命周期阶段的手段。
    /// </remarks>
    public interface IGrainLifecycle : ILifecycleObservable
    {
        /// <summary>
        /// 登记一名 grain 移徙与会者。
        /// </summary>
        /// <param name="participant">与会人员。</param>
        void AddMigrationParticipant(IGrainMigrationParticipant participant);

        /// <summary>
        /// 不登记户a grain 移徙参与者。
        /// </summary>
        /// <param name="participant">与会人员。</param>
        void RemoveMigrationParticipant(IGrainMigrationParticipant participant);
    }

    /// <summary>
    /// 参与移徙期间 grain 活化的脱水和补液。
    /// </summary>
    public interface IGrainMigrationParticipant
    {
        /// <summary>
        /// 在 <see cref="IGrainBase.OnDeactivateAsync(DeactivationReason, CancellationToken)"/> 完成后,在移民启动时,要求启动初始激活。
        /// 参加者可以接触并更新脱水环境。
        /// </summary>
        /// <param name="dehydrationContext">脱水环境</param>
        void OnDehydrate(IDehydrationContext dehydrationContext);

        /// <summary>
        /// 在移民后,在 <see cref="IGrainBase.OnActivateAsync(CancellationToken)"/> 被调用之前,要求启动新的激活。
        /// 参与者可以恢复移徙状态。
        /// </summary>
        /// <param name="rehydrationContext">补液环境</param>
        void OnRehydrate(IRehydrationContext rehydrationContext);
    }

    /// <summary>
    /// 记录正在脱水以向另一地点迁移的 grain 启动状态。
    /// </summary>
    public interface IDehydrationContext
    {
        /// <summary>
        /// 获得在上下文中的钥匙。
        /// </summary>
        IEnumerable<string> Keys { get; }

        /// <summary>
        /// 将序列与提供的密钥联系起来,在脱水环境添加字节顺序。
        /// </summary>
        /// <param name="key">密钥。</param>
        /// <param name="value">数值。</param>
        void AddBytes(string key, ReadOnlySpan<byte> value);

        /// <summary>
        /// 将序列与提供的密钥联系起来,在脱水环境添加字节顺序。
        /// </summary>
        /// <param name="key">密钥。</param>
        /// <param name="valueWriter">一位代表曾将所提供的价值写在上下文。</param>
        /// <param name="value">向 <paramref name="valueWriter"/>提供的价值。</param>
        void AddBytes<T>(string key, Action<T, IBufferWriter<byte>> valueWriter, T value);

        /// <summary>
        /// 试图对脱水环境的价值,与所提供的钥匙相关,使用 <see cref="Orleans.Serialization.Serializer"/>进行序列排序。
        /// 如果找到序列器, 且密钥尚未添加, 那么该值就会被添加, 方法返回 <see langword="true"/>。
        /// 如果不存在序列器或密钥已经添加,则不会增加值,方法返回 <see langword="false"/>。
        /// </summary>
        /// <param name="key">密钥。</param>
        /// <param name="value">增加值。</param>
        bool TryAddValue<T>(string key, T? value);
    }

    /// <summary>
    /// 包含从另一地点移动后正在补水的 grain 激活状态。
    /// </summary>
    public interface IRehydrationContext
    {
        /// <summary>
        /// 获得在上下文中的钥匙。
        /// </summary>
        IEnumerable<string> Keys { get; }

        /// <summary>
        /// 尝试从补液环境获得一个序列的字节,与提供的钥匙相关。
        /// </summary>
        /// <param name="key">密钥。</param>
        /// <param name="value">如果存在的话, 值 。</param>
        /// <returns><see langword="true"/> 如果在上下文中存在钥匙,否则 <see langword="false"/>。</returns>
        bool TryGetBytes(string key, out ReadOnlySequence<byte> value);

        /// <summary>
        /// 试图从补液环境获得与所提供的钥匙相关的价值,使用 <see cref="Orleans.Serialization.Serializer"/>使其脱序。
        /// 如果找到序列器,且有键存在,则该数值被降为新线,方法返回 <see langword="true"/>。
        /// 如果不存在序列器或密钥已经添加,则不会增加值,方法返回 <see langword="false"/>。
        /// </summary>
        /// <param name="key">密钥。</param>
        /// <param name="value">如果存在的话, 值 。</param>
        /// <returns><see langword="true"/> 如果在上下文中存在钥匙,否则 <see langword="false"/>。</returns>
        bool TryGetValue<T>(string key, [NotNullWhen(true)] out T? value);
    }
}
