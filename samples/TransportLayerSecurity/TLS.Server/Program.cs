using HelloWorld;
using HelloWorld.Grains;
using HelloWorld.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

// 确保证书存储中存在自签名证书（缺少时创建）。
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
                    // 此示例中只有一个 Silo，但如果存在多个 Silo，则必须为发起的每个连接
                    // 设置 TargetHost。
                    options.OnAuthenticateAsClient = (connection, sslOptions) => sslOptions.TargetHost = CertificateHelper.CertificateSubject;

                    if (isDevelopment)
                    {
                        // 注意：不要在生产环境中这样做。
                        options.AllowAnyRemoteCertificate();
                    }
                });
    })
    .ConfigureLogging(logging => logging.AddConsole())
    .RunConsoleAsync();
