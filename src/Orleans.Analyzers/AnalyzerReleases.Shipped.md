## Release 3.3.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ORLEANS0003  | Usage   | Error  | 从 Grain继承继承 

## Release 7.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ORLEANS0001  | Usage   | Error  | [AlwaysInterleave] 只能用于 grain 接口方法,而不能用于 grain 类方法
ORLEANS0002  | Usage   | Error  | 不允许引用参数修改器
ORLEANS0004  | Usage   | Error  | 添加序列化 [和NonSerialized]属性
ORLEANS0005  | Usage   | Info   | 添加[GenerateSerializer属性为 [可排 类型
ORLEANS0006  | Usage   | Error  | 无法对摘要/已登记财产进行序列排序
ORLEANS0007  | Usage   | Error  | 
ORLEANS0008  | Usage   | Error  | Grain 接口不能具有属性
ORLEANS0009  | Usage   | Error  | Grain 接口方法必须返回兼容型号
ORLEANS0010  | Usage   | Info   | 添加缺失的 [lias] 属性
ORLEANS0011  | Usage   | Error  | [ Alis] 属性必须是声明类型所独有的
ORLEANS0012  | Usage   | Error  | [Id]属性必须是声明类型中每个成员独有的属性
ORLEANS0013  | Usage   | Error  | 此属性不应用于 grain 执行

### Removed Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|--------------------
ORLEANS0003  | Usage   | Error  | 从 Grain继承继承 
