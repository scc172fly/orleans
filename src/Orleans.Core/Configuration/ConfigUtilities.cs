using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Orleans.Runtime.Configuration
{
    /// <summary>
    /// 配置工作水电类。
    /// </summary>
    public static class ConfigUtilities
    {
        // 时间间隔以小数位数输入,然后是单位字符串: "ms"、 "s"、 "m"、 "hr"
        internal static TimeSpan ParseTimeSpan(string input, string errorMessage)
        {
            long unitSize;
            string numberInput;
            var trimmedInput = input.Trim();
            var unitSymbol = trimmedInput.Length > 0 ? char.ToLowerInvariant(trimmedInput[^1]) : '\0';
            if (unitSymbol == 's' && trimmedInput.Length > 1 && char.ToLowerInvariant(trimmedInput[^2]) == 'm')
            {
                unitSize = 10000;
                numberInput = trimmedInput.Remove(trimmedInput.Length - 2).Trim();
            }
            else if (unitSymbol == 's')
            {
                unitSize = 1000 * 10000;
                numberInput = trimmedInput.Remove(trimmedInput.Length - 1).Trim();
            }
            else if (unitSymbol == 'm')
            {
                unitSize = 60 * 1000 * 10000;
                numberInput = trimmedInput.Remove(trimmedInput.Length - 1).Trim();
            }
            else if (unitSymbol == 'r' && trimmedInput.Length > 1 && char.ToLowerInvariant(trimmedInput[^2]) == 'h')
            {
                unitSize = 60 * 60 * 1000 * 10000L;
                numberInput = trimmedInput.Remove(trimmedInput.Length - 2).Trim();
            }
            else
            {
                unitSize = 1000 * 10000; // 默认秒为秒
                numberInput = trimmedInput;
            }
            decimal rawTimeSpan;
            if (!decimal.TryParse(numberInput, NumberStyles.Any, CultureInfo.InvariantCulture, out rawTimeSpan))
            {
                throw new FormatException(errorMessage + ". Tried to parse " + input);
            }
            return TimeSpan.FromTicks((long)(rawTimeSpan * unitSize));
        }

        internal static IPAddress? ResolveIPAddressOrDefault(byte[]? subnet, AddressFamily family)
        {
            IList<IPAddress> nodeIps = NetworkInterface.GetAllNetworkInterfaces()
                            .Where(iface => iface.OperationalStatus == OperationalStatus.Up)
                            .SelectMany(iface => iface.GetIPProperties().UnicastAddresses)
                            .Select(addr => addr.Address)
                            .Where(addr => addr.AddressFamily == family && !IPAddress.IsLoopback(addr))
                            .ToList();

            var ipAddress = PickIPAddress(nodeIps, subnet, family);
            return ipAddress;
        }

        internal static IPAddress? ResolveIPAddressOrDefault(string? addrOrHost, byte[]? subnet, AddressFamily family)
        {
            var loopback = family == AddressFamily.InterNetwork ? IPAddress.Loopback : IPAddress.IPv6Loopback;

            // 如果地址是空字符串,请列出所有可用的ip地址
            // 上此节点
            if (string.IsNullOrEmpty(addrOrHost))
            {
                return ResolveIPAddressOrDefault(subnet, family);
            }
            else
            {
                // 固定 StreamFilteringTests_SMS 测试
                if (addrOrHost.Equals("loopback", StringComparison.OrdinalIgnoreCase))
                {
                    return loopback;
                }

                // 检查 addrOrHost 是否为有效的 IP 地址，包括环回地址（127.0.0.0/8、::1）和任意地址（0.0.0.0/0、::）。
                if (IPAddress.TryParse(addrOrHost, out var address))
                {
                    return address;
                }

                // 从 DNS 获取IP 地址。 如果 addrOrHost 是本地主机, 将会 
                // 返回回回回回回回回回回回回回回回回回回回IPv4 地址(或如果支持 OS IPv6 的 IPv4 和 IPv6 地址)
                var nodeIps = Dns.GetHostAddresses(addrOrHost);
                return PickIPAddress(nodeIps, subnet, family);
            }
        }

        private static IPAddress? PickIPAddress(IList<IPAddress> nodeIps, byte[]? subnet, AddressFamily family)
        {
            var candidates = new List<IPAddress>();
            foreach (var nodeIp in nodeIps.Where(x => x.AddressFamily == family))
            {
                // 如果子网不匹配, 我们无法解决这个地址。
                // 如果子网没有指定, 请选择最小地址 。
                if (subnet == null)
                {
                    candidates.Add(nodeIp);
                }
                else
                {
                    var ip = nodeIp;
                    if (subnet.Select((b, i) => ip.GetAddressBytes()[i] == b).All(x => x))
                    {
                        candidates.Add(nodeIp);
                    }
                }
            }

            return candidates.Count > 0 ? PickIPAddress(candidates) : null;
        }

        private static IPAddress? PickIPAddress(IReadOnlyList<IPAddress> candidates)
        {
            IPAddress? chosen = null;
            foreach (IPAddress addr in candidates)
            {
                if (chosen == null)
                {
                    chosen = addr;
                }
                else
                {
                    if (CompareIPAddresses(addr, chosen)) // 以确定的方式选择最小地址
                        chosen = addr;
                }
            }
            return chosen;

            // lhs 值为“小”(以某种可重复的意义上)时返回 true
            static bool CompareIPAddresses(IPAddress lhs, IPAddress rhs)
            {
                byte[] lbytes = lhs.GetAddressBytes();
                byte[] rbytes = rhs.GetAddressBytes();

                if (lbytes.Length != rbytes.Length) return lbytes.Length < rbytes.Length;

                // 从最重要的ottet开始比较。
                // 10.68.20.21 < 10.98.05.04
                for (int i = 0; i < lbytes.Length; i++)
                {
                    if (lbytes[i] != rbytes[i])
                    {
                        return lbytes[i] < rbytes[i];
                    }
                }
                // 等同他们
                return false;
            }
        }

        /// <summary>
        /// 获取本地服务器的地址 。
        /// 如果服务器 DNS 记录中正确家族中有多个地址,第一个地址将被返回。
        /// </summary>
        /// <returns>服务器的 IPv4 地址。</returns>
        internal static IPAddress GetLocalIPAddress(AddressFamily family = AddressFamily.InterNetwork, string? interfaceName = null)
        {
            var loopback = (family == AddressFamily.InterNetwork) ? IPAddress.Loopback : IPAddress.IPv6Loopback;
            // 获取所有网络界面列表
            NetworkInterface[] netInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            var candidates = new List<IPAddress>();
            // 通过界面循环
            for (int i = 0; i < netInterfaces.Length; i++)
            {
                NetworkInterface netInterface = netInterfaces[i];

                if (netInterface.OperationalStatus != OperationalStatus.Up)
                {
                    // 跳过不运行的网络界面
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(interfaceName) &&
                    !netInterface.Name.StartsWith(interfaceName, StringComparison.Ordinal)) continue;

                bool isLoopbackInterface = (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback);
                // 从当前接口获取全部 unccast IP 列表
                UnicastIPAddressInformationCollection ipAddresses = netInterface.GetIPProperties().UnicastAddresses;

                // 通过 IP 地址收藏循环
                foreach (UnicastIPAddressInformation ip in ipAddresses)
                {
                    if (ip.Address.AddressFamily == family) // 选择目前所请求家庭的第一个地址。
                    {
                        //除非有人要求我们有一个回回回界面
                        if (!(isLoopbackInterface && ip.Address.Equals(loopback)))
                        {
                            candidates.Add(ip.Address); // 收集所有候选人。
                        }
                    }
                }
            }
            return ResolveLocalIPAddress(candidates, family, interfaceName);
        }

        internal static IPAddress ResolveLocalIPAddress(IReadOnlyList<IPAddress> candidates, AddressFamily family, string? interfaceName)
        {
            if (candidates.Count > 0)
            {
                return PickIPAddress(candidates)!;
            }

            if (string.IsNullOrWhiteSpace(interfaceName))
            {
                return family switch
                {
                    AddressFamily.InterNetwork => IPAddress.Loopback,
                    AddressFamily.InterNetworkV6 => IPAddress.IPv6Loopback,
                    _ => throw new OrleansException("Failed to get a local IP address."),
                };
            }

            throw new OrleansException("Failed to get a local IP address.");
        }

        /// <summary>
        /// 打印 DataConnectionString,
        /// 无需披露任何证书资料
        /// 例如 Azure 存储 AccountKey、 SqlServer 密码或AWS SecretKey。
        /// </summary>
        /// <param name="connectionString">打印的连接字符串。</param>
        /// <returns>字符串表示 DataConnectionString 账户的证书信息已编辑。</returns>
        public static string RedactConnectionStringInfo(string? connectionString)
        {
            string[] secretKeys =
            {
                "AccountKey=",                              // Azure 储存
                "SharedAccessSignature=",                   // 许多 Azure 服务
                "SharedAccessKey=", "SharedSecretValue=",   // ServiceBus
                "Password=",                                // SQL
                "SecretKey=", "SessionToken=",              // DynamoDb
            };
            var mark = "<--SNIP-->";
            if (string.IsNullOrEmpty(connectionString)) return "null";
            //如果连接字符串格式不包含任何 secretKey,则返回仅 <- SNIP-->
            if (!Array.Exists(secretKeys, key => connectionString.Contains(key, StringComparison.Ordinal))) return mark;

            string connectionInfo = connectionString;

            // 从已写入日志文件的连接字符串信息中删除任何密钥
            foreach (var secretKey in secretKeys)
            {
                int keyPos = connectionInfo.IndexOf(secretKey, StringComparison.OrdinalIgnoreCase);
                if (keyPos >= 0)
                {
                    connectionInfo = connectionInfo.Remove(keyPos + secretKey.Length) + mark;
                }
            }

            return connectionInfo;
        }
    }
}
