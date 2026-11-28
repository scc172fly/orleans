using System;
using System.Diagnostics.CodeAnalysis;

namespace Orleans.Runtime
{
    /// <summary>
    /// 系统状态值及当前状态记录。
    /// </summary>
    internal sealed class SystemStatus : IEquatable<SystemStatus>
    {
        private enum InternalSystemStatus
        {
            Unknown = 0,
            Creating,
            Created,
            Starting,
            Running,
            Stopping,
            ShuttingDown,
            Terminated,
        };

        /// <summary>状态 = Unknown（未知）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus Unknown = new SystemStatus(InternalSystemStatus.Unknown);

        /// <summary>状态 = Creating（正在创建）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus Creating = new SystemStatus(InternalSystemStatus.Creating);

        /// <summary>状态 = Created（已创建）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus Created = new SystemStatus(InternalSystemStatus.Created);

        /// <summary>状态 = Starting（正在启动）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus Starting = new SystemStatus(InternalSystemStatus.Starting);

        /// <summary>状态 = Running（正在运行）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus Running = new SystemStatus(InternalSystemStatus.Running);

        /// <summary>状态 = Stopping（正在停止）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus Stopping = new SystemStatus(InternalSystemStatus.Stopping);

        /// <summary>状态 = ShuttingDown（正在关闭）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus ShuttingDown = new SystemStatus(InternalSystemStatus.ShuttingDown);

        /// <summary>状态 = Terminated（已终止）</summary>
        [SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public static readonly SystemStatus Terminated = new SystemStatus(InternalSystemStatus.Terminated);

        private readonly InternalSystemStatus value;
        private SystemStatus(InternalSystemStatus name) { this.value = name; }

        /// <see cref="object.ToString"/>
        public override string ToString() { return this.value.ToString(); }
        /// <see cref="object.GetHashCode"/>
        public override int GetHashCode() { return this.value.GetHashCode(); }
        /// <see cref="object.Equals(object)"/>
        public override bool Equals(object? obj) { var ss = obj as SystemStatus; return ss != null && this.Equals(ss); }
        /// <see cref="IEquatable{T}.Equals(T)"/>
        public bool Equals(SystemStatus? other) { return (other != null) && this.value.Equals(other.value); }
    }
}
