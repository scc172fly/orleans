<a id="microsoft-orleans-transaction-for-dynamodb"></a>

# Microsoft Orleans 交易交易的 DynamoDB

<a id="introduction"></a>

## 简介
Microsoft Orleans 交易交易的 DynamoDB 提供 grain 交易的 Microsoft Orleans 使用亚马逊 DynamoDB.
这样可以确保您的 grains 能够在分布环境中进行交易,使用 DynamoDB 作为基础储存。

分级交易状态和元数据必须符合 DynamoDB400 KB 项限制。 写批数被分割以保持在 DynamoDB每项交易的限额为100项行动和4项受影响物品的甲基溴。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Transactions.DynamoDB
```
