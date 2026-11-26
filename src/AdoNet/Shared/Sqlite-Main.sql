/*
执行情况说明:

(1) 通常的想法是,数据是通过 Orleans 具体查询读写。
   Orleans 在读取时以列名和类型操作,在写作时以参数名和类型操作。

(2) 执行 *必须* 保留输入和输出名称及类型。 Orleans 使用这些参数来读取查询结果, 以名称和类型分类 。
   允许供应商和部署方面的具体调试,只要接口合同,捐款就得到鼓励
   维持。

(3) 执行供应商具体文字 *应在* 保存限制名称。这简化了排除故障
   具体实施之间统一命名。

5) Orleans 的ETag是一个不透明的列,代表一个独特的版本。
   只要它代表一个独有版本, 不重要。 在此执行中我们使用整数来进行编译

(6) 为了明确和排除含糊不清之处, Orleans 期望某些查询返回 TRUE 值 >0 的 TRUE

   或 FALSE 值=0。也就是说,受影响的行或这样无关紧要。如果出现错误或提出例外
   查询 *必须* 确保整个交易被回落,可以退回FALSE或宣传例外。
   Orleans 将例外作为失败处理,并将重试。

7) 执行遵循扩大 Orleans 成员议定书。
        https://learn.microsoft.com/dotnet/orleans/implementation/cluster-management
        https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/SystemTargetInterfaces/IMembershipTable.cs
*/

-- 这些设置通过更好地将读者与作家分开,减少锁定,从而改进数据库的吞吐量。
-- SQL 2012年服务器和新服务器可以自称为CURRENT。 老年人需要工作变通。
CREATE TABLE IF NOT EXISTS OrleansQuery
(
    QueryKey  TEXT PRIMARY KEY,
    QueryText TEXT NOT NULL
);
