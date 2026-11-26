---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans BankAccount ACID transactions"
urlFragment: "orleans-bank-account-acid-transactions"
description: "例如,使用ACID交易的 BankAccount 。"
---

<a id="orleans-bank-account-with-acid-transactions"></a>

# Orleans 与ACIDS交易的银行账户

本样本表明如何使用银行账户假想情况 Orleans 实施ACID交易。
有两种 grains:

![在终端运行的银行服务程序](./assets/BankClient.png)

* `AccountGrain`执行 `IAccountGrain`,模拟银行账户的余额。
* `AtmGrain`执行 `IAtmGrain`,模拟自动取款机,允许两个银行账户之间的转账。

`AtmGrain` 具有此接口 :

```csharp
public interface IAtmGrain : IGrainWithIntegerKey
{
    [Transaction(TransactionOption.Create)]
    Task Transfer(Guid fromAccount, Guid toAccount, uint amountToTransfer);
}
```

`AccountGrain` 具有此接口 :

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

下 `[Transaction(option)]` 上 grain 方法告诉运行时间这些方法是交易的。
`IAtmGrain.Transfer` 方法产生交易,而 `IAccountGrain.Withdraw` 方法和 `IAccountGrain.Deposit` 方法则必须在现有交易中使用。

`AtmGrain.Transfer(...)` 执行情况如下:

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

`Transfer` 方法从一个 `IAccountGrain` 中提取指定数额并将其存入另一个。 Orleans 确保交易中出现这种情况,以确保一致性。

`AccountGrain.Deposit` 方法使用 `ITransactionalState<T>.PerformUpdate` 方法将交存的金额加入账户余额:

```csharp
public Task Deposit(uint amount) => _balance.PerformUpdate(x => x.Value += amount);
```

实际银行允许超额放款账户,但本样本没有这样做。 `AccountGrain.Withdraw(uint amount)` 通过提出例外条款防止过度放款,导致交易中止:

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

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. 打开解压后的示例目录，再打开其中的 C# 项目文件（`.csproj`）。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 直接运行。
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 在命令行,类型 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

开始 *BankServer* 进程

``` bash
dotnet run --project BankServer
```

然后启动 *BankClient* 进程

``` bash
dotnet run --project BankClient
```

客户端将循环在随机账户之间发布交易,打印结果。 例如 :

```console
We transferred 100 credits from Pasqualino to Ida.
Pasqualino balance: 1500
Ida balance: 1600
```

当提取账户时会超支, 客户端将打印错误, 如:

```console
Error transferring 100 credits from Derick to Xaawo: Transaction 2edc92f5-a94d-4167-9522-fa661cc030ff Aborted because of an unhandled exception in a grain method call. See InnerException for details.
        InnerException: Withdrawing 100 credits from account "Derick" would overdraw it. This account has 0 credits.
```
