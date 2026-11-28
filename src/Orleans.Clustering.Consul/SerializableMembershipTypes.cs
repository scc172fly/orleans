using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Consul;
using Newtonsoft.Json;

namespace Orleans.Runtime.Host
{
    /// <summary>
    /// 可进行 JSON 序列化的对象；序列化并进行 Base64 编码后，构成 Silo 的 Consul KVPair 的 Value 部分。
    /// </summary>
    [JsonObject]
    public class ConsulSiloRegistration
    {
        /// <summary>
        /// 作为 KV 键的一部分持久化，因此不进行序列化。
        /// </summary>
        [JsonIgnore]
        internal string DeploymentId { get; set; } = null!;

        /// <summary>
        /// 作为 KV 键的一部分持久化，因此不进行序列化。
        /// </summary>
        [JsonIgnore]
        internal SiloAddress Address { get; set; } = null!;

        /// <summary>
        /// 持久化到单独的 KV 子键，因此不进行序列化，但保留在此处以便更清晰地组装 MembershipEntry。
        /// </summary>
        /// <remarks>
        /// 存储于单独的 KV 键，否则 IAmAlive 的定期更新会导致 Silo 的 KV.ModifyIndex 发生变化，
        /// 进而导致 UpdateRow 操作失败。
        /// </remarks>
        [JsonIgnore]
        internal DateTime IAmAliveTime { get; set; }

        /// <summary>
        /// 更新时用于比较 CAS 值；持久化为 KV.ModifyIndex，因此不进行序列化。
        /// </summary>
        [JsonIgnore]
        internal ulong LastIndex { get; set; }

        // 公共属性序列化到 KV.Value。
        /// <summary>
        /// 获取或设置 Silo 主机名。
        /// </summary>
        [JsonProperty]
        public string Hostname { get; set; } = null!;

        /// <summary>
        /// 获取或设置 Silo 网关端口。
        /// </summary>
        [JsonProperty]
        public int ProxyPort { get; set; }

        /// <summary>
        /// 获取或设置 Silo 启动时间。
        /// </summary>
        [JsonProperty]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 获取或设置 Silo 的成员资格状态。
        /// </summary>
        [JsonProperty]
        public SiloStatus Status { get; set; }

        /// <summary>
        /// 获取或设置 Silo 名称。
        /// </summary>
        [JsonProperty]
        public string SiloName { get; set; } = null!;

        /// <summary>
        /// 获取或设置怀疑此 Silo 出现故障的 Silo。
        /// </summary>
        [JsonProperty]
        public List<SuspectingSilo>? SuspectingSilos { get; set; }

        [JsonConstructor]
        internal ConsulSiloRegistration()
        {
            SuspectingSilos = new List<SuspectingSilo>();
        }
    }

    /// <summary>
    /// 可进行 JSON 序列化的对象；序列化并进行 Base64 编码后，构成 SuspectingSilos 列表中的每个条目。
    /// </summary>
    [JsonObject]
    public class SuspectingSilo
    {
        /// <summary>
        /// 获取或设置发出怀疑的 Silo 标识。
        /// </summary>
        [JsonProperty]
        public string Id { get; set; } = null!;

        /// <summary>
        /// 获取或设置记录怀疑的时间。
        /// </summary>
        [JsonProperty]
        public DateTime Time { get; set; }
    }

    /// <summary>
    /// 包含在 Consul KVPair 与 MembershipEntry 之间相互转换的方法。
    /// 使用 ConsulSiloRegistration 对象作为可序列化的 KV.Value，尽量减少转换操作。
    /// </summary>
    internal class ConsulSiloRegistrationAssembler
    {
        private const string DeploymentKVPrefix = "orleans";  // 确保 Consul 中存在 Orleans 的根 KV 命名空间。
        private const char KeySeparator = '/';
        internal const string SiloIAmAliveSuffix = "iamalive";
        internal const string VersionSuffix = "version";

        internal static string FormatVersionKey(string deploymentId, string? rootKvFolder) => $"{FormatDeploymentKVPrefix(deploymentId, rootKvFolder)}{KeySeparator}{VersionSuffix}";

        internal static string FormatDeploymentKVPrefix(string deploymentId, string? rootKvFolder)
        {
            // 向后兼容。
            if (string.IsNullOrEmpty(rootKvFolder))
            {
                return $"{DeploymentKVPrefix}{KeySeparator}{deploymentId}";
            }
            else
            {
                return $"{rootKvFolder}{KeySeparator}{DeploymentKVPrefix}{KeySeparator}{deploymentId}";
            }
        }

        internal static string FormatDeploymentSiloKey(string deploymentId, string? rootKvFolder, SiloAddress siloAddress)
        {
            return $"{FormatDeploymentKVPrefix(deploymentId, rootKvFolder)}{KeySeparator}{siloAddress.ToParsableString()}";
        }

        internal static string FormatSiloIAmAliveKey(string siloKey)
        {
            return $"{siloKey}{KeySeparator}{SiloIAmAliveSuffix}";
        }

        internal static string FormatSiloIAmAliveKey(string deploymentId, string? rootKvFolder, SiloAddress siloAddress)
        {
            return FormatSiloIAmAliveKey(FormatDeploymentSiloKey(deploymentId, rootKvFolder, siloAddress));
        }

        internal static ConsulSiloRegistration FromKVPairs(string deploymentId, KVPair siloKV, KVPair? iAmAliveKV)
        {
            var ret = JsonConvert.DeserializeObject<ConsulSiloRegistration>(Encoding.UTF8.GetString(siloKV.Value))!;

            var keyParts = siloKV.Key.Split(KeySeparator);
            ret.Address = SiloAddress.FromParsableString(keyParts[^1]);
            ret.DeploymentId = deploymentId;
            ret.LastIndex = siloKV.ModifyIndex;

            if (iAmAliveKV == null)
                ret.IAmAliveTime = ret.StartTime;
            else
                ret.IAmAliveTime = JsonConvert.DeserializeObject<DateTime>(Encoding.UTF8.GetString(iAmAliveKV.Value));

            return ret;
        }

        internal static ConsulSiloRegistration FromMembershipEntry(string deploymentId, MembershipEntry entry, string etag)
        {
            var ret = new ConsulSiloRegistration
            {
                DeploymentId = deploymentId,
                Address = entry.SiloAddress,
                IAmAliveTime = entry.IAmAliveTime,
                LastIndex = Convert.ToUInt64(etag),
                Hostname = entry.HostName,
                ProxyPort = entry.ProxyPort,
                StartTime = entry.StartTime,
                Status = entry.Status,
                SiloName = entry.SiloName,
                SuspectingSilos = entry.SuspectTimes?.Select(silo => new SuspectingSilo { Id = silo.Item1.ToParsableString(), Time = silo.Item2 }).ToList()
            };

            return ret;
        }

        internal static KVPair ToKVPair(ConsulSiloRegistration siloRegistration, string? rootKvFolder)
        {
            var ret = new KVPair(ConsulSiloRegistrationAssembler.FormatDeploymentSiloKey(siloRegistration.DeploymentId, rootKvFolder, siloRegistration.Address));
            ret.ModifyIndex = siloRegistration.LastIndex;
            ret.Value = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(siloRegistration));
            return ret;
        }

        internal static KVPair ToIAmAliveKVPair(string deploymentId, string? rootKvFolder, SiloAddress siloAddress, DateTime iAmAliveTime)
        {
            var ret = new KVPair(ConsulSiloRegistrationAssembler.FormatSiloIAmAliveKey(deploymentId, rootKvFolder, siloAddress));
            ret.Value = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(iAmAliveTime));
            return ret;
        }

        internal static Tuple<MembershipEntry, string> ToMembershipEntry(ConsulSiloRegistration siloRegistration)
        {
            var entry = new MembershipEntry
            {
                SiloAddress = siloRegistration.Address,
                HostName = siloRegistration.Hostname,
                Status = siloRegistration.Status,
                ProxyPort = siloRegistration.ProxyPort,
                StartTime = siloRegistration.StartTime,
                SuspectTimes = siloRegistration.SuspectingSilos?.Select(silo => new Tuple<SiloAddress, DateTime>(SiloAddress.FromParsableString(silo.Id), silo.Time)).ToList(),
                IAmAliveTime = siloRegistration.IAmAliveTime,
                SiloName = siloRegistration.SiloName,

                // 可选，仅适用于 Azure 角色，因此在此初始化。
                RoleName = string.Empty,
                UpdateZone = 0,
                FaultZone = 0
            };

            return new Tuple<MembershipEntry, string>(entry, siloRegistration.LastIndex.ToString());
        }
    }
}
