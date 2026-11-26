using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Consul;
using Newtonsoft.Json;

namespace Orleans.Runtime.Host
{
    /// <summary>
    /// JSON 串列物体,在序列和Base64编码后构成 Silo领事KVPair的值值部分
    /// </summary>
    [JsonObject]
    public class ConsulSiloRegistration
    {
        /// <summary>
        /// 因此作为 KV 密钥的一部分存在, 没有序列化 。
        /// </summary>
        [JsonIgnore]
        internal string DeploymentId { get; set; } = null!;

        /// <summary>
        /// 因此作为 KV 密钥的一部分存在, 没有序列化 。
        /// </summary>
        [JsonIgnore]
        internal SiloAddress Address { get; set; } = null!;

        /// <summary>
        /// 以单独的 KV 子键为基号,因此没有序列,而是在这里举行,以便更清洁的组装 MembershipEntry。
        /// </summary>
        /// <remarks>
        /// 以单独的 KV 存储, 否则对 IAmAlive 的定期更新更新导致 Silo KV.ModifyIndex 更改 
        /// 从而导致 UpdateRow 操作失败。
        /// </remarks>
        [JsonIgnore]
        internal DateTime IAmAliveTime { get; set; }

        /// <summary>
        /// 用于在更新时比较CAS值,一直以 KV.ModifyIndex 为单位,因此没有序列。
        /// </summary>
        [JsonIgnore]
        internal ulong LastIndex { get; set; }

        //公共财产序列编号为 KV.Value
        /// <summary>
        /// 获取或设置 silo 主机名称。
        /// </summary>
        [JsonProperty]
        public string Hostname { get; set; } = null!;

        /// <summary>
        /// 获取或设置 silo 网关端口。
        /// </summary>
        [JsonProperty]
        public int ProxyPort { get; set; }

        /// <summary>
        /// 获取或设置 silo 启动的时间。
        /// </summary>
        [JsonProperty]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 获取或设置 silo 成员资格。
        /// </summary>
        [JsonProperty]
        public SiloStatus Status { get; set; }

        /// <summary>
        /// 获取或设置 silo 名称。
        /// </summary>
        [JsonProperty]
        public string SiloName { get; set; } = null!;

        /// <summary>
        /// 设置 silos 怀疑此事 silo.
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
    /// JSON 串列物体,在序列和Base64编码后,填入 SuspectingSilos 清单中的每一条目
    /// </summary>
    [JsonObject]
    public class SuspectingSilo
    {
        /// <summary>
        /// 获取或设置疑似 silo的身份识别符。
        /// </summary>
        [JsonProperty]
        public string Id { get; set; } = null!;

        /// <summary>
        /// 获取或设定了嫌疑记录的时间。
        /// </summary>
        [JsonProperty]
        public DateTime Time { get; set; }
    }

    /// <summary>
    /// 含有将KVPair领事转换为 MembershipEntry的方法。  
    /// 这将 ConsulSiloRegistration 对象用作可连号 KV.Value ,并最大限度地减少转换操作。
    /// </summary>
    internal class ConsulSiloRegistrationAssembler
    {
        private const string DeploymentKVPrefix = "orleans";  //在领事中确保 orleans 的根 KV 命名空间
        private const char KeySeparator = '/';
        internal const string SiloIAmAliveSuffix = "iamalive";
        internal const string VersionSuffix = "version";

        internal static string FormatVersionKey(string deploymentId, string? rootKvFolder) => $"{FormatDeploymentKVPrefix(deploymentId, rootKvFolder)}{KeySeparator}{VersionSuffix}";

        internal static string FormatDeploymentKVPrefix(string deploymentId, string? rootKvFolder)
        {
            //后向兼容
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

                // 仅允许 - 仅用于此处已启动的 Azure 角色
                RoleName = string.Empty,
                UpdateZone = 0,
                FaultZone = 0
            };

            return new Tuple<MembershipEntry, string>(entry, siloRegistration.LastIndex.ToString());
        }
    }
}
