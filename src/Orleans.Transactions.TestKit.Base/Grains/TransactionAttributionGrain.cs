using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 记录 grain 呼叫的交易归属,但没有明确交易选项。
    /// </summary>
    public class NoAttributionGrain : Grain, INoAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录封存环境交易的 grain 呼叫的交易归属。
    /// </summary>
    public class SuppressAttributionGrain : Grain, ISuppressAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录连接或创建交易的 grain 调用的交易归属。
    /// </summary>
    public class CreateOrJoinAttributionGrain : Grain, ICreateOrJoinAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录创建新交易的 grain 呼叫的交易归属。
    /// </summary>
    public class CreateAttributionGrain : Grain, ICreateAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录需要并加入环境交易的 grain 呼叫的交易归属。
    /// </summary>
    public class JoinAttributionGrain : Grain, IJoinAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录支持环境交易的 grain 呼叫的交易归属。
    /// </summary>
    public class SupportedAttributionGrain : Grain, ISupportedAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    /// <summary>
    /// 记录拒绝环境交易的 grain 电话的交易归属。
    /// </summary>
    public class NotAllowedAttributionGrain : Grain, INotAllowedAttributionGrain
    {
        /// <inheritdoc/>
        public Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            ArgumentNullException.ThrowIfNull(tiers);
            return AttributionGrain.GetNestedTransactionIds(tier, tiers);
        }
    }

    internal static class AttributionGrain
    {
        public static async Task<List<string?>?[]> GetNestedTransactionIds(int tier, List<ITransactionAttributionGrain>[] tiers)
        {
            TransactionInfo? ti = TransactionContext.GetTransactionInfo();
            List<string?>?[] results = new List<string?>[tier + 1 + tiers.Length];
            results[tier] = new List<string?>(new[] { ti?.Id });

            if (tiers.Length == 0)
            {
                return results;
            }

            List<ITransactionAttributionGrain> nextTier = tiers.FirstOrDefault()!;
            List<ITransactionAttributionGrain>[] nextTiers = tiers.Skip(1).ToArray();
            List<string?>?[][] tiersResults = await Task.WhenAll(nextTier.Select(g => g.GetNestedTransactionIds(tier + 1, nextTiers)));
            foreach (List<string?>?[] result in tiersResults)
            {
                if (result.Length != results.Length) throw new ApplicationException("Invalid result length");
                for (int i = tier + 1; i < results.Length; i++)
                {
                    if (results[i] != null)
                    {
                        results[i]!.AddRange(result[i]!);
                    }
                    else
                        results[i] = result[i];
                }
            }

            return results;
        }
    }
}
