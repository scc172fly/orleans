/*
实现说明：

1) 基本思想是通过 Orleans 专用查询读写数据。
   Orleans 读取数据时依赖列名及其类型，写入数据时依赖参数名称及其类型。

2) 实现*必须*保留输入和输出的名称与类型。Orleans 使用这些参数按名称和类型读取查询结果。
   只要保持接口契约，就允许针对数据库厂商及部署环境进行调优，并欢迎贡献。

3) 不同数据库厂商的脚本实现*应当*保留约束名称。通过统一各具体实现中的命名，
   可以简化故障排查。

5) Orleans 的 ETag 是表示唯一版本的不透明列。只要能表示唯一版本，
   具体实现的类型并不重要。本实现使用整数表示版本。

6) 为了明确语义并消除歧义，Orleans 期望某些查询返回大于 0 的值表示 TRUE，
   或返回等于 0 的值表示 FALSE。也就是说，受影响的行数等并不重要。如果发生错误或抛出异常，
   查询*必须*确保整个事务回滚，并可返回 FALSE 或传播异常。
   Orleans 将异常视为失败并重试。

7) 实现遵循 Orleans 扩展成员资格协议。更多信息见：
        https://learn.microsoft.com/dotnet/orleans/implementation/cluster-management
        https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/SystemTargetInterfaces/IMembershipTable.cs

*/
-- 此表定义 Orleans 的操作查询。Orleans 使用它们管理自身操作，
-- 这些是 Orleans 向数据库发出的唯一查询。
-- 只要遵守已声明的接口原则，就可以重新定义这些查询（例如实现非破坏性更新）。
CREATE TABLE OrleansQuery
(
    QueryKey VARCHAR(64) NOT NULL,
    QueryText VARCHAR(8000) NOT NULL,

    CONSTRAINT OrleansQuery_Key PRIMARY KEY(QueryKey)
);
