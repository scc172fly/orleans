using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.LeaseProviders
{
    /// <summary>
    /// 提供使用 Azure 布洛布储存租赁的分布式租赁。
    /// </summary>
    public class AzureBlobLeaseProvider : ILeaseProvider
    {
        private BlobContainerClient? container;
        private readonly AzureBlobLeaseProviderOptions options;
        private BlobServiceClient? blobClient;

        /// <summary>
        /// 初始化 <see cref="AzureBlobLeaseProvider"/> 类的新实例。
        /// </summary>
        /// <param name="options">下 Azure 集团租赁供应商的备选办法。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Microsoft.Extensions.DependencyInjection supplies the registered options instance.")]
        public AzureBlobLeaseProvider(IOptions<AzureBlobLeaseProviderOptions> options)
            : this(options.Value)
        {
        }

        private AzureBlobLeaseProvider(AzureBlobLeaseProviderOptions options)
        {
            this.options = options;
        }

        private async Task InitContainerIfNotExistsAsync()
        {
            if (this.container == null)
            {
                this.blobClient = await options.CreateClient!();
                var tmpContainer = blobClient.GetBlobContainerClient(this.options.BlobContainerName);
                await tmpContainer.CreateIfNotExistsAsync().ConfigureAwait(false);
                this.container = tmpContainer;
            }
        }

#pragma warning disable CA1308 // Persisted lease blob addresses use lowercase category and resource-key components.
        private BlobClient GetBlobClient(string category, string resourceKey) => this.container!.GetBlobClient($"{category.ToLowerInvariant()}-{resourceKey.ToLowerInvariant()}.json");
#pragma warning restore CA1308

        /// <inheritdoc />
        public async Task<AcquireLeaseResult[]> Acquire(string category, LeaseRequest[] leaseRequests)
        {
            ArgumentNullException.ThrowIfNull(leaseRequests);

            await InitContainerIfNotExistsAsync();
            var tasks = new List<Task<AcquireLeaseResult>>(leaseRequests.Length);
            foreach (var leaseRequest in leaseRequests)
            {
                tasks.Add(Acquire(category, leaseRequest));
            }
            //Task.WhenAll 将按所提供任务顺序的数组返回每项任务的结果
            return await Task.WhenAll(tasks);
        }

        private async Task<AcquireLeaseResult> Acquire(string category, LeaseRequest leaseRequest)
        {
            try
            {
                var blobClient = GetBlobClient(category, leaseRequest.ResourceKey);
                //创建此 blob
                await blobClient.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes("blob")), new BlobHttpHeaders { ContentType = "application/json" });
                var leaseClient = blobClient.GetBlobLeaseClient();
                var lease = await leaseClient.AcquireAsync(leaseRequest.Duration);
                return new AcquireLeaseResult(new AcquiredLease(leaseRequest.ResourceKey, leaseRequest.Duration, lease.Value.LeaseId, DateTime.UtcNow), ResponseCode.OK, null);
            }
            catch (RequestFailedException e)
            {
                ResponseCode statusCode;
                //此映射基于以下参考文献 : https://learn.microsoft.com/rest/api/storageservices/blob-service-error-codes
                // https://learn.microsoft.com/rest/api/storageservices/Lease-Blob?redirectedfrom=MSDN
                switch (e.Status)
                {
                    case 404:
                    case 409:
                    case 412: statusCode = ResponseCode.LeaseNotAvailable; break;
                    default: statusCode = ResponseCode.TransientFailure; break;
                }
                return new AcquireLeaseResult(new AcquiredLease(leaseRequest.ResourceKey), statusCode, e);
            }
        }

        /// <inheritdoc />
        public async Task Release(string category, AcquiredLease[] acquiredLeases)
        {
            ArgumentNullException.ThrowIfNull(acquiredLeases);

            await InitContainerIfNotExistsAsync();
            var tasks = new List<Task>(acquiredLeases.Length);
            foreach (var acquiredLease in acquiredLeases)
            {
                tasks.Add(Release(category, acquiredLease));
            }
            await Task.WhenAll(tasks);
        }

        private Task Release(string category, AcquiredLease acquiredLease)
        {
            var leaseClient = GetBlobClient(category, acquiredLease.ResourceKey).GetBlobLeaseClient(acquiredLease.Token);
            return leaseClient.ReleaseAsync();
        }

        /// <inheritdoc />
        public async Task<AcquireLeaseResult[]> Renew(string category, AcquiredLease[] acquiredLeases)
        {
            ArgumentNullException.ThrowIfNull(acquiredLeases);

            await InitContainerIfNotExistsAsync();
            var tasks = new List<Task<AcquireLeaseResult>>(acquiredLeases.Length);
            foreach (var acquiredLease in acquiredLeases)
            {
                tasks.Add(Renew(category, acquiredLease));
            }
            //Task.WhenAll 将按所提供任务顺序的数组返回每项任务的结果
            return await Task.WhenAll(tasks);
        }

        private async Task<AcquireLeaseResult> Renew(string category, AcquiredLease acquiredLease)
        {
            var leaseClient = GetBlobClient(category, acquiredLease.ResourceKey).GetBlobLeaseClient(acquiredLease.Token);

            try
            {
                await leaseClient.RenewAsync();
                return new AcquireLeaseResult(new AcquiredLease(acquiredLease.ResourceKey, acquiredLease.Duration, acquiredLease.Token, DateTime.UtcNow),
                    ResponseCode.OK, null);
            }
            catch (RequestFailedException e)
            {
                ResponseCode statusCode;
                //此映射基于以下参考文献 : https://learn.microsoft.com/rest/api/storageservices/blob-service-error-codes
                // https://learn.microsoft.com/rest/api/storageservices/Lease-Blob?redirectedfrom=MSDN
                switch (e.Status)
                {
                    case 404:
                    case 409:
                    case 412: statusCode = ResponseCode.InvalidToken; break;
                    default: statusCode = ResponseCode.TransientFailure; break;
                }
                return new AcquireLeaseResult(new AcquiredLease(acquiredLease.ResourceKey), statusCode, e);
            }
        }

        /// <summary>
        /// 使用服务供应商提出的指定选项创建一个 Azure Blob租赁提供商。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">配置的租赁提供商的名称 。</param>
        /// <returns>配置好的租赁提供商。</returns>
        public static ILeaseProvider Create(IServiceProvider services, string name)
        {
            AzureBlobLeaseProviderOptions options = services.GetOptionsByName<AzureBlobLeaseProviderOptions>(name);
            return new AzureBlobLeaseProvider(options);
        }
    }
}
