// Copyright (c) Microsoft. All rights reserved.
// 持麻省理工学院执照

namespace Orleans.ShoppingCart.Abstractions;

public interface ICatalogInitializerGrain : IGrainWithStringKey
{
    Task EnsureSeededAsync(List<ProductDetails> products);
}
