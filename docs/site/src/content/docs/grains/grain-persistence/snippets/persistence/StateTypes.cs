using Orleans;
using Orleans.Runtime;

namespace Orleans.Docs.Snippets.Persistence;

// <profile_state>
[Serializable]
public class ProfileState
{
    public required string Name { get; set; }

    public Date DateOfBirth { get; set; }
}
// </profile_state>

// <cart_state>
[Serializable]
public class CartState
{
    public List<CartItem> Items { get; set; } = new();
}

public class CartItem
{
    public required string ProductId { get; set; }
    public int Quantity { get; set; }
}
// </cart_state>

// 文档中使用的 Date 类型的占位实现。
public struct Date
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
}
