// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Abstractions;

public interface IInventoryGrain : IGrainWithStringKey
{
    ValueTask<int> GetProductCount();

    IAsyncEnumerable<ProductDetails> GetAllProductsAsync();

    ValueTask AddOrUpdateProductAsync(ProductDetails productDetails);

    ValueTask RemoveProductAsync(string productId);
}
