<!-- Azure credential chain guidance -->

> [!TIP]
> <xref:Azure.Identity.DefaultAzureCredential> 可无缝用于本地开发和生产环境。在开发环境中，它使用 Azure CLI 或 Visual Studio 凭据。在 Azure 上的生产环境中，它会自动使用资源的托管标识。为了提高生产环境的性能和可调试性，可考虑将其替换为 <xref:Azure.Identity.ManagedIdentityCredential> 等特定凭据。有关详细信息，请参阅 [DefaultAzureCredential 使用指南](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains#usage-guidance-for-defaultazurecredential)。
