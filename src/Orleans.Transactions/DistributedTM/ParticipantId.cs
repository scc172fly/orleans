using System;
using System.Collections.Generic;
using System.Linq;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions
{
    /// <summary>
    /// 确定交易参与者及其支持的协议作用。
    /// </summary>
    [Serializable, GenerateSerializer, Immutable]
    public readonly struct ParticipantId : IEquatable<ParticipantId>
    {
        /// <summary>
        /// 获得一个比较器,该比较器将识别 <see cref="Name"/> 和 <see cref="Reference"/>的参与者,
        /// 独立于其支助作用。
        /// </summary>
        public static readonly IEqualityComparer<ParticipantId> Comparer = new IdComparer();

        /// <summary>
        /// 指定参与者在交易承诺协议中可以发挥的作用。
        /// </summary>
        [GenerateSerializer]
        [Flags]
        public enum Role
        {
            /// <summary>
            /// 参与人储存可读或写的交易状态。
            /// </summary>
            Resource = 1 << 0,

            /// <summary>
            /// 参与者可以协调交易承诺。
            /// </summary>
            Manager = 1 << 1,

            /// <summary>
            /// 在选择交易经理时,更倾向于参与人。
            /// </summary>
            PriorityManager = 1 << 2
        }

        /// <summary>
        /// 查找在 grain内确定参与人交易资源的名称。
        /// </summary>
        [Id(0)]
        public string Name { get; }

        /// <summary>
        /// 获取主机主机的 grain 引用。
        /// </summary>
        [Id(1)]
        public GrainReference Reference { get; }

        /// <summary>
        /// 获得参与者支持的礼宾角色。
        /// </summary>
        [Id(2)]
        public Role SupportedRoles { get; }

        /// <summary>
        /// 初始化新的交易参与者识别符号。
        /// </summary>
        /// <param name="name">用以识别其内部交易资源的名称 grain.</param>
        /// <param name="reference">提及 grain 以参与者的所在,为东道主。</param>
        /// <param name="supportedRoles">参与方所支持的礼宾作用。</param>
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
        /// 确定两个交易参与者识别资料及其支持作用是否相等。
        /// </summary>
        public static bool operator ==(ParticipantId left, ParticipantId right) => left.Equals(right);

        /// <summary>
        /// 确定两个交易参与者识别资料或其支持作用是否不平等。
        /// </summary>
        public static bool operator !=(ParticipantId left, ParticipantId right) => !left.Equals(right);

        /// <summary>
        /// 返回该参与者的诊断性陈述。
        /// </summary>
        /// <returns>一个包含参与者姓名和 grain 参考的字符串。</returns>
        public override string ToString()
        {
            return $"ParticipantId.{Name}.{Reference}";
        }

        /// <summary>
        /// 按资源名称和 grain 参考比对参与人识别符号。
        /// </summary>
        [GenerateSerializer, Immutable]
        public sealed class IdComparer : IEqualityComparer<ParticipantId>
        {
            /// <summary>
            /// 确定两个参与者识别符号是否指同一资源。
            /// </summary>
            /// <param name="x">比较第一个参与者识别码。</param>
            /// <param name="y">比较第二个参与者识别码。</param>
            /// <returns>
            /// <see langword="true"/> 当两个识别符号都具有相同的资源名称和 grain 参考;
            /// 否则, <see langword="false"/>.
            /// </returns>
            public bool Equals(ParticipantId x, ParticipantId y)
            {
                return string.CompareOrdinal(x.Name, y.Name) == 0 && Equals(x.Reference, y.Reference);
            }

            /// <summary>
            /// 返回来自参与者资源名称和 grain 参考文献的散列代码。
            /// </summary>
            /// <param name="obj">参与人识别符号。</param>
            /// <returns> <paramref name="obj"/>的散列码。</returns>
            public int GetHashCode(ParticipantId obj) => HashCode.Combine(obj.Name, obj.Reference);
        }
    }

    /// <summary>
    /// 提供业务,按协议作用检查和选择交易参与者。
    /// </summary>
    public static class ParticipantRoleExtensions
    {
        /// <summary>
        /// 确定参与者是否至少支持其中一项特定角色。
        /// </summary>
        /// <param name="participant">检查的参与者。</param>
        /// <param name="role">测试的角色或作用。</param>
        /// <returns>
        /// (a)<see langword="true"/> ,当参与者支持至少一个特定角色时;
        /// 否则, <see langword="false"/>.
        /// </returns>
        public static bool SupportsRoles(this ParticipantId participant, ParticipantId.Role role)
        {
            return (participant.SupportedRoles & role) != 0;
        }

        /// <summary>
        /// 确定参与者是否储存交易状态。
        /// </summary>
        /// <param name="participant">检查的参与者。</param>
        /// <returns><see langword="true"/> 当参与者支持资源作用时;否则, <see langword="false"/>。</returns>
        public static bool IsResource(this ParticipantId participant)
        {
            return participant.SupportsRoles(ParticipantId.Role.Resource);
        }

        /// <summary>
        /// 确定参与者能否协调交易承诺。
        /// </summary>
        /// <param name="participant">检查的参与者。</param>
        /// <returns><see langword="true"/> 当参与人支持管理者角色时;否则, <see langword="false"/>。</returns>
        public static bool IsManager(this ParticipantId participant)
        {
            return participant.SupportsRoles(ParticipantId.Role.Manager);
        }

        /// <summary>
        /// 确定在选择交易管理人时是否偏好参与人。
        /// </summary>
        /// <param name="participant">检查的参与者。</param>
        /// <returns><see langword="true"/> 当参与者支持优先管理者的作用时;否则, <see langword="false"/>.</returns>
        public static bool IsPriorityManager(this ParticipantId participant)
        {
            return participant.SupportsRoles(ParticipantId.Role.PriorityManager);
        }

        /// <summary>
        /// 选择存储交易状态的参与者。
        /// </summary>
        /// <param name="participants">参与者及其交易准入盘点。</param>
        /// <returns>支持资源作用的参与者,他们利用机会的人数。</returns>
        public static IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> SelectResources(this IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> participants)
        {
            return participants.Where(p => p.Key.IsResource());
        }

        /// <summary>
        /// 选择能够协调交易承诺的参与者。
        /// </summary>
        /// <param name="participants">参与者及其交易准入盘点。</param>
        /// <returns>支持管理人员作用的参与者,他们可以计算出他们的访问次数。</returns>
        public static IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> SelectManagers(this IEnumerable<KeyValuePair<ParticipantId, AccessCounter>> participants)
        {
            return participants.Where(p => p.Key.IsManager());
        }

        /// <summary>
        /// 选择在选择交易管理器时首选的参与者。
        /// </summary>
        /// <param name="participants">参与者过滤。</param>
        /// <returns>支持优先管理者作用的参与者。</returns>
        public static IEnumerable<ParticipantId> SelectPriorityManagers(this IEnumerable<ParticipantId> participants)
        {
            return participants.Where(p => p.IsPriorityManager());
        }
    }
}
