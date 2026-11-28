<a id="aws-kinesis-and-dynamodb"></a>

# AWS Kinesis 与 DynamoDB

本示例运行一个 Orleans Silo，其所有持久化子系统均使用 AWS 服务：

- DynamoDB 集群成员资格
- DynamoDB Grain 状态及 `PubSubStore`
- DynamoDB 提醒器
- Amazon Kinesis Data Streams
- DynamoDB Kinesis 流检查点

示例每五秒发布一个事件。一个隐式订阅的 Grain 消费每个事件、持久化其状态，并注册持久化提醒器。

<a id="prerequisites"></a>

## 先决条件

1. 安装仓库 `global.json` 所选定的 [.NET SDK](https://dotnet.microsoft.com/download)。
1. 使用标准 [AWS SDK 凭据链](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html)配置 AWS 凭据。
1. 创建 Kinesis 数据流：

   ```shell
   aws kinesis create-stream --stream-name orleans-sample --shard-count 1 --region us-east-1
   aws kinesis wait stream-exists --stream-name orleans-sample --region us-east-1
   ```

示例自动创建 DynamoDB 表，并采用按需计费。在生产部署中，请通过基础设施即代码预配表，并禁用自动创建。

<a id="run-the-sample"></a>

## 运行示例

在仓库根目录中执行：

```shell
dotnet run --project samples/AWS/KinesisDynamoDB/KinesisDynamoDB.csproj
```

按 <kbd>Ctrl</kbd>+<kbd>C</kbd> 停止运行。

以下环境变量可用于自定义资源：

| 变量 | 默认值 | 用途 |
|---|---|---|
| `AWS_REGION` 或 `AWS_DEFAULT_REGION` | `us-east-1` | Kinesis 与 DynamoDB 所在区域 |
| `ORLEANS_KINESIS_STREAM` | `orleans-sample` | 现有 Kinesis 数据流 |
| `ORLEANS_DYNAMODB_PREFIX` | `OrleansSample` | 所有五个 DynamoDB 表的前缀 |
| `ORLEANS_CLUSTER_ID` | `aws-kinesis-sample` | Orleans 部署标识符 |
| `ORLEANS_SERVICE_ID` | `aws-kinesis-sample` | 稳定的 Orleans 应用程序标识符 |

每个同时运行的部署应使用不同的集群 ID。如果替换部署需要保留 Grain 状态、提醒器、订阅和检查点，请保持服务 ID 稳定。

有关检查点行为及运维指导，请参阅[使用 Amazon Kinesis 处理流](../../../docs/site/src/content/docs/streaming/kinesis-streaming.md)。
