<a id="aws-kinesis-and-dynamodb"></a>

# AWS 基尼西斯和 DynamoDB

本样本运行一个 Orleans silo ,对每个耐久子系统使用AWS服务:

- DynamoDB 分组成员
- DynamoDB grain 状态和 `PubSubStore`
- DynamoDB 提醒
- 亚马孙 Kinesis 数据流
- DynamoDB 基尼西斯流检查站

样本每五秒钟公布一次事件。 默认的 grain 消耗了每个事件,维持了其状态,并登记了一个持久的提醒。

<a id="prerequisites"></a>

## 先决条件

1. 安装 [.NET SDK 斯丁克](https://dotnet.microsoft.com/download) 由存储库选择 `global.json`.
1. 配置 AWS 证书,使用标准 [AWS SDK 证书链](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html)。
1. 创建 Kinesis 数据流 :

   ```shell
   aws kinesis create-stream --stream-name orleans-sample --shard-count 1 --region us-east-1
   aws kinesis wait stream-exists --stream-name orleans-sample --region us-east-1
   ```

样本自动创建其 DynamoDB 表,并随需求开具账单。对于生产部署、通过基础设施提供表格作为代码和禁用自动创建。

<a id="run-the-sample"></a>

## 运行样本

从仓库根 :

```shell
dotnet run --project samples/AWS/KinesisDynamoDB/KinesisDynamoDB.csproj
```

按 <kbd>Ctrl</kbd>+<kbd>C</kbd> 停止。

下列环境变量自定义资源:

| 变量 | 默认默认 | 目的目的宗旨 |
|---|---|---|
| `AWS_REGION` 或 `AWS_DEFAULT_REGION` | `us-east-1` | 基奈西和 DynamoDB 区域  |
| `ORLEANS_KINESIS_STREAM` | `orleans-sample` | 现有的 Kinesis 数据流 |
| `ORLEANS_DYNAMODB_PREFIX` | `OrleansSample` | 全部五个 DynamoDB 表的前缀 |
| `ORLEANS_CLUSTER_ID` | `aws-kinesis-sample` | Orleans 部署识别标识符 |
| `ORLEANS_SERVICE_ID` | `aws-kinesis-sample` | 稳定 Orleans 应用程序标识符 |

使用单独的集束识别码来对同时进行部署的每个单位使用。 当替换部署必须保持 grain 状态、提醒、订阅和检查站时, 使服务标识保持稳定 。

检查站行为和业务指导见 [亚马逊 Kinesis 流](../../../docs/site/src/content/docs/streaming/kinesis-streaming.md) 。
