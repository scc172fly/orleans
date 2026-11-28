using System.Diagnostics;
using System.Text;

namespace Orleans.CodeGenerator;

/// <summary>
/// 直接根据生成计划写出 C# 代码。
/// </summary>
/// <remarks>
/// 生成文件使用四个空格的
/// 缩进、CRLF 换行符，最后一行没有终止换行符，并在右大括号之后保留一个空行，
/// 除非下一行关闭外层块或继续同一条语句。调用方提供
/// 完整且已生成文本的 C# 片段；写入器仅负责行结构和缩进。
/// </remarks>
internal sealed class SourceWriter
{
    private const string NewLine = "\r\n";
    private readonly StringBuilder _builder;
    private readonly int _initialIndentation;
    private int _indentation;
    private bool _hasLine;
    private bool _blankLinePending;

    /// <param name="capacity">初始字符容量。</param>
    /// <param name="initialIndentation">第一行缩进，用于调用方在此深度插入的片段。</param>
    public SourceWriter(int capacity = 1024, int initialIndentation = 0)
    {
        _builder = new StringBuilder(capacity);
        _indentation = _initialIndentation = initialIndentation;
    }

    /// <summary>按当前缩进开始新的一行，使用 <see cref="Write(string)"/> 追加文本。</summary>
    public SourceWriter StartLine()
    {
        if (_hasLine)
        {
            _builder.Append(NewLine);
            if (_blankLinePending)
            {
                _builder.Append(NewLine);
            }
        }

        _hasLine = true;
        _blankLinePending = false;
        _builder.Append(' ', _indentation * 4);
        return this;
    }

    public SourceWriter Write(string text)
    {
        _builder.Append(text);
        return this;
    }

    public SourceWriter Write(char value)
    {
        _builder.Append(value);
        return this;
    }

    public void WriteLine(string text) => StartLine().Write(text);

    public void OpenBlock()
    {
        WriteLine("{");
        _indentation++;
    }

    public void CloseBlock()
    {
        Debug.Assert(_indentation > 0);
        _blankLinePending = false;
        _indentation--;
        WriteLine("}");
        _blankLinePending = true;
    }

    /// <summary>写出空块，将 <c>{</c> 和 <c>}</c> 放在不同行。</summary>
    public void EmptyBlock()
    {
        OpenBlock();
        CloseBlock();
    }

    /// <summary>当下一行继续当前语句（例如 <c>else</c>）时，取消右大括号后的空行。</summary>
    public void ContinueStatement() => _blankLinePending = false;

    /// <summary>请求在下一行之前插入一个空行。</summary>
    public void BlankLine() => _blankLinePending = true;

    public void Indent() => _indentation++;

    public void Unindent()
    {
        Debug.Assert(_indentation > 0);
        _indentation--;
    }

    /// <summary>
    /// 按当前缩进写入另一写入器产生的各行；片段必须以其自身的缩进开头。
    /// </summary>
    public void WriteFragment(string fragment)
    {
        if (fragment.Length == 0)
        {
            return;
        }

        if (_hasLine)
        {
            _builder.Append(NewLine);
            if (_blankLinePending)
            {
                _builder.Append(NewLine);
            }
        }

        _hasLine = true;
        _blankLinePending = false;
        _builder.Append(fragment);
    }

    public override string ToString()
    {
        Debug.Assert(_indentation == _initialIndentation);
        return _builder.ToString();
    }
}
