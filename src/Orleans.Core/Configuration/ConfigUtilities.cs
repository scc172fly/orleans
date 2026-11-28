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
    /// 配置相关的实用工具类。
    /// </summary>
    public static class ConfigUtilities
    {
        // 时间间隔以十进制数字字符串输入，其后可选带有单位字符串：“ms”、“s”、“m”、“hr”。
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
                unitSize = 1000 * 10000; // 默认单位为秒。
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

            // 如果地址为空字符串，则直接枚举
            // 此节点上的所有可用 IP 地址。
            if (string.IsNullOrEmpty(addrOrHost))
            {
                return ResolveIPAddressOrDefault(subnet, family);
            }
            else
            {
                // 修复 StreamFilteringTests_SMS 测试。
                if (addrOrHost.Equals("loopback", StringComparison.OrdinalIgnoreCase))
                {
                    return loopback;
                }

                // 检查 addrOrHost 是否为有效 IP 地址，包括环回地址（127.0.0.0/8、::1）及任意地址（0.0.0.0/0、::）。
                if (IPAddress.TryParse(addrOrHost, out var address))
                {
                    return address;
                }

                // 从 DNS 获取 IP 地址。如果 addrOrHost 为 localhost，则
                // 返回环回 IPv4 地址（如果操作系统支持 IPv6，则返回 IPv4 和 IPv6 地址）。
                var nodeIps = Dns.GetHostAddresses(addrOrHost);
                return PickIPAddress(nodeIps, subnet, family);
            }
        }

        private static IPAddress? PickIPAddress(IList<IPAddress> nodeIps, byte[]? subnet, AddressFamily family)
        {
            var candidates = new List<IPAddress>();
            foreach (var nodeIp in nodeIps.Where(x => x.AddressFamily == family))
            {
                // 如果子网不匹配，则无法解析此地址。
                // 如果未指定子网，则以确定性方式选取最小的地址。
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
                    if (CompareIPAddresses(addr, chosen)) // 以确定性方式选取最小的地址。
                        chosen = addr;
                }
            }
            return chosen;

            // 如果 lhs 按某种可重复的比较规则小于 rhs，则返回 true。
            static bool CompareIPAddresses(IPAddress lhs, IPAddress rhs)
            {
                byte[] lbytes = lhs.GetAddressBytes();
                byte[] rbytes = rhs.GetAddressBytes();

                if (lbytes.Length != rbytes.Length) return lbytes.Length < rbytes.Length;

                // 从最高有效八位组开始比较。
                // 10.68.20.21 < 10.98.05.04
                for (int i = 0; i < lbytes.Length; i++)
                {
                    if (lbytes[i] != rbytes[i])
                    {
                        return lbytes[i] < rbytes[i];
                    }
                }
                // 两者相等。
                return false;
            }
        }

        /// <summary>
        /// 获取本地服务器的地址。
        /// 如果服务器的 DNS 记录中存在多个属于正确地址族的地址，则返回第一个。
        /// </summary>
        /// <returns>服务器的 IPv4 地址。</returns>
        internal static IPAddress GetLocalIPAddress(AddressFamily family = AddressFamily.InterNetwork, string? interfaceName = null)
        {
            var loopback = (family == AddressFamily.InterNetwork) ? IPAddress.Loopback : IPAddress.IPv6Loopback;
            // 获取所有网络接口的列表。
            NetworkInterface[] netInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            var candidates = new List<IPAddress>();
            // 遍历网络接口。
            for (int i = 0; i < netInterfaces.Length; i++)
            {
                NetworkInterface netInterface = netInterfaces[i];

                if (netInterface.OperationalStatus != OperationalStatus.Up)
                {
                    // 跳过未处于工作状态的网络接口。
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(interfaceName) &&
                    !netInterface.Name.StartsWith(interfaceName, StringComparison.Ordinal)) continue;

                bool isLoopbackInterface = (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback);
                // 从当前接口获取所有单播 IP 地址的列表。
                UnicastIPAddressInformationCollection ipAddresses = netInterface.GetIPProperties().UnicastAddresses;

                // 遍历 IP 地址集合。
                foreach (UnicastIPAddressInformation ip in ipAddresses)
                {
                    if (ip.Address.AddressFamily == family) // 目前选择所请求地址族中的第一个地址，后续需要重新考虑此策略。
                    {
                        // 除非明确要求环回接口，否则不选择环回地址。
                        if (!(isLoopbackInterface && ip.Address.Equals(loopback)))
                        {
                            candidates.Add(ip.Address); // 收集所有候选地址。
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
        /// 输出 DataConnectionString，
        /// 但不泄露任何凭据信息，
        /// 例如 Azure Storage AccountKey、SQL Server 密码或 AWS SecretKey。
        /// </summary>
        /// <param name="connectionString">要输出的连接字符串。</param>
        /// <returns>已隐去帐户凭据信息的 DataConnectionString 的字符串表示形式。</returns>
        public static string RedactConnectionStringInfo(string? connectionString)
        {
            string[] secretKeys =
            {
                "AccountKey=",                              // Azure Storage
                "SharedAccessSignature=",                   // 多种 Azure 服务
                "SharedAccessKey=", "SharedSecretValue=",   // ServiceBus
                "Password=",                                // SQL
                "SecretKey=", "SessionToken=",              // DynamoDb
            };
            var mark = "<--SNIP-->";
            if (string.IsNullOrEmpty(connectionString)) return "null";
            // 如果连接字符串格式不包含任何 secretKey，则仅返回 <--SNIP-->。
            if (!Array.Exists(secretKeys, key => connectionString.Contains(key, StringComparison.Ordinal))) return mark;

            string connectionInfo = connectionString;

            // 从写入日志文件的连接字符串信息中移除所有密钥。
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
