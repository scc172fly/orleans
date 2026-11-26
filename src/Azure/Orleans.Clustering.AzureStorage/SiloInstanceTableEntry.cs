using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Orleans.Runtime;

namespace Orleans.AzureUtils
{
    internal class SiloInstanceTableEntry : ITableEntity
    {
        public string? DeploymentId { get; set; }    // PartitionKey
        public string? Address { get; set; }         // RowKey
        public string? Port { get; set; }            // RowKey
        public string? Generation { get; set; }      // RowKey

        public string? HostName { get; set; }        // 强制性
        public string? Status { get; set; }          // 强制性
        public string? ProxyPort { get; set; }       // 任择

        public string? RoleName { get; set; }        // 仅允许 - 只允许 Azure 角色
        public string? SiloName { get; set; }
        public string? InstanceName { get; set; }    // 后向兼容性我们留下旧栏目 直到所有客户都更新代码到新版本
        public string? UpdateZone { get; set; }         // 仅允许 - 只允许 Azure 角色
        public string? FaultZone { get; set; }          // 仅允许 - 只允许 Azure 角色

        public string? SuspectingSilos { get; set; }          // 是为了生命
        public string? SuspectingTimes { get; set; }          // 是为了生命

        public string? StartTime { get; set; }          // 开始此时间 silo 。 对于诊断, 请使用它 。
        public string? IAmAliveTime { get; set; }           // 此时此时间 silo 更新后它还活着,用于诊断
        public string? MembershipVersion { get; set; }

        public string PartitionKey { get; set; } = null!;
        public string RowKey { get; set; } = null!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        internal const string TABLE_VERSION_ROW = "VersionRow";
        // Azure 表查询按 RowKey排序。这些数值围绕每个 IPv4 和 IPv6 地址字符串排序。
        internal const string TABLE_VERSION_ROW_MIN = "!Start";
        internal const string TABLE_VERSION_ROW_MAX = "~End";
        internal const char Seperator = '-';

        internal static bool IsVersionRow(string rowKey)
            => rowKey is TABLE_VERSION_ROW or TABLE_VERSION_ROW_MIN or TABLE_VERSION_ROW_MAX;

        public static string ConstructRowKey(SiloAddress silo)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}-{2}",
                silo.Endpoint.Address,
                silo.Endpoint.Port,
                silo.Generation);
        }
        internal static SiloAddress UnpackRowKey(string rowKey)
        {
            var debugInfo = "UnpackRowKey";
            try
            {
#if DEBUG
                debugInfo = string.Format(CultureInfo.CurrentCulture, "UnpackRowKey: RowKey={0}", rowKey);
                Trace.TraceInformation(debugInfo);
#endif
                int idx1 = rowKey.IndexOf(Seperator, StringComparison.Ordinal);
                ReadOnlySpan<char> rowKeySpan = rowKey.AsSpan();
                int idx2 = rowKeySpan.LastIndexOf(Seperator);
#if DEBUG
                debugInfo = string.Format(
                    CultureInfo.CurrentCulture,
                    "UnpackRowKey: RowKey={0} Idx1={1} Idx2={2}",
                    rowKey,
                    idx1,
                    idx2);
#endif
                ReadOnlySpan<char> addressStr = rowKeySpan[..idx1];
                ReadOnlySpan<char> portStr = rowKeySpan.Slice(idx1 + 1, idx2 - idx1 - 1);
                ReadOnlySpan<char> genStr = rowKeySpan[(idx2 + 1)..];
#if DEBUG
                debugInfo = string.Format(
                    CultureInfo.CurrentCulture,
                    "UnpackRowKey: RowKey={0} -> Address={1} Port={2} Generation={3}",
                    rowKey,
                    addressStr.ToString(),
                    portStr.ToString(),
                    genStr.ToString());

                Trace.TraceInformation(debugInfo);
#endif
                IPAddress address = IPAddress.Parse(addressStr);
                int port = int.Parse(portStr, NumberStyles.Integer, CultureInfo.InvariantCulture);
                int generation = int.Parse(genStr, NumberStyles.Integer, CultureInfo.InvariantCulture);
                return SiloAddress.New(address, port, generation);
            }
            catch (Exception exc)
            {
                throw new AggregateException("Error from " + debugInfo, exc);
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (IsVersionRow(RowKey))
            {
                sb.Append("VersionRow [").Append(RowKey);
                sb.Append(" Deployment=").Append(DeploymentId);
                sb.Append(" MembershipVersion=").Append(MembershipVersion);
                sb.Append("]");
            }
            else
            {
                sb.Append("OrleansSilo [");
                sb.Append(" Deployment=").Append(DeploymentId);
                sb.Append(" LocalEndpoint=").Append(Address);
                sb.Append(" LocalPort=").Append(Port);
                sb.Append(" Generation=").Append(Generation);

                sb.Append(" Host=").Append(HostName);
                sb.Append(" Status=").Append(Status);
                sb.Append(" ProxyPort=").Append(ProxyPort);

                if (!string.IsNullOrEmpty(RoleName)) sb.Append(" RoleName=").Append(RoleName);
                sb.Append(" SiloName=").Append(SiloName);
                sb.Append(" UpgradeZone=").Append(UpdateZone);
                sb.Append(" FaultZone=").Append(FaultZone);

                if (!string.IsNullOrEmpty(SuspectingSilos)) sb.Append(" SuspectingSilos=").Append(SuspectingSilos);
                if (!string.IsNullOrEmpty(SuspectingTimes)) sb.Append(" SuspectingTimes=").Append(SuspectingTimes);
                sb.Append(" StartTime=").Append(StartTime);
                sb.Append(" IAmAliveTime=").Append(IAmAliveTime);
                sb.Append("]");
            }
            return sb.ToString();
        }
    }
}
