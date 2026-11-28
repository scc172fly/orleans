using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 标识事务参与者及其支持的协议角色。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct ParticipantId : IEquatable<ParticipantId>
    {
        /// <summary>
        /// 获取一个根据 <see cref="Name"/> 和 <see cref="Reference"/> 识别参与者的比较器，
        /// 与参与者支持的角色无关。
        /// </summary>
        public static readonly IEqualityComparer<ParticipantId> Comparer = new IdComparer();

        /// <summary>
        /// 指定参与者在事务提交协议中可以承担的角色。
        /// </summary>
        [GenerateSerializer]
        [Flags]
        public enum Role
        {
            /// <summary>
            /// 参与者存储可读取或写入的事务状态。
            /// </summary>
            Resource = 1 << 0,

            /// <summary>
            /// 参与者可以协调事务提交。
            /// </summary>
            Manager = 1 << 1,

            /// <summary>
            /// 选择事务管理器时优先考虑此参与者。
            /// </summary>
            PriorityManager = 1 << 2
        }

        /// <summary>
        /// 获取用于标识参与者在所属 Grain 内的事务资源的名称。
        /// </summary>
        [Id(0)]
        public string Name { get; }

        /// <summary>
        /// 获取承载参与者的 Grain 的引用。
        /// </summary>
        [Id(1)]
        public GrainReference Reference { get; }

        /// <summary>
        /// 获取参与者支持的协议角色。
        /// </summary>
        [Id(2)]
        public Role SupportedRoles { get; }

        /// <summary>
        /// 初始化新的事务参与者标识符。
        /// </summary>
        /// <param name="name">用于标识所属 Grain 内的事务资源的名称。</param>
        /// <param name="reference">承载参与者的 Grain 的引用。</param>
        /// <param name="supportedRoles">参与者支持的协议角色。</param>
        public ParticipantId(string name, GrainReference reference, Role supportedRoles)
        {
            this.Name = name;
            this.Reference = reference;
            this.SupportedRoles = supportedRoles;
        }

        /// <inheritdoc/>
        public bool Equals(ParticipantId other) =>
            Name == other.Name
            && EqualityComparer<GrainReference>.Default.Equals(Reference, other.Reference)
            && SupportedRoles == other.SupportedRoles;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is ParticipantId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Name, Reference, SupportedRoles);

        /// <summary>
        /// 确定两个事务参与者标识符及其支持的角色是否相等。
        /// </summary>
        public static bool operator ==(ParticipantId left, ParticipantId right) => left.Equals(right);

        /// <summary>
        /// 确定两个事务参与者标识符或其支持的角色是否不相等。
        /// </summary>
        public static bool operator !=(ParticipantId left, ParticipantId right) => !left.Equals(right);

        /// <summary>
        /// 返回此参与者的诊断表示形式。
        /// </summary>
        /// <returns>包含参与者名称和 Grain 引用的字符串。</returns>
        public override string ToString()
        {
            return $"ParticipantId.{Name}.{Reference}";
        }

        /// <summary>
        /// 根据资源名称和 Grain 引用比较参与者标识符。
        /// </summary>
        [GenerateSerializer, Immutable]
        public sealed class IdComparer : IEqualityComparer<ParticipantId>
        {
            /// <summary>
            /// 确定两个参与者标识符是否指向同一资源。
            /// </summary>
            /// <param name="x">要比较的第一个参与者标识符。</param>
            /// <param name="y">要比较的第二个参与者标识符。</param>
            /// <returns>
            /// <see langword="true"/>，如果两个标识符具有相同的资源名称和 Grain 引用；
            /// 否则为 <see langword="false"/>。
            /// </returns>
            public bool Equals(ParticipantId x, ParticipantId y)
            {
                return string.CompareOrdinal(x.Name, y.Name) == 0 && Equals(x.Reference, y.Reference);
            }

            /// <summary>
            /// 返回根据参与者的资源名称和 Grain 引用生成的哈希码。
            /// </summary>
            /// <param name="obj">参与者标识符。</param>
            /// <returns><paramref name="obj"/> 的哈希码。</returns>
            public int GetHashCode(ParticipantId obj) => HashCode.Combine(obj.Name, obj.Reference);
        }
    }

    /// <summary>
    /// 提供按协议角色检查和选择事务参与者的操作。
    /// </summary>
    public static class ParticipantRoleExtensions
    {
        /// <summary>
        /// 确定参与者是否支持至少一个指定角色。
        /// </summary>
        /// <param name="participant">要检查的参与者。</param>
        /// <param name="role">要测试的一个或多个角色。</param>
        /// <returns>
        /// <see langword="true"/>，如果参与者支持至少一个指定角色；
        /// 否则为 <see langword="false"/>。
        /// </returns>
        public static bool SupportsRoles(this ParticipantId participant, ParticipantId.Role role)
        {
            return (participant.SupportedRoles & role) != 0;
        }

        /// <summary>
        /// 确定参与者是否存储事务状态。
        /// </summary>
        /// <param name="participant">要检查的参与者。</param>
        /// <returns><see langword="true"/>，如果参与者支持资源角色；否则为 <see langword="false"/>。</returns>
        public static bool IsResource(this ParticipantId participant)
        {
            return participant.SupportsRoles(ParticipantId.Role.Resource);
        }

        /// <summary>
        /// 确定参与者是否可以协调事务提交。
        /// </summary>
        /// <param name="participant">要检查的参与者。</param>
        /// <returns><see langword="true"/>，如果参与者支持管理器角色；否则为 <see langword="false"/>。</returns>
        public static bool IsManager(this ParticipantId participant)
        {
            return participant.SupportsRoles(ParticipantId.Role.Manager);
        }

        /// <summary>
        /// 确定选择事务管理器时是否优先考虑此参与者。
        /// </summary>
        /// <param name="participant">要检查的参与者。</param>
        /// <returns><see langword="true"/>，如果参与者支持优先管理器角色；否则为 <see langword="false"/>。</returns>
        public static bool IsPriorityManager(this ParticipantId participant)
        {
            return participant.SupportsRoles(ParticipantId.Role.PriorityManager);
        }

        /// <summary>
        /// 选择存储事务状态的参与者。
        /// </summary>
        /// <param name="participants">参与者及其事务访问计数。</param>
        /// <returns>支持资源角色的参与者及其访问计数。</returns>
        public static IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> SelectResources(this IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> participants)
        {
            return participants.Where(p => p.Key.IsResource());
        }

        /// <summary>
        /// 选择可以协调事务提交的参与者。
        /// </summary>
        /// <param name="participants">参与者及其事务访问计数。</param>
        /// <returns>支持管理器角色的参与者及其访问计数。</returns>
        public static IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> SelectManagers(this IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> participants)
        {
            return participants.Where(p => p.Key.IsManager());
        }

        /// <summary>
        /// 选择在确定事务管理器时应优先考虑的参与者。
        /// </summary>
        /// <param name="participants">要筛选的参与者。</param>
        /// <returns>支持优先管理器角色的参与者。</returns>
        public static IEnumerable<ParticipantId> SelectPriorityManagers(this IEnumerable<ParticipantId> participants)
        {
            return participants.Where(p => p.IsPriorityManager());
        }
    }
}
