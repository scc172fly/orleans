---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Transport Layer Security (TLS) sample"
urlFragment: "orleans-transport-layer-security-tls"
description: "证明运输层安全(TLS)的 Orleans 样本。"
---

<a id="orleans-transport-layer-security-tls-sample"></a>

# Orleans 运输层安全抽样

该样本显示客户和 silo 通过相互运输层安全(mTLS)保障的通道进行通信。

<a id="running-the-sample"></a>

## 运行样本

如果不存在样本, 样本会自动创建自签名证书。 只需运行此样本 :

```powershell
.\run.cmd
```

或 Linux/macOS上:

```bash
./run.sh
```

样本将:

1. 检查用户证书存储中是否存在名为 `fakedomain.faketld` 的证书
2. 如果找不到, 自动创建自签名证书并保存
3. 启动 silo 和客户端使用 TLS 进行所有通信

<a id="key-concepts"></a>

## 关键概念

样本的重要部分是:

* 开发自动自签名证书生成
* 配置服务器和客户端以使用相互 TLS验证连接

<a id="certificate-management"></a>

### 证书管理

下 `CertificateHelper` 类内 `TLS.Contracts` 处理证书管理 :

```csharp
// Automatically ensures a certificate exists (creates if missing)
CertificateHelper.EnsureCertificateExists();
```

<a id="tls-configuration"></a>

### TLS 配置

下 `ISiloBuilder.UseTls(...)` 内 [`Program.cs`](./TLS.Server/Program.cs) 服务器上和 `IClientBuilder.UseTls` 客户端配置 TLS 时:

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

## 手工证书管理

<a id="creating-a-certificate-manually-optional"></a>

### 手动创建证书( 可选择)

如果您选择使用 PowerShell手动创建证书:

```powershell
$cert = New-SelfSignedCertificate -CertStoreLocation Cert:\CurrentUser\My -DnsName "fakedomain.faketld"
```

<a id="removing-the-certificate"></a>

### 删除证书

运行样本后删除自签名证书:

```powershell
Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -like "*fakedomain.faketld*" } | Remove-Item
```

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. Navigate to the folder that holds the unzipped sample code, and open the solution file.
    3. Choose the <kbd>F5</kbd> key to run with debugging, or <kbd>Ctrl</kbd>+<kbd>F5</kbd> keys to run the project without debugging.
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 运行 `.\run.cmd` (窗口) 或 `./run.sh` (林克斯/macOS)。

***注:*** 确保在将您应用到生产环境中时遵循安全最佳做法。使用CA签发的证书,不允许无效的生产证书。
