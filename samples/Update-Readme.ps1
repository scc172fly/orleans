[CmdletBinding()]
param(
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
$samplesRoot = $PSScriptRoot
$manifestPath = Join-Path $samplesRoot 'gallery.json'
$readmePath = Join-Path $samplesRoot 'README.md'
$entries = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$lines = [System.Collections.Generic.List[string]]::new()

$lines.Add('<a id="orleans-samples"></a>')
$lines.Add('')
$lines.Add('# Orleans 示例')
$lines.Add('')
$lines.Add('本目录汇集 Orleans 官方示例。')
$lines.Add('')
$lines.Add('<!-- 由 Update-Readme.ps1 根据 gallery.json 生成。 -->')
$lines.Add('')
$lines.Add('从其他 Microsoft 仓库导入的示例在索引和 `gallery.json` 中保留原仓库地址，原始许可证随示例一并保留。')
$lines.Add('')
$lines.Add('<a id="build-and-validate"></a>')
$lines.Add('')
$lines.Add('## 构建和验证')
$lines.Add('')
$lines.Add('在仓库根目录运行以下命令：')
$lines.Add('')
$lines.Add('```powershell')
$lines.Add('pwsh ./samples/Validate-Samples.ps1')
$lines.Add('```')
$lines.Add('')
$lines.Add('该命令检查示例清单，并构建 `Samples.slnx` 中的所有项目。使用外部云服务的示例只在运行时需要连接这些服务。')
$lines.Add('')
$lines.Add('<a id="featured-samples"></a>')
$lines.Add('')
$lines.Add('## 精选示例')
$lines.Add('')
$lines.Add('| 示例 | 说明 | 原始来源 |')
$lines.Add('| --- | --- | --- |')

foreach ($entry in $entries | Where-Object featured) {
    $sourceLabel = $entry.sourceRepository -replace '^https://github.com/', ''
    $lines.Add("| [$($entry.title)]($($entry.path)) | $($entry.description) | [$sourceLabel]($($entry.sourceRepository)) |")
}

$lines.Add('')
$lines.Add('<a id="all-samples"></a>')
$lines.Add('')
$lines.Add('## 全部示例')
$lines.Add('')
$lines.Add('| 示例 | 说明 | 语言 | 标签 | 原始来源 |')
$lines.Add('| --- | --- | --- | --- | --- |')

foreach ($entry in $entries) {
    $sourceLabel = $entry.sourceRepository -replace '^https://github.com/', ''
    $languages = $entry.languages -join ', '
    $tags = $entry.tags -join ', '
    $lines.Add("| [$($entry.title)]($($entry.path)) | $($entry.description) | $languages | $tags | [$sourceLabel]($($entry.sourceRepository)) |")
}

$content = ($lines -join [Environment]::NewLine) + [Environment]::NewLine

if ($Check) {
    $current = Get-Content -LiteralPath $readmePath -Raw
    if ($current -ne $content) {
        throw 'samples/README.md is out of date. Run samples/Update-Readme.ps1.'
    }

    return
}

[System.IO.File]::WriteAllText($readmePath, $content, [System.Text.UTF8Encoding]::new($false))
