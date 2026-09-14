# DDR4/DDR5 内存 SPD 信息安全编辑器

基于 [spdrw/spdrw.github.io](https://github.com/spdrw/spdrw.github.io) 串口协议实现的 Windows 桌面版 SPD 编辑工具。

## 功能

- COM 串口连接 SPD 读写器（115200 波特率）
- 读取 / 写入物理芯片 SPD 数据
- 载入 / 备份 BIN 文件
- 修改内存品牌、颗粒制造商、序列号、产品型号、生产日期
- 一键快捷操作（无序列、随机型号/SN、还原初始状态）
- 批量模式：写入成功后 SN 自动递增
- 实时操作日志
- **XMP/EXPO/频率/时序/电压高级编辑**（基于 [ddrxmpeditor-pro](https://github.com/cnns2022/ddrxmpeditor-pro)）
  - DDR4: JEDEC SPD + XMP 2.0 (2 Profiles)
  - DDR5: JEDEC SPD + XMP 3.0 (5 Profiles) + EXPO (2 Profiles)
  - Speed Bin 数据库（DDR4 23 / DDR5 63 档），一键 Apply 填充时序
  - 电压编辑 (VDD/VDDQ/VPP/VMEMCTRL)
  - 命令速率 (1N/2N/3N)、Intel DMB、Realtime OC
  - Profile 名称、CAS Latency 勾选、EXPO 启用开关
  - **杂项 (Misc)**：Form Factor、制造信息、料号、散热片、Bank Groups
  - **XMP Profile 复制**（源 → 目标）
  - **XMP/EXPO Profile 导入/导出**（.bin 文件，DDR5 64B / DDR4 47B / EXPO 40B）
  - CRC-16/XMODEM 自动重算

## 运行

```powershell
dotnet run --project SpdEditor.csproj
```

或直接运行编译产物：

```
bin\Release\net10.0-windows\SpdEditor.exe
```

## 硬件要求

需要配合 SPD 读写器硬件（如 CH341 / 树莓派 Pico 等），通过 COM 口通信。协议帧格式：

```
AA 55 [cmd] [deviceType] [addrHi] [addrLo] 00 [data] [crc8]
```

- `cmd=0x05` 读取 SPD
- `cmd=0x02` 写入单字节
- `deviceType`: 3=256B, 4=512B(DDR4), 5=1024B(DDR5)

## 项目结构

```
Core/
  SpdProtocol.cs         - 串口协议（移植自 spdrw）
  SpdParser.cs           - SPD 数据解析
  SpdEditorLogic.cs      - 字段修改逻辑
  SerialDeviceService.cs - COM 口通信
  JedecManufacturers.cs  - JEDEC 制造商 ID
  Xmp/                   - XMP/EXPO 高级编辑（移植自 ddrxmpeditor-pro）
    SpdUtils.cs          - CRC、电压、时序转换
    SpeedBinService.cs   - Speed Bin 应用
    Ddr4AdvancedModel.cs - DDR4 SPD + XMP 2.0
    Ddr5AdvancedModel.cs - DDR5 SPD + XMP 3.0 + EXPO
    speed_bins.json      - JEDEC Speed Bin 数据库
AdvancedEditorForm.cs  - 高级编辑对话框
MainForm.cs            - 主界面逻辑
MainForm.Designer.cs   - 界面布局
```
