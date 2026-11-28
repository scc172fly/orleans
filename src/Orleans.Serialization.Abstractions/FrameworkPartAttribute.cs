using System;
using System.ComponentModel;

namespace Orleans.Metadata;

/// <summary>
/// 指定程序集不包含应用程序代码。
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class FrameworkPartAttribute : Attribute
{
}
