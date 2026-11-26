<! -- -- Azure 证书链制导 -- >

> [!TIP]
> <xref:Azure.Identity.DefaultAzureCredential> 可用于本地开发和生产环境。本地开发时，它使用 Azure CLI 或 Visual Studio 凭据。在 Azure 生产环境中，它会自动使用资源的托管标识。为提高生产环境的性能并简化调试，可以改用 <xref:Azure.Identity.ManagedIdentityCredential> 等具体凭据。详情请参阅 [DefaultAzureCredential 使用指南](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains#usage-guidance-for-defaultazurecredential)。
