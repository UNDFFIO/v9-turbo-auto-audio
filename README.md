# V9 自动声音切换

迈从 **V9 Turbo+** 放回无线充电底座后，耳机会关机，但电脑上的 2.4G 接收器仍然在线，Windows 往往继续把声音发给耳机。这个小程序读取接收器返回的耳机状态，自动在耳机和扬声器之间切换默认声音输出。

**拿起连接 → 使用耳机；放回关机 → 使用扬声器。**

> 本项目公开源码，**仅限非商业使用**，采用 [PolyForm Noncommercial 1.0.0](LICENSE)。禁止未经许可用于商业目的；这不是允许商用的 OSI 标准开源许可。项目为独立开发，与迈从官方无隶属关系。

## 下载与使用

从 [最新版本](https://github.com/UNDFFIO/v9-turbo-auto-audio/releases/latest) 下载 `V9AutoAudio-v1.0.0-windows-x64.zip`，解压到你准备长期保留、可写入的文件夹，双击 **V9自动声音切换.exe**。

程序在任务栏通知区域显示耳机图标，可能收在隐藏图标菜单里。双击图标打开设置，选择“耳机连接时”和“耳机断开时”的两个输出设备，并勾选“登录 Windows 后自动运行”。默认优先识别 `MCHOSE V9 Turbo+` 与 `Realtek(R) Audio`，其他扬声器可在设置里选择。

首次运行后在这台电脑上生成配置，下载包不包含任何人的设备配置。保留程序文件夹即可；移动文件夹后请重新设置登录自启动。

## 功能

- 直接读取 2.4G 接收器的耳机状态和电量，不依赖迈从 HUB。
- 耳机连接通常约 1 秒内识别；连续三次查询无回复后判定离线，通常约 3～4 秒。
- 同时切换常规、媒体、通信的默认**输出**，保留各设备音量和麦克风设置。
- 托盘运行、暂停与恢复、设备选择、当前用户登录自启动、大小受限的本地日志。
- 同一耳机状态下不会持续覆盖你手动选的设备；读状态出错时保留当前输出并重试。
- 运行期间不访问网络，不修改耳机固件或耳机配置。

## 支持范围

Windows 10/11 **x64**，使用 Windows 自带 .NET Framework。普通用户运行即可，不需要 Python、.NET SDK 或额外驱动。

目前只支持已验证的 **V9 Turbo+ 2.4G 接收器**：USB `VID=3837`、`PID=600A`，HID 用途页 `FF90`、报文编号 `55`。蓝牙模式、其他型号、其他接收器版本不在当前验证范围内。离线也可能来自手动关机、切换到蓝牙或走出无线范围，并不专指放回底座。

应用需要把声音输出设为“系统默认”才能跟随 Windows 切换；已固定输出设备的应用需要先更改它自己的选项。

## 从源码构建

克隆本仓库，在 Windows PowerShell 中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出为 `dist\V9自动声音切换.exe`。脚本使用 Windows 的 .NET Framework C# 编译器，固定为 x64，不下载依赖。完整步骤见 [构建与命令行工具](docs/BUILD.md)。

## 文档

- [使用、暂停、自启动与卸载](docs/USAGE.md)
- [状态协议与切换逻辑](docs/PROTOCOL.md)
- [构建与诊断命令](docs/BUILD.md)
- [实测结果与验证边界](docs/VALIDATION.md)
- [许可说明](docs/LICENSING.md)
- [版本记录](CHANGELOG.md)

## 源码结构

```text
src/
  Program.cs       程序入口、单实例和诊断命令
  Headset.cs       HID 接口筛选与状态读取
  Audio.cs         Windows 默认输出读取、切换、验证和失败回退
  Store.cs         本地配置、日志、状态与登录启动项
  TrayContext.cs   后台检测、状态变化和托盘菜单
  SettingsForm.cs  输出设备与启动设置
build.ps1          本地构建脚本
settings.example.json  不含设备编号的配置示例
docs/              使用与实现说明
LICENSE            PolyForm Noncommercial 1.0.0 许可全文
NOTICE             作者与参考资料声明
LICENSES/          第三方许可声明
```

## 许可与致谢

本项目的原创代码、软件和文档采用 **PolyForm Noncommercial 1.0.0**，仅授予非商业用途的许可；允许在许可范围内使用、研究、修改和分发，分发时必须附带许可和必要声明。具体权利与限制以 [LICENSE](LICENSE) 为准。第三方内容仍遵循其原有许可，见 [NOTICE](NOTICE) 和 [LICENSES](LICENSES)。

耳机状态协议参考 [迈从官方 HUB 状态读取代码](https://cdn.mchose.com.cn/customPage/prod/assets/hidDeviceOptions-Sp1r1Ins.js)；Windows 音频接口布局参考 [AudioDeviceCmdlets](https://github.com/frgnca/AudioDeviceCmdlets)。仓库不包含官方 HUB、厂商固件或它们的打包文件。
