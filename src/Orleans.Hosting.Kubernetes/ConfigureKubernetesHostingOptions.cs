using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Orleans.Hosting.Kubernetes
{
    internal class ConfigureKubernetesHostingOptions :
        IConfigureOptions<ClusterOptions>,
        IConfigureOptions<SiloOptions>,
        IPostConfigureOptions<EndpointOptions>,
        IConfigureOptions<KubernetesHostingOptions>
    {
        private readonly IServiceProvider _serviceProvider;

        public ConfigureKubernetesHostingOptions(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Configure(KubernetesHostingOptions options)
        {
            options.Namespace ??= Environment.GetEnvironmentVariable(KubernetesHostingOptions.PodNamespaceEnvironmentVariable) ?? ReadNamespaceFromServiceAccount();
            options.PodName ??= Environment.GetEnvironmentVariable(KubernetesHostingOptions.PodNameEnvironmentVariable) ?? Environment.MachineName;
            options.PodIP ??= Environment.GetEnvironmentVariable(KubernetesHostingOptions.PodIPEnvironmentVariable);
        }

        public void Configure(ClusterOptions options)
        {
            var serviceIdEnvVar = Environment.GetEnvironmentVariable(KubernetesHostingOptions.ServiceIdEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(serviceIdEnvVar))
            {
                options.ServiceId = serviceIdEnvVar;
            }

            var clusterIdEnvVar = Environment.GetEnvironmentVariable(KubernetesHostingOptions.ClusterIdEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(clusterIdEnvVar))
            {
                options.ClusterId = clusterIdEnvVar;
            }
        }

        public void Configure(SiloOptions options)
        {
            var hostingOptions = _serviceProvider.GetRequiredService<IOptions<KubernetesHostingOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(hostingOptions.PodName))
            {
                options.SiloName = hostingOptions.PodName;
            }
        }

        public void PostConfigure(string? name, EndpointOptions options)
        {
            // 使用 PostConfigure 给开发商一个机会,使其有机会使用常规设置 SiloPort 和 GatewayPort
            // 配置方法,无需担心与 UseKubernetesHosting 调用相关的排序。
            if (options.AdvertisedIPAddress is null)
            {
                var hostingOptions = _serviceProvider.GetRequiredService<IOptions<KubernetesHostingOptions>>().Value;
                IPAddress? podIp = null;
                if (hostingOptions.PodIP is not null)
                {
                    podIp = IPAddress.Parse(hostingOptions.PodIP);
                }
                else
                {
                    var hostAddresses = Dns.GetHostAddresses(hostingOptions.PodName!);
                    if (hostAddresses != null)
                    {
                        podIp = IPAddressSelector.PickIPAddress(hostAddresses);
                    }
                }

                if (podIp is not null)
                {
                    options.AdvertisedIPAddress = podIp;
                }
            }

            if (options.SiloListeningEndpoint is null)
            {
                options.SiloListeningEndpoint = new IPEndPoint(IPAddress.Any, options.SiloPort);
            }

            if (options.GatewayListeningEndpoint is null && options.GatewayPort > 0)
            {
                options.GatewayListeningEndpoint = new IPEndPoint(IPAddress.Any, options.GatewayPort);
            }
        }

        private static string? ReadNamespaceFromServiceAccount()
        {
            // 从该库的服务账户中读取命名空间 。
            var serviceAccountNamespacePath = Path.Combine($"{Path.DirectorySeparatorChar}var", "run", "secrets", "kubernetes.io", "serviceaccount", "namespace");
            if (File.Exists(serviceAccountNamespacePath))
            {
                return File.ReadAllText(serviceAccountNamespacePath).Trim();
            }

            return null;
        }

        private static class IPAddressSelector
        {
            // IANNA 私人IPv4 地址
            private static readonly (IPAddress Address, IPAddress SubnetMask)[] PreferredRanges = new[]
            {
                (IPAddress.Parse("192.168.0.0"), IPAddress.Parse("255.255.0.0")),
                (IPAddress.Parse("10.0.0.0"), IPAddress.Parse("255.0.0.0")),
                (IPAddress.Parse("172.16.0.0"), IPAddress.Parse("255.240.0.0")),
            };

            public static IPAddress? PickIPAddress(IReadOnlyList<IPAddress> candidates)
            {
                IPAddress? chosen = null;
                foreach (var address in candidates)
                {
                    if (chosen is null)
                    {
                        chosen = address;
                    }
                    else
                    {
                        if (CompareIPAddresses(address, chosen))
                        {
                            chosen = address;
                        }
                    }
                }
                return chosen;

                // lhs 值为“小”(以某种可重复的意义上)时返回 true
                static bool CompareIPAddresses(IPAddress lhs, IPAddress rhs)
                {
                    var lhsBytes = lhs.GetAddressBytes();
                    var rhsBytes = rhs.GetAddressBytes();

                    if (lhsBytes.Length != rhsBytes.Length)
                    {
                        return lhsBytes.Length < rhsBytes.Length;
                    }

                    // 优于IANA私人IPv4地址范围10x.x.x、192.168x.x、172.16-31。x.x 高于其他地址。
                    if (lhs.AddressFamily is AddressFamily.InterNetwork && rhs.AddressFamily is AddressFamily.InterNetwork)
                    {
                        var lhsPref = GetPreferredSubnetRank(lhs);
                        var rhsPref = GetPreferredSubnetRank(rhs);
                        if (lhsPref != rhsPref)
                        {
                            return lhsPref < rhsPref;
                        }
                    }

                    // 比较从最重要的奥特开始
                    // 10.68.20.21 < 10.98.05.04
                    return lhsBytes.AsSpan().SequenceCompareTo(rhsBytes.AsSpan()) < 0;
                }

                static int GetPreferredSubnetRank(IPAddress ip)
                {
                    var ipBytes = ip.GetAddressBytes();
                    Span<byte> masked = stackalloc byte[ipBytes.Length];
                    var i = 0;
                    foreach (var (Address, SubnetMask) in PreferredRanges)
                    {
                        ipBytes.CopyTo(masked);
                        var subnetMaskBytes = SubnetMask.GetAddressBytes();
                        if (ipBytes.Length != subnetMaskBytes.Length)
                        {
                            continue;
                        }

                        And(ipBytes, subnetMaskBytes, masked);
                        if (masked.SequenceEqual(Address.GetAddressBytes()))
                        {
                            return i;
                        }

                        ++i;
                    }

                    return PreferredRanges.Length;
                    static void And(ReadOnlySpan<byte> lhs, ReadOnlySpan<byte> rhs, Span<byte> result)
                    {
                        Debug.Assert(lhs.Length == rhs.Length);
                        Debug.Assert(lhs.Length == result.Length);

                        for (var i = 0; i < lhs.Length; i++)
                        {
                            result[i] = (byte)(lhs[i] & rhs[i]);
                        }
                    }
                }
            }
        }
    }
}
