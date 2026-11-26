using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Orleans.Dashboard.Model;
using Orleans.Runtime;

namespace Orleans.Dashboard.Implementation;

/// <summary>
/// 利用反射法从
/// <see cref="ISiloLifecycleSubject"/> ,以便仪表板能够将它们变成
/// 依赖性图。
/// </summary>
/// <remarks>
/// 仪表板没有 <c>InternalsVisibleTo</c> 访问该天
/// 运行时, 所以我们在 <c>SiloLifecycleSubject</c> 和私密状态行走
/// <c>LifecycleSubject</c> 数据形状
/// (<c>List&lt;MonitoredObserver&gt;</c> 排在《公约》 silo 主题,公开
/// <c>Name</c>/<c>Stage</c>/<c>StageName</c> 属性和私人
/// <c>observer</c> 字段)稳定,数据结构已经相同
/// <c>SiloLifecycleSubject.OnStart</c>排放到日志上。
/// </remarks>
internal static class LifecycleStageInspector
{
    private static readonly HashSet<int> NamedStageValues = BuildNamedStageValues();
    private static readonly Func<CancellationToken, Task> NoOpStart = _ => Task.CompletedTask;
    private static readonly Func<CancellationToken, Task> NoOpStop = _ => Task.CompletedTask;

    public static LifecycleStageInfo[] GetStages(ISiloLifecycleSubject? lifecycle)
    {
        if (lifecycle is null)
        {
            return [];
        }

        // silo的生命周期对象保留单独的 `observers` 字段
        // 信息比《公约》更丰富的(清单<MonitoredObserver>)
        // 基 `subscribers` 字段。最好这样做,但返回到基准列表
        // 如果反射失败。
        var monitored = TryReadField<System.Collections.IEnumerable>(lifecycle, "observers");
        if (monitored is not null)
        {
            return BuildStages(EnumerateMonitored(monitored));
        }

        var subscribers = TryReadField<System.Collections.IEnumerable>(lifecycle, "subscribers");
        if (subscribers is not null)
        {
            return BuildStages(EnumerateOrdered(subscribers));
        }

        return [];
    }

    private static LifecycleStageInfo[] BuildStages(IEnumerable<(int Stage, string? StageName, string? Name, object? InnerObserver)> entries)
    {
        return entries
            .GroupBy(e => e.Stage)
            .OrderBy(g => g.Key)
            .Select(group => new LifecycleStageInfo
            {
                Stage = group.Key,
                StageName = group.Select(e => e.StageName).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                    ?? group.Key.ToString(CultureInfo.CurrentCulture),
                IsNamedStage = NamedStageValues.Contains(group.Key),
                Observers = group.Select(e => BuildObserver(e.Name, e.InnerObserver)).ToArray(),
            })
            .ToArray();
    }

    private static IEnumerable<(int Stage, string? StageName, string? Name, object? InnerObserver)> EnumerateMonitored(System.Collections.IEnumerable monitored)
    {
        foreach (var item in monitored)
        {
            if (item is null) continue;
            var type = item.GetType();
            var stage = (int)(type.GetProperty("Stage")?.GetValue(item) ?? 0);
            var stageName = type.GetProperty("StageName")?.GetValue(item) as string;
            var name = type.GetProperty("Name")?.GetValue(item) as string;
            var inner = type.GetField("observer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(item);
            yield return (stage, stageName, name, inner);
        }
    }

    private static IEnumerable<(int Stage, string? StageName, string? Name, object? InnerObserver)> EnumerateOrdered(System.Collections.IEnumerable ordered)
    {
        foreach (var item in ordered)
        {
            if (item is null) continue;
            var type = item.GetType();
            var stage = (int)(type.GetProperty("Stage")?.GetValue(item) ?? 0);
            var inner = type.GetProperty("Observer")?.GetValue(item);
            yield return (stage, null, null, inner);
        }
    }

    private static LifecycleObserverInfo BuildObserver(string? name, object? inner)
    {
        var info = new LifecycleObserverInfo
        {
            Name = name ?? "(unnamed)",
        };

        if (inner is null)
        {
            info.ObserverType = "(disposed)";
            return info;
        }

        // 基 LifecycleSubject 将每个注册的封以内部
        // OrderedObserver. silo 主题包在 MonitoredObserver中再次。
        // 如果反射返回, 解封一个额外的 MonitoredObserver 层。
        if (inner.GetType().Name == "MonitoredObserver")
        {
            inner = inner.GetType().GetField("observer", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(inner) ?? inner;
        }

        // 尝试提取代表目标/方法。
        // 将代表的包装在 LifecycleExtensions 中
        // 私有 `Observer` (与 onStart/onStop 字段)或 `StartupObserver`
        // (在 `_onStart` 字段中, OnStop 是无操作的)
        var innerType = inner.GetType();
        Delegate? onStart = TryReadDelegateField(inner, innerType, "onStart") ?? TryReadDelegateField(inner, innerType, "_onStart");
        Delegate? onStop = TryReadDelegateField(inner, innerType, "onStop") ?? TryReadDelegateField(inner, innerType, "_onStop");

        if (onStart is null && onStop is null && innerType.GetMethod("OnStart") is not null)
        {
            // 具体 ILifecycleObserver 执行。
            info.ObserverType = FormatType(innerType);
            info.HasOnStart = true;
            info.HasOnStop = true;
            info.OnStartMethod = $"{FormatType(innerType)}.OnStart";
            info.OnStopMethod = $"{FormatType(innerType)}.OnStop";
            return info;
        }

        if (onStart is not null)
        {
            info.HasOnStart = !IsNoOp(onStart);
            info.OnStartMethod = FormatDelegate(onStart);
        }

        if (onStop is not null)
        {
            info.HasOnStop = !IsNoOp(onStop);
            info.OnStopMethod = FormatDelegate(onStop);
        }

        info.ObserverType = ResolveOwningType(onStart, onStop) ?? FormatType(innerType);

        return info;
    }

    private static Delegate? TryReadDelegateField(object owner, Type ownerType, string fieldName)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(owner) as Delegate;
    }

    private static bool IsNoOp(Delegate? d)
    {
        // 我们处理编译器生成 `_ => Task.CompletedTask` 和
        // 将无代表作为“不不”代表,让UI能够平暗
        // 边缘。检测编译器关闭是最佳的尝试。
        if (d is null) return true;
        if (ReferenceEquals(d, NoOpStart) || ReferenceEquals(d, NoOpStop)) return true;
        var method = d.Method;
        if (method.DeclaringType is { } declaring && declaring.Name.Contains("<>c", StringComparison.Ordinal))
        {
            // 兰巴达,如果不分析IL 我们无法肯定
            // 当使用生命周期电线时,几乎总是没有机会。
            return true;
        }
        return false;
    }

    private static string? FormatDelegate(Delegate? d)
    {
        if (d is null) return null;
        var method = d.Method;
        var declaring = method.DeclaringType;
        if (declaring is null) return CleanMethodName(method.Name);

        // 关闭时,请步行到可看到的附件类型。
        if (declaring.Name.StartsWith("<>c", StringComparison.Ordinal) && declaring.DeclaringType is not null)
        {
            declaring = declaring.DeclaringType;
        }

        var cleanedMethod = CleanMethodName(method.Name);
        var targetType = d.Target?.GetType();
        if (targetType is not null && targetType != declaring && !targetType.Name.StartsWith("<>", StringComparison.Ordinal))
        {
            return $"{FormatType(targetType)}.{cleanedMethod}";
        }
        return $"{FormatType(declaring)}.{cleanedMethod}";
    }

    /// <summary>
    /// 以羊羔制作的方法名称来绘制编译器生成的前缀
    /// (<c>&lt;Outer&gt;b__N_N</c>)和局部功能
    /// (<c>&lt;Outer&gt;g__Name|N_N</c>。本地功能保留其原始功能
    /// 姓名; 匿名羊羔以 <c>&lt;lambda in Outer&gt;</c>.
    /// </summary>
    private static string CleanMethodName(string methodName)
    {
        if (string.IsNullOrEmpty(methodName) || methodName[0] != '<')
        {
            return methodName;
        }

        // 查找前导“ <” 的匹配 “% > , 计算嵌套
        // 明确互动执行中诸如
        // `<Orleans.ILifecycleParticipant<Orleans.Runtime.ISiloLifecycle>.Participate>g__Name|N`.
        var depth = 0;
        var endOfOuter = -1;
        for (var i = 0; i < methodName.Length; i++)
        {
            var c = methodName[i];
            if (c == '<') depth++;
            else if (c == '>')
            {
                depth--;
                if (depth == 0)
                {
                    endOfOuter = i;
                    break;
                }
            }
        }

        if (endOfOuter < 0)
        {
            return methodName;
        }

        var outer = methodName.AsSpan(1, endOfOuter - 1).ToString();
        // 减少明确接口执行,如
        // `Orleans.ILifecycleParticipant<...>.Participate` 下到仅
        // 后退方法名称( 参与) 。
        var lastDot = LastTopLevelDot(outer);
        if (lastDot >= 0)
        {
            outer = outer[(lastDot + 1)..];
        }

        var suffix = methodName.AsSpan(endOfOuter + 1);
        if (suffix.Length >= 3 && suffix[0] == 'g' && suffix[1] == '_' && suffix[2] == '_')
        {
            // <Outer>g__ 本地Name _N_N - > LocalName
            var rest = suffix[3..];
            var pipe = rest.IndexOf('|');
            var localName = pipe >= 0 ? rest[..pipe] : rest;
            return localName.ToString();
        }

        if (suffix.Length >= 3 && suffix[0] == 'b' && suffix[1] == '_' && suffix[2] == '_')
        {
            // <Outer>b__N_N -> <lambda in Outer>
            return $"<lambda in {outer}>";
        }

        return methodName;
    }

    private static int LastTopLevelDot(string value)
    {
        var depth = 0;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var c = value[i];
            if (c == '>') depth++;
            else if (c == '<') depth--;
            else if (c == '.' && depth == 0)
            {
                return i;
            }
        }
        return -1;
    }

    private static string? ResolveOwningType(Delegate? onStart, Delegate? onStop)
    {
        var d = onStart ?? onStop;
        if (d is null) return null;
        var target = d.Target?.GetType();
        if (target is not null && !target.Name.StartsWith("<>", StringComparison.Ordinal))
        {
            return FormatType(target);
        }
        var declaring = d.Method.DeclaringType;
        if (declaring is null) return null;
        if (declaring.Name.StartsWith("<>c", StringComparison.Ordinal) && declaring.DeclaringType is not null)
        {
            declaring = declaring.DeclaringType;
        }
        return FormatType(declaring);
    }

    [return: NotNullIfNotNull(nameof(type))]
    private static string? FormatType(Type? type)
    {
        if (type is null) return null;
        if (!type.IsGenericType) return type.FullName ?? type.Name;
        var genericArgs = string.Join(", ", type.GetGenericArguments().Select(FormatType));
        var name = type.Namespace is null ? type.Name : $"{type.Namespace}.{type.Name}";
        var tickIndex = name.IndexOf('`', StringComparison.Ordinal);
        if (tickIndex >= 0) name = name[..tickIndex];
        return $"{name}<{genericArgs}>";
    }

    private static T? TryReadField<T>(object owner, string fieldName) where T : class
    {
        var type = owner.GetType();
        while (type is not null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null && field.GetValue(owner) is T value)
            {
                return value;
            }
            type = type.BaseType;
        }
        return null;
    }

    private static HashSet<int> BuildNamedStageValues()
    {
        var values = new HashSet<int>();
        foreach (var field in typeof(ServiceLifecycleStage).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(int))
            {
                values.Add((int)field.GetRawConstantValue()!);
            }
        }
        return values;
    }
}
