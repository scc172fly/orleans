using System.Diagnostics;
using System.Text;

namespace Orleans.CodeGenerator;

/// <summary>
/// 直接来自排放计划。
/// </summary>
/// <remarks>
/// 生成文件使用四个空格
/// 缩放、 CRLF 线条分隔器、 最后一行之后没有终止者, 以及关闭后一条空白线
/// 除非下一行关闭附件块或继续相同的语句,否则该词将无法被调用。
/// 完整、已经拼写好的 C# 碎片; 作者只拥有线条结构和缩进。
/// </remarks>
internal sealed class SourceWriter
{
    private const string NewLine = "\r\n";
    private readonly StringBuilder _builder;
    private readonly int _initialIndentation;
    private int _indentation;
    private bool _hasLine;
    private bool _blankLinePending;

    /// <param name="capacity">初始字符能力。</param>
    /// <param name="initialIndentation">第一行的缩进,用于一个在深度插入呼叫者的碎片。</param>
    public SourceWriter(int capacity = 1024, int initialIndentation = 0)
    {
        _builder = new StringBuilder(capacity);
        _indentation = _initialIndentation = initialIndentation;
    }

    /// <summary>在当前缩进时开始新行。文本附加于 <see cref="Write(string)"/>。</summary>
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

    /// <summary>写一个空的街区, <c>{</c> 和 <c>}</c> 单行。</summary>
    public void EmptyBlock()
    {
        OpenBlock();
        CloseBlock();
    }

    /// <summary>当下一行继续该语句时, 将关闭支架之后的空白线封住, 如 <c>else</c>。</summary>
    public void ContinueStatement() => _blankLinePending = false;

    /// <summary>要求在下一行之前有一个空线。</summary>
    public void BlankLine() => _blankLinePending = true;

    public void Indent() => _indentation++;

    public void Unindent()
    {
        Debug.Assert(_indentation > 0);
        _indentation--;
    }

    /// <summary>
    /// 写出由另一位作家在目前的缩进时生成的线条;碎片必须从自己的缩进开始。
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
