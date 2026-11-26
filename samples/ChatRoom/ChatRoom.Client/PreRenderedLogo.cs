using System.IO.Compression;
using Spectre.Console;
using Spectre.Console.Rendering;

internal sealed class PreRenderedLogo : Renderable
{
    private const int PixelWidth = 2;
    private const int MaxWidth = 25 * PixelWidth;
    // 条目覆盖1-25 像素, 每像素有两个控制台单元格 。
    private readonly string[] _markup;

    public PreRenderedLogo()
    {
        using var stream = typeof(PreRenderedLogo).Assembly.GetManifestResourceStream("ChatRoom.Client.logo.markup.gz");
        using var compressed = new GZipStream(stream!, CompressionMode.Decompress);
        using var reader = new StreamReader(compressed);
        _markup = reader.ReadToEnd().Split('\f');
    }

    protected override Measurement Measure(RenderOptions options, int maxWidth)
    {
        var width = Math.Min(maxWidth, MaxWidth);
        return new Measurement(width, width);
    }

    protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        if (maxWidth < PixelWidth)
        {
            return [];
        }

        return ((IRenderable)new Markup(_markup[Math.Min(maxWidth, MaxWidth) / PixelWidth - 1])).Render(options, maxWidth);
    }
}
