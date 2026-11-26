// <transactions_client_program>
using IHost host = Host.CreateDefaultBuilder(args)
    .UseOrleansClient((_, client) =>
    {
        client.UseLocalhostClustering()
            .UseTransactions();
    })
    .Build();

await host.StartAsync();

var client = host.Services.GetRequiredService<IClusterClient>();
var transactionClient= host.Services.GetRequiredService<ITransactionClient>();

var accountNames = new[] { "Xaawo", "Pasqualino", "Derick", "Ida", "Stacy", "Xiao" };
var random = Random.Shared;

while (!Console.KeyAvailable)
{
    // 选择一些随机账户来换钱
    var fromIndex = random.Next(accountNames.Length);
    var toIndex = random.Next(accountNames.Length);
    while (toIndex == fromIndex)
    {
        // 避免向同一账户转帐/转出同一账户,因为这样做毫无意义
        toIndex = (toIndex + 1) % accountNames.Length;
    }

    var fromKey = accountNames[fromIndex];
    var toKey = accountNames[toIndex];
    var fromAccount = client.GetGrain<IAccountGrain>(fromKey);
    var toAccount = client.GetGrain<IAccountGrain>(toKey);

    // 执行传输并查询结果
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

    // 睡着再跑
    await Task.Delay(TimeSpan.FromMilliseconds(200));
}
// </transactions_client_program>
