# 第三方组件

## windows-ble-hid / BleHid CLI
- 上游：https://github.com/abhishek-raj/windows-ble-hid
- 本地源码基线：v0.7.0，版本号来自开发期间保存的源码目录。
- Copyright (c) 2026 Abhishek Raj
- MIT 完整文本：third_party/windows-ble-hid/LICENSE。
- 包含 CLI/Core/Localization 所需源码与本地扩展，发行包从这些源码重新构建。
- 本地扩展：combo gui+z / gui+shift+z、vtbstatus、vtbselect、vtbhold alt/m/off、vtbdrag。
- vtbdrag 是已搁置实验，不代表 Procreate 画布手势已实现。
- 第三方 MIT 代码仍可按 MIT 使用、修改和分发。项目根目录许可不覆盖其原有授权。

## .NET 与 Windows SDK
发行包为 win-x64 自包含版本，携带 .NET 8 运行时。许可证与第三方声明见 licenses/dotnet/。
Windows SDK .NET 引用包的许可信息及包元数据见 licenses/windows-sdk/。
项目主体的限制性许可不改变上述组件的许可。

Virtual TourBox 是独立项目，与 TourBox、Microsoft、Sony、Nintendo、Savage Interactive 无隶属或官方合作关系。相关名称仅用于描述功能及兼容性。
