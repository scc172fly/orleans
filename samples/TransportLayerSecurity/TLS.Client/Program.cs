using HelloWorld;
using HelloWorld.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography.X509Certificates;

// 确保存在自签名证书( 如果缺少的话, 创建一个)
CertificateHelper.EnsureCertificateExists();

// 配置客户端并连接到服务 。
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

                    // 注:在生产环境中不要这样做,因为不安全。
                    options.AllowAnyRemoteCertificate();
                }))
    .UseConsoleLifetime()
    .Build();

await host.StartAsync();
Console.WriteLine("Client successfully connected to silo host");

// 使用连接的客户端调用 grain,将结果写入终端。
var factory = host.Services.GetRequiredService<IGrainFactory>();
var friend = factory.GetGrain<IHelloGrain>(0);
var response = await friend.SayHello("Good morning, my friend!");
Console.WriteLine($"\n\n{response}\n\n");

Console.WriteLine("Press Enter to exit...");
Console.ReadLine();
