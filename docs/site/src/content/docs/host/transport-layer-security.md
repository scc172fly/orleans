---
title: "与 TLS 连接的安全 Orleans"
description: "配置服务器认证 TLS 或 Orleans silo 和客户端连接的双向 TLS 。"
ms.date: 08/08/2026
ms.topic: how-to
---

<a id="secure-orleans-connections-with-tls"></a>

# 与 TLS 连接的安全 Orleans

Orleans 能够保护客户端到silo 和 silo- 来...silo 与运输层安全(TLS)连接。 TLS加密通信,认证端点作为 TLS 服务器。mTLS还要求并认证作为 TLS 客户端的终点。

> [!IMPORTANT]
> <xref:Orleans.Connections.Transport.Security.TlsOptions.RemoteCertificateMode> 默认为 `RequireCertificate`。对于 Silo 的入站连接，此设置要求连接方 Silo 或 Orleans 客户端出示证书。若只验证 TLS 服务端身份，请在每个 Silo 上明确设为 `NoCertificate`。

TRS API 包含在 Orleans 服务器和客户包中。

<a id="migrate-tls-configuration"></a>

## 迁移 TLS 配置

TLS 配置和证书装入现位于 Orleans 核心包和运行时间包中。从 `Microsoft.Orleans.Connections.Security` 迁移到该软件的应用更新其参考文献如下:

| [前一个 API | 当前 API |
|---|---|
| `Orleans.Hosting.OrleansConnectionSecurityHostingExtensions` 客户端超载 | <xref:Orleans.Hosting.ClientTlsHostingExtensions> |
| `Orleans.Hosting.OrleansConnectionSecurityHostingExtensions` silo 超载 | <xref:Orleans.Hosting.SiloTlsHostingExtensions> |
| `Orleans.Connections.Security.CertificateLoader` | <xref:Orleans.Connections.Transport.Security.CertificateLoader> |
| `Orleans.Connections.Security` TLS 选项和特性 | `Orleans.Connections.Transport.Security` |

下 `builder.UseTls(...)` 保留了调用语法。 明确静态呼叫使用客户端或 silo 以上所列主机主机类型。此软件包和组装合并是一个二进制 API 改变:重建应用程序和图书馆,

<a id="choose-an-authentication-model"></a>

## 选择认证模式

| 型 型 | 入 silo 政策 | 向外的当地认证政策 | 典型边界 |
|---|---|---|---|
| 服务器认证 TLS | `RemoteCertificateMode = NoCertificate` | `ClientCertificateMode = NoCertificate` | 客户端已在应用程序网关或其他信任层验证 |
| mTLS | `RemoteCertificateMode = RequireCertificate` (默认) | `ClientCertificateMode = RequireCertificate` | 整个网络的直接连接,其中两个工作量都需要加密身份 |

两种类似名称的备选办法在不同阶段适用:

- <xref:Orleans.Connections.Transport.Security.TlsOptions.RemoteCertificateMode> controls peer certificate requirements. On inbound silo connections, `RequireCertificate` requires a certificate from the connecting Orleans client or silo, `AllowCertificate` requests one and accepts its absence, and `NoCertificate` omits client-certificate requests. On outbound connections, this option governs validation of the TLS server's certificate.
- <xref:Orleans.Connections.Transport.Security.TlsOptions.ClientCertificateMode> controls selection of the local client certificate whenever Orleans initiates a TLS connection: silo-to-silo connections for a silo and gateway connections for an Orleans client.

`ClientCertificateMode` 默认值为 `AllowCertificate`: 当配置的本地证书对客户端验证有效时, 发送该证书, 但允许丢失或不合适的本地证书。 设置为 `RequireCertificate` , 使外限要求清晰明了, 当没有适当的本地证书时配置或连接设置失败 。

每一 silo 接受和启动连接。 for mTLS, a silo 因此证书需要输入连接的服务器验证扩展密钥使用(EKU),外出连接的客户身份验证 EKU。 silo 证书只需要服务器验证。 Orleans 客户端证书 mTLS 证书身份、发放和信任应反映工作量的作用,而不是在整个组群中重复使用一份证书和私人钥匙。

TLS为 Orleans 运输提供保密、完整和基于证书的同行认证。它不授权 grain 电话,孤立租户,在接收数据后保护数据,或确保成员/存储供应商交通安全,除非这些供应商是单独配置的。 折中信任证书或私人钥匙可以让攻击者冒充工作量。

<a id="load-the-local-certificate"></a>

## 装入本地证书

`UseTls` 超载接受一个有可访问的私人密钥的 <xref:System.Security.Cryptography.X509Certificates.X509Certificate2> 。从您部署支持的证书源中装入,并在 Orleans 主机存在期间将其保存为未处理 。

<a id="operating-system-certificate-store"></a>

### 操作系统证书存储

当工作量证书安装在操作系统证书存储处时, Orleans 可以按主题名称装载。以下 silo 示例搜索当前用户的个人(`My`)存储,要求当前证书有效,并配置服务器认证的 TLS :

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="CertificateStore":::

设定 `allowInvalid` 至 `false` 仓库超载要求有一个无障碍的私人密钥,并选择适合工作量角色的证书。 确保选定的证书拥有认证模式所要求的每一所EKU;特别是 silo 使用证书的 mTLS 既需要服务器验证,也需要客户身份验证。

根据运行该过程的身份选择 [CurrentUser](xref:System.Security.Cryptography.X509Certificates.StoreLocation) 或 [LocalMachine](xref:System.Security.Cryptography.X509Certificates.StoreLocation) ,并给予该身份进入私人密钥。如果一个主题名称可以匹配多个部署证书,则将指定的证书装入或使用证书选择器使用发行人、缩略图或其他特定部署的身份证检查。

<a id="pkcs12pfx-file"></a>

### PKCS#12/PFX文件

PKCS#12/PFX文件使用 <xref:System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile*>:

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="LoadPkcs12Certificate":::

获取来自受保护配置或秘密提供者的路径和密码, 而不是源代码或普通配置文件。 限制对文件及其私人密钥的访问, 使其进入工作量身份。 将返回的证书传递给以下各节显示的有关 silo 或客户 `UseTls` 配置, 在主机运行时保持其生命力并在主机停止后处置它 。

<a id="configure-server-authenticated-tls"></a>

## 配置服务器认证 TLS

silo 提供服务器证书。连接客户端和 silos 验证其链条、有效期、 EKU 和 DNS 名称,但不提交客户端证书。 silo 配置明确禁用了入境连接的远程证书和出境 silo- -silo 连接的本地客户端证书。

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="ServerAuthenticatedTls":::

配置没有本地证书的 Orleans 客户端 :

:::code language="csharp" source="./snippets/transport-layer-security/csharp/ClientExample/Program.cs" id="ServerAuthenticatedTls":::

<xref:Orleans.Connections.Transport.Security.TlsClientAuthenticationOptions.TargetHost> must match a DNS Subject Alternative Name (SAN) on the server certificate. Use the stable service name clients use to reach the silos, not an arbitrary certificate subject.

<a id="configure-mutual-tls"></a>

## 配置彼此的 TLS

对于 mTLS, silos 要求每个入境的 Orleans 客户或 silo 出具证书,并要求每一个出境 silo-至silo 连接都出具本地客户证书:

:::code language="csharp" source="./snippets/transport-layer-security/csharp/SiloExample/Program.cs" id="MutualTls":::

:::code language="csharp" source="./snippets/transport-layer-security/csharp/ClientExample/Program.cs" id="MutualTls":::

默认平台验证在 <xref:Orleans.Connections.Transport.Security.TlsOptions.RemoteCertificateValidation> 不设置时适用。 如果您提供回调, 请保持正常的链条和名称检查, 并只添加您所需的具体部署策略。 返回 `true` 的回调会无条件地破坏对等认证 。

> [!WARNING]
> <xref:Orleans.Connections.Transport.Security.TlsOptions.AllowAnyRemoteCertificate> 仅适用于隔离的本地开发环境。它会接受任何远程证书，因而无法防止身份冒充或主动中间人攻击。

<a id="establish-certificate-trust"></a>

## 建立证书信任

将上述决定作为单独的信托决定处理:

- **客户端信任 silos:** 签发 silo 服务器证书的 CA 由 Orleans 客户信任。 SAN 证书与 `TargetHost`匹配。
- **Silos 信托客户:** 对于 mTLS,为客户签发证书的CA由每个 silo信任。如果拥有任意公共证书不足以获得授权,则使用私人CA或附加验证政策。
- **Silos 信任信托基金 silos:** 每一 silo 外出验证服务器证书 silo- 来...silo 连接的 mTLS, 每个 silo 并提交客户认证证书。

保持信任商店的狭义。 当来自这些信托的证书被接受为集群身份时,不要将无关的公共或公司根植于工作量特定的信任包中。 网络政策仍应将 silo 和网关港口限制在预期同行手中。

<a id="protocols-and-revocation"></a>

## 协议和撤销

TLS 运输工具谈判了 `Orleans1` 应用程序- 程序协议标识符, 与现有的 Orleans 端点保持协议谈判。 认证回调可以通过信件传输的特性收集访问 <xref:System.Net.Security.SslStream> 。

<xref:Orleans.Connections.Transport.Security.TlsOptions.SslProtocols> defaults to TLS 1.2 and TLS 1.3. Retain those defaults unless an interoperability or policy requirement calls for a narrower set. Orleans doesn't enable TLS 1.0 or TLS 1.1 by default.

设定 <xref:Orleans.Connections.Transport.Security.TlsOptions.CheckCertificateRevocation> 输入时检查远程证书 silo 连接。对于外向连接,设置 <xref:Orleans.Connections.Transport.Security.TlsClientAuthenticationOptions.CertificateRevocationCheckMode> 内 <xref:Orleans.Connections.Transport.Security.TlsOptions.OnAuthenticateAsClient>在允许进行撤销检查之前,核实所有工作量都可达到证书撤销清单或在线证书状况协议服务,并决定停业应如何影响可用性。

<a id="rotate-certificates"></a>

## 旋转证书

部署前计划轮调:

1. 签发与要求的姓名和工伤和工伤股相同的替换证书。
2. 在任何终点提交新证书之前,将新的发行链分配给信托商店。
3. 使新旧链在重叠窗口中有效。
4. 与替换证书重新启动进程,或使用 <xref:Orleans.Connections.Transport.Security.TlsOptions.LocalServerCertificateSelector> 和 <xref:Orleans.Connections.Transport.Security.TlsOptions.LocalClientCertificateSelector> 动态选择证书。
5. 确认新的连接使用替换,然后删除旧证书和过时的信任根。

验证过程中会调用证书选择器, 但证书装入、 缓存、 处置和刷新是应用程序的责任。 在正常重新连接和 silo 重新启动行为下测试旋转。 提醒在重叠窗口关闭前提前过期 。

<a id="production-checklist"></a>

## 生产清单

- 仅给予需要的工作身份, 给予私人钥匙文件或密钥存储条目 。
- 最好对每个工作量或每例一次的单独证书,超过一个出口的全集群私人钥匙。
- 验证 SANs、 EKUs、链信任、有效性和撤销行为。
- 服务器认证 TLS, 设置 `RemoteCertificateMode` 和 `ClientCertificateMode` 至 `NoCertificate` 日期 时 silos.
- 用于 mTLS, 设置 `RemoteCertificateMode` 和 `ClientCertificateMode` 至 `RequireCertificate` 日期 时 silos 并配置每个连接上的客户端认证证书 Orleans 客户端。
- 即使启用 TLS , 也仍然保护网关和有网络政策的 silo 端口。
- 保持时钟同步, 因为证书有效性检查取决于时间 。
- 监测 TLS 握手失败和证书过期; 不记录私人密钥或证书密码 。
- 将PFX密码存放在秘密仓库,而不是源文件或普通配置文件。

<a id="see-also"></a>

## 另请参阅

- [Orleans 安全](../security/index.md)
- [网络加固](../security/networking.md)
- <xref:Orleans.Connections.Transport.Security.TlsOptions>
- <xref:Orleans.Hosting.ClientTlsHostingExtensions.UseTls*>
- <xref:Orleans.Hosting.SiloTlsHostingExtensions.UseTls*>
- [客户配置](configuration-guide/client-configuration.md)
- [服务器配置](configuration-guide/server-configuration.md)
- [.NET TLS/SSL最佳做法](https://learn.microsoft.com/dotnet/core/extensions/sslstream-best-practices)
- [Orleans 运输层安全抽样](https://learn.microsoft.com/samples/dotnet/samples/orleans-transport-layer-security-tls/)
