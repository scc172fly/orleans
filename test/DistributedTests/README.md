<a id="distributed-tests"></a>

# 分布式测试

本目录中的项目是 Crank 工作负载，并非 `dotnet test` 测试项目。仓库级别的
[`distributed-tests.yml`](../../distributed-tests.yml)
文件定义了工作负载与测试场景。

<a id="prerequisites"></a>

## 先决条件

- Windows。当前 Crank 配置会启动 Windows 应用宿主程序
  （`DistributedTests.Server.exe` 和 `DistributedTests.Client.exe`）。
- 使用仓库的 `global.json` 所选定的 .NET SDK。
- Docker Desktop 或其他 Docker 兼容的运行时，用于运行
  [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)，
  除非使用 Azure Storage 账户。
- 本地端口 `5010`、`11111` 和 `30000` 可用；使用 Azurite 时还需要端口 `10000`、
  `10001` 和 `10002` 可用。

安装相同版本的 [Crank](https://github.com/dotnet/crank) 控制器和代理：

```powershell
dotnet tool install --global Microsoft.Crank.Controller --version "0.2.0-*"
dotnet tool install --global Microsoft.Crank.Agent --version "0.2.0-*"
```

如果其中任一工具已安装，将 `install` 替换为 `update`。

<a id="run-locally"></a>

## 在本地运行

除非另有说明，所有命令均从仓库根目录运行。

<a id="1-start-azurite"></a>

### 1. 启动 Azurite

分布式测试使用 Azure Tables 管理集群成员资格，并使用 Azure Queues 实现所有测试场景都需要的控制通道。
可靠性和滚动测试场景也使用队列协调工作负载。请使用与 CI 相同的 Azurite 版本及服务终结点：

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

`local` 配置文件将环回队列和表服务终结点传递给各个工作负载。环回终结点会选择 Azurite 的
`UseDevelopmentStorage=true` 连接字符串，因此无须配置凭据或连接字符串。

<a id="2-build-the-workloads"></a>

### 2. 构建工作负载

Crank 配置默认使用 `net10.0`，并从 `Artifacts\DistributedTests` 读取构建后的应用：

```powershell
dotnet build test\DistributedTests\DistributedTests.Server\DistributedTests.Server.csproj --configuration Release --framework net10.0
dotnet build test\DistributedTests\DistributedTests.Client\DistributedTests.Client.csproj --configuration Release --framework net10.0
```

修改 Orleans 或分布式测试工作负载后，请重新构建这两个项目。

<a id="3-start-the-crank-agent"></a>

### 3. 启动 Crank 代理

在独立的终端中启动代理，并保持其运行：

```powershell
crank-agent --url http://localhost:5010
```

本地配置文件将服务器、客户端及混沌代理作业发送到此终结点。
它还将服务器工作负载缩减为一个本地实例；非本地场景定义默认使用十个服务器实例。

<a id="4-run-a-scenario"></a>

### 4. 运行测试场景

在另一个终端中运行：

```powershell
crank --config .\distributed-tests.yml --scenario ping --profile local
```

可用场景为 `ping`、`fanout`、`streaming`、`reliability` 和 `rolling`。
工作负载规模与运行时长由 [`distributed-tests.yml`](../../distributed-tests.yml) 定义，其中某些场景会运行数分钟。

运行完成后，停止 Crank 代理与 Azurite：

```powershell
docker stop orleans-distributed-tests-azurite
```

<a id="use-an-azure-storage-account"></a>

## 使用 Azure Storage 账户

若要使用 Azure Storage 而非 Azurite，请保留本地配置文件的 Crank 代理终结点，并覆盖其存储变量：

```powershell
$account = "<storage-account-name>"
crank --config .\distributed-tests.yml --scenario ping --profile local `
  --variable "azureQueueUri=https://$account.queue.core.windows.net" `
  --variable "azureTableUri=https://$account.table.core.windows.net"
```

设置 `TENANT_ID` 和 `CLIENT_ID` 时，工作负载进程使用 `ClientAssertionCredential` 对非环回终结点进行身份验证；
否则使用 `DefaultAzureCredential`。请在代理主机上配置所选凭据，并授予该标识在目标账户中创建和使用队列与表的权限。

<a id="troubleshooting"></a>

## 故障排查

- **Crank 报告 `http://localhost:5010` 不可用**：启动 Crank 代理，并确认没有其他进程正在使用端口 `5010`。
- **源目录或可执行文件缺失**：以 `Release` 配置、`net10.0` 目标框架构建两个工作负载，并确认可执行文件存在于
  `Artifacts\DistributedTests\<workload>\net10.0`。
- **工作负载无法连接到存储**：确认 Azurite 容器正在运行，并且端口 `10001` 和 `10002` 可访问。
