---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans BankAccount ACID 事务"
urlFragment: "orleans-bank-account-acid-transactions"
description: "使用 ACID 事务的 BankAccount 示例。"
---

<a id="orleans-bank-account-with-acid-transactions"></a>

# Orleans 银行账户 ACID 事务

本示例以银行账户为场景，演示如何使用 Orleans 实现 ACID 事务。
其中有两种 Grain：

![在终端中运行的 BankClient 应用](./assets/BankClient.png)

* `AccountGrain` 实现 `IAccountGrain`，模拟具有余额的银行账户。
* `AtmGrain` 实现 `IAtmGrain`，模拟可以在两个银行账户之间转账的自动柜员机（ATM）。

`AtmGrain` 具有以下接口：

```csharp
public interface IAtmGrain : IGrainWithIntegerKey
{
    [Transaction(TransactionOption.Create)]
    Task Transfer(Guid fromAccount, Guid toAccount, uint amountToTransfer);
}
```

`AccountGrain` 具有以下接口：

```csharp
public interface IAccountGrain : IGrainWithGuidKey
{
    [Transaction(TransactionOption.Join)]
    Task Withdraw(uint amount);

    [Transaction(TransactionOption.Join)]
    Task Deposit(uint amount);

    [Transaction(TransactionOption.CreateOrJoin)]
    Task<uint> GetBalance();
}
```

Grain 方法上的 `[Transaction(option)]` 特性告知运行时，这些方法具有事务语义。
`IAtmGrain.Transfer` 方法创建事务，而 `IAccountGrain.Withdraw` 和 `IAccountGrain.Deposit` 方法必须在已有事务的上下文中调用。

`AtmGrain.Transfer(...)` 的实现如下：

```csharp
public async Task Transfer(
    IAccountGrain fromAccount,
    IAccountGrain toAccount,
    uint amountToTransfer)
{
    await Task.WhenAll(
        fromAccount.Withdraw(amountToTransfer),
        toAccount.Deposit(amountToTransfer));
}
```

`Transfer` 方法从一个 `IAccountGrain` 中取出指定金额，并存入另一个账户。Orleans 确保该操作在事务上下文中进行，以保证一致性。

`AccountGrain.Deposit` 方法通过 `ITransactionalState<T>.PerformUpdate` 方法将存入金额增加到账户余额中：

```csharp
public Task Deposit(uint amount) => _balance.PerformUpdate(x => x.Value += amount);
```

实际银行可能允许账户透支，但本示例不允许。`AccountGrain.Withdraw(uint amount)` 通过引发异常来防止透支，从而使事务中止：

```csharp
public Task Withdraw(uint amount) => _balance.PerformUpdate(x =>
{
    if (x.Value < amount)
    {
        throw new InvalidOperationException(
            $"Withdrawing {amount} credits from account \"{this.GetPrimaryKeyString()}\" would overdraw it."
            + $" This account has {x.Value} credits.");
    }

    x.Value -= amount;
});
```

<a id="sample-prerequisites"></a>

## 示例先决条件

本示例使用 C# 编写，目标框架为 .NET 10。需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

<a id="building-the-sample"></a>

## 构建示例

按照以下步骤下载并运行示例：

1. 下载并解压示例。
2. 在 Visual Studio（2022 或更高版本）中：
    1. 在菜单栏中选择 **文件** > **打开** > **项目/解决方案**。
    2. 导航到存放已解压示例代码的文件夹，并打开 C# 项目（.csproj）文件。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 在不调试的情况下运行项目。
3. 从命令行运行：
   1. 导航到存放已解压示例代码的文件夹。
   2. 在命令行输入 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

首先启动 *BankServer* 进程：

``` bash
dotnet run --project BankServer
```

然后启动 *BankClient* 进程：

``` bash
dotnet run --project BankClient
```

客户端会循环在随机账户间发起事务，并输出结果。例如：

```console
We transferred 100 credits from Pasqualino to Ida.
Pasqualino balance: 1500
Ida balance: 1600
```

如果取款会导致账户透支，客户端会输出类似以下的错误：

```console
Error transferring 100 credits from Derick to Xaawo: Transaction 2edc92f5-a94d-4167-9522-fa661cc030ff Aborted because of an unhandled exception in a grain method call. See InnerException for details.
        InnerException: Withdrawing 100 credits from account "Derick" would overdraw it. This account has 0 credits.
```
