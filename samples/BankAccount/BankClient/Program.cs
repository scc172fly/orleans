using AccountTransfer.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using IHost host = Host.CreateDefaultBuilder(args)
    .UseOrleansClient(client => client.UseLocalhostClustering()
            .UseTransactions())
    .UseConsoleLifetime()
    .Build();

await host.StartAsync();

var client = host.Services.GetRequiredService<IClusterClient>();
var transactionClient = host.Services.GetRequiredService<ITransactionClient>();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

var accountNames = new[] { "Xaawo", "Pasqualino", "Derick", "Ida", "Stacy", "Xiao" };
var random = Random.Shared;

while (!lifetime.ApplicationStopping.IsCancellationRequested)
{
    // 随机选择若干账户进行转账。
    var fromIndex = random.Next(accountNames.Length);
    var toIndex = random.Next(accountNames.Length);
    while (toIndex == fromIndex)
    {
        // 避免在同一个账户之间转账，因为这种操作没有意义。
        toIndex = (toIndex + 1) % accountNames.Length;
    }

    var fromKey = accountNames[fromIndex];
    var toKey = accountNames[toIndex];
    var fromAccount = client.GetGrain<IAccountGrain>(fromKey);
    var toAccount = client.GetGrain<IAccountGrain>(toKey);

    // 执行转账并查询结果。
    try
    {
        var transferAmount = random.Next(200);

        await transactionClient.RunTransaction(
            TransactionOption.Create,
            async () =>
            {
                await fromAccount.Withdraw(transferAmount);
                await toAccount.Deposit(transferAmount);
            });

        var fromBalance = await fromAccount.GetBalance();
        var toBalance = await toAccount.GetBalance();

        Console.WriteLine(
            $"We transferred {transferAmount} credits from {fromKey} to " +
            $"{toKey}.\n{fromKey} balance: {fromBalance}\n{toKey} balance: {toBalance}\n");
    }
    catch (Exception exception)
    {
        Console.WriteLine(
            $"Error transferring credits from " +
            $"{fromKey} to {toKey}: {exception.Message}");

        if (exception.InnerException is { } inner)
        {
            Console.WriteLine($"\tInnerException: {inner.Message}\n");
        }

        Console.WriteLine();
    }

    // 休眠后再次运行。
    await Task.Delay(TimeSpan.FromMilliseconds(200));
}