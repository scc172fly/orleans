using HelloWorld;
using HelloWorld.Grains;
using HelloWorld.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

// 确保存在自签名证书( 如果缺少的话, 创建一个)
CertificateHelper.EnsureCertificateExists();

await new HostBuilder()
    .UseEnvironment(Environments.Development)
    .UseOrleans((ctx, builder) =>
    {
        var isDevelopment = ctx.HostingEnvironment.IsDevelopment();
        builder
            .UseLocalhostClustering()
            .UseTls(
                CertificateHelper.StoreName,
                CertificateHelper.CertificateSubject,
                allowInvalid: isDevelopment,
                CertificateHelper.StoreLocation,
                options =>
                {
                    // 在此样本中只有一个 silo,但如果有多个 silos ,则必须设定 TargetHost
                    // 启动的每个连接。
                    options.OnAuthenticateAsClient = (connection, sslOptions) => sslOptions.TargetHost = CertificateHelper.CertificateSubject;

                    if (isDevelopment)
                    {
                        // 注:在生产环境中不这样做
                        options.AllowAnyRemoteCertificate();
                    }
                });
    })
    .ConfigureLogging(logging => logging.AddConsole())
    .RunConsoleAsync();
