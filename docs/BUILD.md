# 构建与命令行工具

## 构建

在 Windows x64 上克隆仓库后执行：

```powershell
git clone https://github.com/UNDFFIO/v9-turbo-auto-audio.git
cd v9-turbo-auto-audio
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本调用 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，编译 `src` 下六个源码文件，引用系统自带的 `System`、`System.Core`、`System.Windows.Forms`、`System.Drawing`、`System.Web.Extensions`，目标为 x64 Windows 窗口程序。

构建不访问网络。默认输出 `dist\V9自动声音切换.exe`，可用 `-OutputDirectory` 指定其他输出目录。发布版打包不包括配置、状态或日志，首次运行时自动生成。

Windows 自带编译器版本和编译时间可能使自行构建文件的 SHA-256 与发布版不同。发布包附带校验文件，原始运行版的验证记录见 [VALIDATION.md](VALIDATION.md)。

## 诊断工具

程序是窗口程序，诊断结果写入 JSON，不显示命令行控制台。通过 `Start-Process -Wait` 等待运行完成。以下命令不会启动另一个常驻托盘实例：

```powershell
$v9Program = Join-Path $PWD 'dist\V9自动声音切换.exe'
Start-Process -FilePath $v9Program -ArgumentList '--diagnose "diagnostics.json"' -Wait
```

| 参数 | 作用 |
| --- | --- |
| `--diagnose "报告路径"` | 读取耳机状态、输出列表、当前三个默认输出、设置和自启动状态 |
| `--switch-headset "报告路径"` | 手动切换到配置中的耳机，报告切换前后输出与验证结果 |
| `--switch-speakers "报告路径"` | 手动切换到备用扬声器并生成报告 |
| `--install-startup "报告路径"` | 写入当前用户登录启动项并报告登记状态 |
| `--remove-startup "报告路径"` | 删除指向当前程序路径的本项目启动项 |
| `--stop` | 请求当前用户会话内运行中的程序退出 |

未指定报告路径时使用程序目录下的 `诊断结果.json`。报告可能包含设备编号和程序路径，只用于本机排查。强制切换命令会实际改变系统默认输出。

无参数启动托盘程序。同一用户会话中重复启动会立即退出，不会产生多个常驻实例。退出、诊断和自启动工具必须在目标用户的实际 Windows 会话中执行。
