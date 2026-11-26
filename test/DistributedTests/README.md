<a id="distributed-tests"></a>

# 分布式测试

本目录中的项目是 刻度工作量, 不是 `dotnet test` 测试
存储库级别 [`distributed-tests.yml`](../../distributed-tests.yml)
文件定义工作量和情景。

<a id="prerequisites"></a>

## 先决条件

- Windows 。 当前 克朗配置启动 Windows 应用程序主机
  (`DistributedTests.Server.exe` 和 `DistributedTests.Client.exe`)。
- 下 .NET SDK 由存储库选择 `global.json`.
- docker 桌面或另一个与 Dock 兼容的运行时间
  [紫紫](https://learn.microsoft.com/azure/storage/common/storage-use-azurite),
  除非使用 Azure 储存账户。
- 当地可用的港口 `5010`、 `11111`和 `30000` 加上港口 `10000`,
  `10001`,以及使用Azurite时的 `10002` 。

使用 [响声](https://github.com/dotnet/crank) 控制器和代理安装
同一版本:

```powershell
dotnet tool install --global Microsoft.Crank.Controller --version "0.2.0-*"
dotnet tool install --global Microsoft.Crank.Agent --version "0.2.0-*"
```

如果工具之一已经安装, 则将 `install` 替换为 `update`。

<a id="run-locally"></a>

## 本地运行本地

除非另有说明, 否则从仓库根运行全部命令 。

<a id="1-start-azurite"></a>

### 1. 开始阿祖罗特

分布式测试使用 Azure 表格显示组群成员和 Azure 队列
每种情况所需的控制频道。
也使用队列来协调工作量。
CI使用版本和服务端点:

```powershell
docker run --detach --rm --name orleans-distributed-tests-azurite `
  --publish 10000:10000 `
  --publish 10001:10001 `
  --publish 10002:10002 `
  mcr.microsoft.com/azure-storage/azurite:3.35.0 `
  azurite `
  --blobHost 0.0.0.0 `
  --queueHost 0.0.0.0 `
  --tableHost 0.0.0.0 `
  --skipApiVersionCheck
```

`local` 配置文件通过每个回回回的队列和表格端点
。 循环后端点选择 Azurite 的
`UseDevelopmentStorage=true` 连接字符串,因此没有证书或
需要连接字符串配置 。

<a id="2-build-the-workloads"></a>

### 2. 工作量的形成

克朗配置默认值为 `net10.0` ,并读取已建应用程序
`Artifacts\DistributedTests`发自:

```powershell
dotnet build test\DistributedTests\DistributedTests.Server\DistributedTests.Server.csproj --configuration Release --framework net10.0
dotnet build test\DistributedTests\DistributedTests.Client\DistributedTests.Client.csproj --configuration Release --framework net10.0
```

在改变 Orleans 或分配测试工作量之后重建两个项目。

<a id="3-start-the-crank-agent"></a>

### 3. 启动克兰代理商

启动代理器,在单独的终端中运行并保持运行:

```powershell
crank-agent --url http://localhost:5010
```

本地配置将服务器、客户端和混乱代理任务发送到此
端点:它还将服务器工作量减少到一个当地实例;
非本地方案定义默认为 10 个服务器实例 。

<a id="4-run-a-scenario"></a>

### 4. 运行一种假想

在另一个终端中运行 :

```powershell
crank --config .\distributed-tests.yml --scenario ping --profile local
```

现有情景是 `ping`、 `fanout`、 `streaming`、 `reliability`,以及
`rolling`. 其工作量大小和期限在
[`distributed-tests.yml`](../../distributed-tests.yml),而且有些运行
几分钟

当运行完成后,停止 克兰克代理和阿祖特:

```powershell
docker stop orleans-distributed-tests-azurite
```

<a id="use-an-azure-storage-account"></a>

## 使用 Azure 存储账户

要使用 Azure 存储而不是 Azurite, 保存其 crank 的本地配置文件
代理代理端点并推翻其存储变量:

```powershell
$account = "<storage-account-name>"
crank --config .\distributed-tests.yml --scenario ping --profile local `
  --variable "azureQueueUri=https://$account.queue.core.windows.net" `
  --variable "azureTableUri=https://$account.table.core.windows.net"
```

`TENANT_ID` 和 `CLIENT_ID` 被确定时,工作量进程认证
使用 `ClientAssertionCredential`的非循环后端点; 否则它们会使用
`DefaultAzureCredential`. 配置代理服务器主机上选定的证书
并允许该身份许可在
目标账户。

<a id="troubleshooting"></a>

## 故障排查

- **克洛报告 `http://localhost:5010` 无法使用**:开始
  关闭代理,并核实没有其他程序使用端口 `5010`。
- **源文件夹或可执行文件丢失了**:在
  `Release` 用于 `net10.0` 并确认该可执行文件存在于
  `Artifacts\DistributedTests\<workload>\net10.0`.
- **工作量无法与储存连接**:确认Azurite容器
  正在运行, 端口 `10001` 和 `10002` 是可达的 。
