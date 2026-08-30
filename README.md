# 商业超体：价值与定位

## 产品简介

商业超体 V1.0 - AI智能商业诊断与价值定位引擎

基于 .NET 8 + WPF 构建的智能商业诊断工具，采用 Copilot 伴随式对话模式，通过多轮深度对话帮助用户挖掘商业底牌、构建护城河、锁定精准定位。

## 技术架构

- **框架**: .NET 8.0 + WPF
- **架构模式**: MVVM
- **UI风格**: 暗黑极客风
- **核心依赖**:
  - CommunityToolkit.Mvvm - MVVM框架
  - Microsoft.Extensions.DependencyInjection - 依赖注入
  - Newtonsoft.Json - JSON处理
  - Markdig - Markdown渲染
  - Serilog - 日志记录

## 功能特性

### 核心模块

1. **多轮"老中医"把脉引擎**
   - 内置预设的系统级Prompt提问链
   - 探索独占资源 → 逼问隐性痛点 → 确认履约边界

2. **实时信息提炼器**
   - AI后台静默分析用户对话
   - 自动提取"价值标签"
   - 实时更新商业画布

3. **降维对标与护城河构建**
   - 当诊断达到80%阈值时激活
   - 生成竞品对比分析

4. **终极商业蓝图生成**
   - 一句话超级签名
   - 信任构建SOP
   - 交付模式建议
   - 支持导出为 PDF / Markdown / HTML 三种格式

### 容错机制

- 话题偏离纠正：AI温柔拉回机制
- 断点续传：自动保存会话进度

## 项目结构

```
商业超体价值与定位/
├── Models/           # 数据模型
├── ViewModels/        # 视图模型 (MVVM)
├── Views/             # 视图层
├── Services/          # 业务服务层
├── Converters/        # 数据转换器
├── Resources/         # 资源文件
└── App.xaml          # 应用程序入口
```

## 构建与运行

### 环境要求

- .NET 8.0 SDK
- Visual Studio 2022/2026 或 VS Code

### 编译

```bash
cd d:\CSharpProjects\商业超体：价值与定位
dotnet restore
dotnet build
```

### 运行

```bash
dotnet run --project src\商业超体价值与定位\商业超体价值与定位.csproj
```

## 配置说明

首次运行时，程序会在 `%LOCALAPPDATA%\商业超体\` 目录下创建配置文件：

- `llm_config.json` - LLM API配置
- `session.json` - 会话保存

### LLM配置

支持配置国内大模型（DeepSeek/Kimi）的API：

```json
{
    "Provider": "DeepSeek",
    "ApiKey": "your-api-key",
    "BaseUrl": "https://api.deepseek.com/v1",
    "Model": "deepseek-chat",
    "MaxTokens": 2000,
    "Temperature": 0.7
}
```

## 使用说明

1. **启动应用** - 双击运行程序
2. **开始诊断** - 点击"新会话"开始商业诊断
3. **对话交互** - 在左侧与AI顾问进行多轮对话
4. **观察画布** - 右侧商业画布会实时更新
5. **生成蓝图** - 当完成度达到80%时，可生成最终商业蓝图
6. **导出文档** - 将商业蓝图导出为 PDF / Markdown / HTML 三种格式

## License

Copyright © 2026 商业超体
