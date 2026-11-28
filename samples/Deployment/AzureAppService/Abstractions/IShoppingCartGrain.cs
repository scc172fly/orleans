// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT License.

namespace Orleans.ShoppingCart.Abstractions;

public interface IShoppingCartGrain : IGrainWithStringKey
{
    /// <summary>
    /// 将数量为 <paramref name="quantity"/> 的对应
    /// <paramref name="product"/> 添加到购物车中。
    /// </summary>
    Task<bool> AddOrUpdateItemAsync(int quantity, ProductDetails product);

    /// <summary>
    /// 从购物车中移除给定的 <paramref name="product" />。
    /// </summary>
    Task RemoveItemAsync(ProductDetails product);

    /// <summary>
    /// 获取购物车中的所有商品。
    /// </summary>
    Task<HashSet<CartItem>> GetAllItemsAsync();

    /// <summary>
    /// 获取购物车中的商品数量。
    /// </summary>
    Task<int> GetTotalItemsInCartAsync();

    /// <summary>
    /// 清空购物车。
    /// </summary>
    Task EmptyCartAsync();
}
