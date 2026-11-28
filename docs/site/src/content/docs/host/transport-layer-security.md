---
title: 使用 TLS 保护 Orleans 连接
description: 为 Orleans Silo 和客户端连接配置仅验证服务器身份的 TLS 或双向 TLS。
ms.date: 08/08/2026
ms.topic: how-to
---

<a id="secure-orleans-connections-with-tls"></a>

# 使用 TLS 保护 Orleans 连接

Orleans 可以使用传输层安全协议 (TLS) 保护客户端到 Silo 及 Silo 间连接。TLS 加密流量，并验证充当 TLS 服务器的终结点身份。双向 TLS (mTLS) 还要求充当 TLS 客户端的终结点提供证书，并验证其身份。

> [!IMPORTANT]
> <xref:Orleans.Connections.Transport.Security.TlsOptions.RemoteCertificateMode> 默认为 `RequireCertificate`。对于 Silo 的入站连接，此默认值要求连接的 Silo 或 Orleans 客户端提供证书。若要配置不需要客户端证书、仅验证服务器身份的 TLS，请在每个 Silo 上显式将其设置为 `NoCertificate`。

TLS API 包含在 Orleans 服务端及客户端包中。

<a id="migrate-tls-configuration"></a>

## 迁移 TLS 配置

TLS 配置及证书加载现在位于 Orleans Core 和 Runtime 包中。从 `Microsoft.Orleans.Connections.Security` 迁移的应用需要按如下方式更新引用：

| 旧 API | 当前 API |
|---|---|
| `Orleans.Hosting.OrleansConnectionSecurityHostingExtensions` 客户端重载 | <xref:Orleans.Hosting.ClientTlsHostingExtensions> |
| `Orleans.Hosting.OrleansConnectionSecurityHostingExtensions` Silo 重载 | <xref:Orleans.Hosting.SiloTlsHostingExtensions> |
| `Orleans.Connections.Security.CertificateLoader` | <xref:Orleans.Connections.Transport.Security.CertificateLoader> |
| `Orleans.Connections.Security` TLS 选项及功能 | `Orleans.Connections.Transport.Security` |

`builder.UseTls(...)` 调用语法保持不变。显式静态调用使用上表中的客户端或 Silo 托管类型。这次包及程序集整合属于二进制 API 变更：针对消息传输 API 重新生成应用及库，并将重新生成的二进制文件一同部署。

<a id="choose-an-authentication-model"></a>

## 选择身份验证模型

| 模型 | Silo 入站策略 | 出站本地证书策略 | 典型边界 |
|---|---|---|---|
| 仅验证服务器身份的 TLS | `RemoteCertificateMode = NoCertificate` | `ClientCertificateMode = NoCertificate` | 客户端已在应用网关或其他可信层完成身份验证 |
| mTLS | `RemoteCertificateMode = RequireCertificate`（默认值） | `ClientCertificateMode = RequireCertificate` | 两端工作负载均需要密码学身份的跨网络直接连接 |

这两个名称相近的选项在不同阶段生效：

- <xref:Orleans.Connections.Transport.Security.TlsOptions.RemoteCertificateMode> 控制对等方证书要求。对于 Silo 入站连接，`RequireCertificate` 要求连接的 Orleans 客户端或 Silo 提供证书，`AllowCertificate` 请求证书但接受证书缺失的情况，`NoCertificate` 不请求客户端证书。对于出站连接，此选项控制 TLS 服务器证书的验证。
- <xref:Orleans.Connections.Transport.Security.TlsOptions.ClientCertificateMode> 控制 Orleans 发起 TLS 连接时本地客户端证书的选择：对于 Silo 是 Silo 间连接，对于 Orleans 客户端是网关连接。

`ClientCertificateMode` 默认为 `AllowCertificate`：当已配置的本地证书适用于客户端身份验证时发送该证书，但也容许本地证书缺失或不适用。将其设置为 `RequireCertificate` 可显式要求出站客户端证书，在没有合适的本地证书时使配置或连接建立失败。

每个 Silo 既接受连接又发起连接。因此，在 mTLS 模式下，Silo 证书需要用于入站连接的服务器身份验证扩展密钥用途 (EKU)，以及用于出站连接的客户端身份验证 EKU。仅验证服务器身份的 TLS 模式下，Silo 证书只需要服务器身份验证 EKU。mTLS 使用的 Orleans 客户端证书需要客户端身份验证 EKU。证书身份、颁发及信任配置应体现工作负载角色，而非在整个集群中复用同一证书和私钥。

TLS 为 Orleans 传输提供机密性、完整性及基于证书的对等方身份验证。它不会对 Grain 调用授权、隔离租户、保护任一进程接收数据后的数据，也不会保护未单独配置的成员资格或存储提供程序流量。可信证书或私钥遭到泄露可能使攻击者冒充该工作负载。

<a id="load-the-local-certificate"></a>

## 加载本地证书

`UseTls` 重载接受具有可访问私钥的 <xref:System.Security.Cryptography.X509Certificates.X509Certificate2>。从部署支持的证书来源加载证书，并在 Orleans 宿主整个生命周期内保持证书未被释放。

<a id="operating-system-certificate-store"></a>

### 操作系统证书存储

当工作负载证书安装在操作系统证书存储中时，Orleans 可以按使用者名称加载证书。以下 Silo 示例搜索当前用户的个人 (`My`) 存储，要求证书当前有效，并配置仅验证服务器身份的 TLS：

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="CertificateStore":::

在隔离的开发环境之外，将 `allowInvalid` 设置为 `false`。证书存储重载要求私钥可访问，并选择适用于工作负载角色的证书。确保证书具有身份验证模型要求的所有 EKU；尤其是 mTLS 使用的 Silo 证书，需要同时具备服务器身份验证及客户端身份验证 EKU。

根据运行进程的身份选择 [CurrentUser](xref:System.Security.Cryptography.X509Certificates.StoreLocation) 或 [LocalMachine](xref:System.Security.Cryptography.X509Certificates.StoreLocation)，并授予该身份访问私钥的权限。如果使用者名称可能匹配多个部署证书，应显式加载预期证书，或使用带有颁发者、指纹或其他部署专用身份检查的证书选择器。

<a id="pkcs12pfx-file"></a>

### PKCS#12/PFX 文件

对于 PKCS#12/PFX 文件，使用 <xref:System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile*>：

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="LoadPkcs12Certificate":::

从受保护的配置或机密提供程序获取路径及密码，而不是从源代码或普通配置文件获取。将文件及其私钥的访问权限限制在工作负载身份范围内。将返回的证书传入以下各节所示的相应 Silo 或客户端 `UseTls` 配置，在宿主运行期间保持证书有效存续，并在宿主停止后释放证书。

<a id="configure-server-authenticated-tls"></a>

## 配置仅验证服务器身份的 TLS

Silo 提供服务器证书。连接的客户端及 Silo 验证证书链、有效期、EKU 及 DNS 名称，但不提供客户端证书。Silo 配置显式禁用入站连接对远程客户端证书的要求，以及出站 Silo 间连接的本地客户端证书。

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="ServerAuthenticatedTls":::

配置不带本地证书的 Orleans 客户端：

:::code language="csharp" source="./snippets/transport-layer-security/csharp/ClientExample/Program.cs" id="ServerAuthenticatedTls":::

<xref:Orleans.Connections.Transport.Security.TlsClientAuthenticationOptions.TargetHost> 必须匹配服务器证书上的 DNS 使用者可选名称 (SAN)。使用客户端访问 Silo 时所用的稳定服务名称，而不是任意证书使用者名称。

<a id="configure-mutual-tls"></a>

## 配置双向 TLS

在 mTLS 模式下，Silo 要求每个入站 Orleans 客户端或 Silo 提供证书，并要求每个出站 Silo 间连接使用本地客户端证书：

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="MutualTls":::

:::code language="csharp" source="./snippets/transport-layer-security/csharp/ClientExample/Program.cs" id="MutualTls":::

未设置 <xref:Orleans.Connections.Transport.Security.TlsOptions.RemoteCertificateValidation> 时，使用平台默认验证。如果提供此回调，应保留常规证书链及名称检查，只添加所需的部署专用策略。无条件返回 `true` 的回调会使对等方身份验证失效。

> [!WARNING]
> <xref:Orleans.Connections.Transport.Security.TlsOptions.AllowAnyRemoteCertificate> 仅适用于隔离的本地开发环境。它接受任意远程证书，因此无法防御身份冒充或主动中间人攻击。

<a id="establish-certificate-trust"></a>

## 建立证书信任

将以下情况视为独立的信任决策：

- **客户端信任 Silo：** Orleans 客户端信任颁发 Silo 服务器证书的 CA。证书 SAN 匹配 `TargetHost`。
- **Silo 信任客户端：** 对于 mTLS，每个 Silo 均信任颁发客户端证书的 CA。如果持有任意公共 CA 颁发的证书不足以构成授权，则使用私有 CA 或附加验证策略。
- **Silo 信任 Silo：** 每个 Silo 验证出站 Silo 间连接中的服务器证书。使用 mTLS 时，每个 Silo 还提供客户端身份验证证书。

限制信任存储范围。如果来自某些根 CA 的任意证书都将被接受为集群身份，则不要将无关的公共或企业根 CA 放入工作负载专用信任集合。网络策略仍应限制 Silo 及网关端口，只允许预期的对等方访问。

<a id="protocols-and-revocation"></a>

## 协议与吊销

TLS 传输协商 `Orleans1` 应用层协议标识符，保持与现有 Orleans 终结点的协议协商兼容性。身份验证回调可以通过消息传输的功能集合访问 <xref:System.Net.Security.SslStream>。

<xref:Orleans.Connections.Transport.Security.TlsOptions.SslProtocols> 默认为 TLS 1.2 和 TLS 1.3。除非互操作性或策略要求缩小协议集合，否则保留这些默认值。Orleans 默认不会启用 TLS 1.0 或 TLS 1.1。

设置 <xref:Orleans.Connections.Transport.Security.TlsOptions.CheckCertificateRevocation>，对 Silo 入站连接的远程证书执行吊销检查。对于出站连接，在 <xref:Orleans.Connections.Transport.Security.TlsOptions.OnAuthenticateAsClient> 中设置 <xref:Orleans.Connections.Transport.Security.TlsClientAuthenticationOptions.CertificateRevocationCheckMode>。启用吊销检查前，确认每个工作负载都能访问证书吊销列表 (CRL) 或联机证书状态协议 (OCSP) 服务，并决定这些服务发生故障时应如何影响可用性。

<a id="rotate-certificates"></a>

## 轮换证书

在部署前规划轮换：

1. 颁发具有相同必需名称及 EKU 的替换证书。
2. 在任何终结点提供新证书之前，将新的颁发链分发到信任存储。
3. 在重叠时间窗口内同时保持新旧证书链有效。
4. 使用替换证书重启进程，或使用 <xref:Orleans.Connections.Transport.Security.TlsOptions.LocalServerCertificateSelector> 和 <xref:Orleans.Connections.Transport.Security.TlsOptions.LocalClientCertificateSelector> 动态选择证书。
5. 确认新连接使用替换证书，然后移除旧证书及过时的信任根。

证书选择器在身份验证期间被调用，但证书加载、缓存、释放及刷新属于应用责任。在正常重连及 Silo 重启行为下测试轮换。在重叠时间窗口结束之前提前发出证书到期告警。

<a id="production-checklist"></a>

## 生产环境检查清单

- 仅向需要私钥的工作负载身份授予私钥文件或密钥存储条目访问权限。
- 优先采用每个工作负载或实例的独立证书，而非一个导出的集群范围私钥。
- 验证 SAN、EKU、证书链信任、有效性及吊销行为。
- 对于仅验证服务器身份的 TLS，在 Silo 上将 `RemoteCertificateMode` 和 `ClientCertificateMode` 设置为 `NoCertificate`。
- 对于 mTLS，在 Silo 上将 `RemoteCertificateMode` 和 `ClientCertificateMode` 设置为 `RequireCertificate`，并为每个连接的 Orleans 客户端配置客户端身份验证证书。
- 即使启用了 TLS，也要使用网络策略保护网关及 Silo 端口。
- 保持时钟同步，因为证书有效性检查依赖时间。
- 监控 TLS 握手故障及证书到期；不要记录私钥或证书密码。
- 将 PFX 密码存入机密存储，而非源代码或普通配置文件。

<a id="see-also"></a>

## 另请参阅

- [Orleans 安全](../security/index.md)
- [网络加固](../security/networking.md)
- <xref:Orleans.Connections.Transport.Security.TlsOptions>
- <xref:Orleans.Hosting.ClientTlsHostingExtensions.UseTls*>
- <xref:Orleans.Hosting.SiloTlsHostingExtensions.UseTls*>
- [客户端配置](configuration-guide/client-configuration.md)
- [服务端配置](configuration-guide/server-configuration.md)
- [.NET TLS/SSL 最佳实践](https://learn.microsoft.com/dotnet/core/extensions/sslstream-best-practices)
- [Orleans 传输层安全 (TLS) 示例](https://learn.microsoft.com/samples/dotnet/samples/orleans-transport-layer-security-tls/)
