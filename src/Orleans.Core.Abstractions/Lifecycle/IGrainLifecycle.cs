using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Orleans.Runtime
{
    /// <summary>
    /// 可观察的 Grain 生命周期。
    /// </summary>
    /// <remarks>
    /// 此类型通常用作 <see cref="ILifecycleParticipant{IGrainLifecycle}"/> 的泛型参数，
    /// 以便参与 Grain 激活实例的各个生命周期阶段。
    /// </remarks>
    public interface IGrainLifecycle : ILifecycleObservable
    {
        /// <summary>
        /// 注册 Grain 迁移参与者。
        /// </summary>
        /// <param name="participant">参与者。</param>
        void AddMigrationParticipant(IGrainMigrationParticipant participant);

        /// <summary>
        /// 注销 Grain 迁移参与者。
        /// </summary>
        /// <param name="participant">参与者。</param>
        void RemoveMigrationParticipant(IGrainMigrationParticipant participant);
    }

    /// <summary>
    /// 在迁移期间参与 Grain 激活实例状态的提取和恢复。
    /// </summary>
    public interface IGrainMigrationParticipant
    {
        /// <summary>
        /// 启动迁移时，在 <see cref="IGrainBase.OnDeactivateAsync(DeactivationReason, CancellationToken)"/> 完成后于原激活实例上调用。
        /// 参与者可以访问和更新状态提取上下文。
        /// </summary>
        /// <param name="dehydrationContext">状态提取上下文。</param>
        void OnDehydrate(IDehydrationContext dehydrationContext);

        /// <summary>
        /// 迁移后，在调用 <see cref="IGrainBase.OnActivateAsync(CancellationToken)"/> 之前于新激活实例上调用。
        /// 参与者可以从迁移上下文恢复状态。
        /// </summary>
        /// <param name="rehydrationContext">状态恢复上下文。</param>
        void OnRehydrate(IRehydrationContext rehydrationContext);
    }

    /// <summary>
    /// 记录正在为迁移到另一位置而提取状态的 Grain 激活实例的状态。
    /// </summary>
    public interface IDehydrationContext
    {
        /// <summary>
        /// 获取上下文中的键。
        /// </summary>
        IEnumerable<string> Keys { get; }

        /// <summary>
        /// 将字节序列添加到状态提取上下文，并将其与提供的键关联。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值。</param>
        void AddBytes(string key, ReadOnlySpan<byte> value);

        /// <summary>
        /// 将字节序列添加到状态提取上下文，并将其与提供的键关联。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="valueWriter">用于将给定值写入上下文的委托。</param>
        /// <param name="value">要传递给 <paramref name="valueWriter"/> 的值。</param>
        void AddBytes<T>(string key, Action<T, IBufferWriter<byte>> valueWriter, T value);

        /// <summary>
        /// 尝试将一个值添加到状态提取上下文并与提供的键关联，使用 <see cref="Orleans.Serialization.Serializer"/> 对该值进行序列化。
        /// 如果找到该值的序列化器，且键尚未添加，则添加该值并返回 <see langword="true"/>。
        /// 如果不存在序列化器或键已添加，则不添加该值，方法返回 <see langword="false"/>。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">要添加的值。</param>
        bool TryAddValue<T>(string key, T? value);
    }

    /// <summary>
    /// 包含从其他位置迁移后正在恢复状态的 Grain 激活实例的状态。
    /// </summary>
    public interface IRehydrationContext
    {
        /// <summary>
        /// 获取上下文中的键。
        /// </summary>
        IEnumerable<string> Keys { get; }

        /// <summary>
        /// 尝试从状态恢复上下文获取与提供的键关联的字节序列。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值（如果存在）。</param>
        /// <returns>如果上下文中存在该键则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        bool TryGetBytes(string key, out ReadOnlySequence<byte> value);

        /// <summary>
        /// 尝试从状态恢复上下文获取与提供的键关联的值，使用 <see cref="Orleans.Serialization.Serializer"/> 对该值进行反序列化。
        /// 如果找到该值的序列化器且键存在，则反序列化该值并返回 <see langword="true"/>。
        /// 如果不存在序列化器或键已添加，则不添加该值，方法返回 <see langword="false"/>。
        /// </summary>
        /// <param name="key">键。</param>
        /// <param name="value">值（如果存在）。</param>
        /// <returns>如果上下文中存在该键则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
        bool TryGetValue<T>(string key, [NotNullWhen(true)] out T? value);
    }
}
