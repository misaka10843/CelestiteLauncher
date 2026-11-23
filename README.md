# Celestite [![GitHub Release](https://img.shields.io/github/v/release/Kengxxiao/CelestiteLauncher)](https://github.com/Kengxxiao/CelestiteLauncher/releases)
![GitHub License](https://img.shields.io/github/license/Kengxxiao/CelestiteLauncher)
![GitHub last commit](https://img.shields.io/github/last-commit/Kengxxiao/CelestiteLauncher)
[![GitHub Downloads](https://img.shields.io/github/downloads/Kengxxiao/CelestiteLauncher/total)](https://github.com/Kengxxiao/CelestiteLauncher/releases)
[![Build Status](https://dev.azure.com/Kengxxiao/Celestite-opensource/_apis/build/status%2FKengxxiao.CelestiteLauncher?branchName=main)](https://dev.azure.com/Kengxxiao/Celestite-opensource/_build/latest?definitionId=5&branchName=main)

**Celestite** 是一款跨平台的第三方 **DMMGamePlayer** 实现，提供了基础的启动器功能和一些额外的高级特性，包括但不限于：  
* 在日本以外的国家/地区使用 **DMMGamePlayer** 的功能[^1]。
* 自定义游戏启动命令行。
* 在非 Windows 系统上使用 Proton/Wine 等 Windows 兼容层启动游戏。
* 从导航栏打开 DMM 的 GAMES 任务官网。
* 以无 GUI 模式执行游戏启动流程。
* 浏览并搜索客户端和网页游戏目录，将可入库的游戏添加到账号。网页游戏使用系统默认浏览器打开。
  
[^1]: 该项功能不包含在开源代码中，需要在 [Releases](https://github.com/Kengxxiao/CelestiteLauncher/releases) 中下载以获得该项功能支持。

同时，**Celestite** 兼容 **DMMGamePlayer** 的数据格式，允许用户执行以下操作：
* 在 **Celestite** 上直接使用 **DMMGamePlayer** 的登录态。
* 共享 **DMMGamePlayer** 中已经下载完成的游戏。

## 使用
要使用 **Celestite**，建议您在 [Releases](https://github.com/Kengxxiao/CelestiteLauncher/releases) 中下载对应系统的最新版本。  
由于启动器本体不包含更新检查功能，因此建议您在 Github 上定期检查是否有针对 BUG 的软件更新。

使用登录页的 **DMM 网页授权登录** 登录。登录页和设置页都可以保存启动器代理，支持 HTTP、HTTPS 和 SOCKS5。外部浏览器使用自己的代理设置。

Windows 会为当前系统用户加密保存网页授权。会话有效时，重新打开启动器会自动登录。会话过期、被 DMM 撤销或在 Celestite 中退出账号后，需要重新授权。

Windows 下可运行 `Celestite.Desktop.exe --register-dmm-protocol`，将当前用户的 DMM 网页授权回调和游戏链接关联到这份 Celestite。

启用游戏代理选项后，Celestite 会将 `HTTP_PROXY`、`HTTPS_PROXY` 和 `ALL_PROXY` 传给它直接启动的非提权游戏进程。游戏需要支持这些环境变量才能使用代理。

## 高级启动
可以使用一些特定的命令行参数使 **Celestite** 自动执行某些功能。
* ```--nogui``` 启用无 GUI 模式，该模式在运行完成后自动退出。
* 无 GUI 模式使用仍有效的已保存会话。网页授权用户可向正在运行且已登录的 Celestite 传递游戏启动指令。
* ```--productId <productId> --type <type>``` 要求 **Celestite** 直接启动某个游戏。如果 **Celestite** 未在后台运行，那么启动指令将会在登录成功后开始执行；反之，启动指令将会被传递给正在运行的 **Celestite** 实例执行。
### 示例
* ```Celestite.Desktop.exe --productId mementomori --type GCL``` 向 Celestite 发送启动 MementoMori 的指令。

## 手动发版

在 GitHub 仓库的 **Actions** 中选择 **Release Celestite**，点击 **Run workflow**，选择分支并填写新的版本标签（例如 `v2.0.0`）。流程会构建 Windows x64 的 .NET 10 Native AOT 包，并将 ZIP 上传到对应的 GitHub Release。
