[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'dist'
}
$v9Compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $v9Compiler -PathType Leaf)) {
    throw '未找到 x64 .NET Framework C# 编译器。请在 Windows x64 上安装或启用 .NET Framework 后重试。'
}

$v9Sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File |
    Sort-Object Name | ForEach-Object { $_.FullName })
if ($v9Sources.Count -ne 6) { throw '源码文件不完整，应包含 src 下的六个 C# 文件。' }

$v9OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $v9OutputDirectory -Force | Out-Null
$v9Executable = Join-Path $v9OutputDirectory 'V9自动声音切换.exe'
$v9CompilerArguments = @(
    '/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
    ('/out:' + $v9Executable)
) + $v9Sources

& $v9Compiler @v9CompilerArguments
if ($LASTEXITCODE -ne 0) { throw ('C# 编译失败，退出码：' + $LASTEXITCODE) }
if (-not (Test-Path -LiteralPath $v9Executable -PathType Leaf)) { throw '编译器未生成程序。' }

Write-Output ('构建完成：' + $v9Executable)
