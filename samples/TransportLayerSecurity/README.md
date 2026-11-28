---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans 传输层安全（TLS）示例"
urlFragment: "orleans-transport-layer-security-tls"
description: "演示传输层安全（TLS）的 Orleans 示例。"
---

<a id="orleans-transport-layer-security-tls-sample"></a>

# Orleans 传输层安全（TLS）示例

本示例演示通过双向传输层安全（mTLS）保护的通道通信的客户端和 Silo。

<a id="running-the-sample"></a>

## 运行示例

如果自签名证书不存在，本示例会自动创建。只需运行示例：

```powershell
.\run.cmd
```

或者在 Linux/macOS 上：

```bash
./run.sh
```

示例将：

1. 检查用户证书存储中是否存在名为 `fakedomain.faketld` 的证书
2. 若未找到，则自动创建自签名证书并存入证书存储
3. 启动 Silo 和客户端，并对所有通信使用 TLS

<a id="key-concepts"></a>

## 关键概念

本示例的重点包括：

* 为开发环境自动生成自签名证书
* 配置服务端和客户端，使用双向 TLS 对连接进行身份验证

<a id="certificate-management"></a>

### 证书管理

`TLS.Contracts` 中的 `CertificateHelper` 类负责证书管理：

```csharp
// Automatically ensures a certificate exists (creates if missing)
CertificateHelper.EnsureCertificateExists();
```

<a id="tls-configuration"></a>

### TLS 配置

服务端 [`Program.cs`](./TLS.Server/Program.cs) 中的 `ISiloBuilder.UseTls(...)` 和客户端的 `IClientBuilder.UseTls` 用于配置 TLS：

```csharp
builder.UseTls(
    CertificateHelper.StoreName,
    CertificateHelper.CertificateSubject,
    allowInvalid: isDevelopment,
    CertificateHelper.StoreLocation,
    options =>
    {
        // In this sample there is only one server, however if there are multiple silos then the TargetHost must be set
        // for each connection which is initiated.
        options.OnAuthenticateAsClient = (connection, sslOptions) =>
        {
            sslOptions.TargetHost = CertificateHelper.CertificateSubject;
        };

        if (isDevelopment)
        {
            // NOTE: Do not do this in a production environment
            options.AllowAnyRemoteCertificate();
        }
    })
```

<a id="manual-certificate-management"></a>

## 手动管理证书

<a id="creating-a-certificate-manually-optional"></a>

### 手动创建证书（可选）

如果希望使用 PowerShell 手动创建证书：

```powershell
$cert = New-SelfSignedCertificate -CertStoreLocation Cert:\CurrentUser\My -DnsName "fakedomain.faketld"
```

<a id="removing-the-certificate"></a>

### 删除证书

运行示例后，如需删除自签名证书：

```powershell
Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*fakedomain.faketld*" } | Remove-Item
```

<a id="sample-prerequisites"></a>

## 示例先决条件

本示例使用 C# 编写，以 .NET 10 为目标框架。需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

<a id="building-the-sample"></a>

## 构建示例

若要下载并运行示例，请执行以下步骤：

1. 下载并解压示例。
2. 在 Visual Studio（2022 或更高版本）中：
    1. 在菜单栏中选择**文件** > **打开** > **项目/解决方案**。
    2. 导航到解压后的示例代码所在文件夹，并打开解决方案文件。
    3. 按 <kbd>F5</kbd> 进行调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 无调试运行项目。
3. 通过命令行：
   1. 导航到解压后的示例代码所在文件夹。
   2. 运行 `.\run.cmd`（Windows）或 `./run.sh`（Linux/macOS）。

***注意：*** 在生产环境部署应用时，请务必遵循安全最佳实践。在生产环境使用 CA 签发的证书，并且不要允许无效证书。
