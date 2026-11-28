using HelloWorld;
using HelloWorld.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;

// 确保证书存储中存在自签名证书（缺少时创建）。
CertificateHelper.EnsureCertificateExists();

// 配置客户端并连接服务。
using var host = new HostBuilder()
    .UseOrleansClient(builder =>
        builder.UseLocalhostClustering()
            .UseTls(CertificateHelper.StoreName, CertificateHelper.CertificateSubject,
                allowInvalid: true,
                CertificateHelper.StoreLocation,
                options =>
                {
                    options.OnAuthenticateAsClient =
                        (connection, sslOptions) =>
                            sslOptions.TargetHost = CertificateHelper.CertificateSubject;

                    // 注意：不要在生产环境中这样做，因为这种做法不安全。
                    options.AllowAnyRemoteCertificate();
                }))
    .UseConsoleLifetime()
    .Build();

await host.StartAsync();
Console.WriteLine("Client successfully connected to silo host");

// 使用已连接的客户端调用 Grain，并将结果输出到终端。
var factory = host.Services.GetRequiredService<IGrainFactory>();
var friend = factory.GetGrain<IHelloGrain>(0);
var response = await friend.SayHello("Good morning, my friend!");
Console.WriteLine($"\n\n{response}\n\n");

Console.WriteLine("Press Enter to exit...");
Console.ReadLine();
