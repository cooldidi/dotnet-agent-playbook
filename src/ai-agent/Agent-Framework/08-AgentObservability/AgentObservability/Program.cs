// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a simple AI agent with Azure OpenAI as the backend that logs telemetry using OpenTelemetry.

using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.Text;


// 设置控制台的输入输出编码为 UTF-8，确保中文等多字节字符能正确显示和读取
Console.InputEncoding = Encoding.UTF8;
// 不输出 BOM（字节顺序标记），避免在控制台前端或日志中出现不可见字符
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

// 从环境变量读取 Azure OpenAI 服务的端点与部署名
// AZURE_OPENAI_ENDPOINT 必须设置，否则抛出异常
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
// 部署名称可通过环境变量覆盖，默认使用 gpt-4o-mini
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

// 可选：Application Insights 的连接字符串，用于将跟踪导出到 Azure Monitor
var applicationInsightsConnectionString = Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");

// 为每次运行生成一个唯一的 source name，方便在 OpenTelemetry 中区分会话
string sourceName = Guid.NewGuid().ToString("N");

// 创建 OpenTelemetry 的 TracerProviderBuilder，并添加控制台导出器（便于本地调试）
var tracerProviderBuilder = Sdk.CreateTracerProviderBuilder()
    .AddSource(sourceName)
    .AddConsoleExporter();

// 如果配置了 Application Insights，则将 Azure Monitor Trace Exporter 加入到 TracerProvider
if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    tracerProviderBuilder.AddAzureMonitorTraceExporter(options => options.ConnectionString = applicationInsightsConnectionString);
}

// 构建并在作用域结束时释放 TracerProvider
using var tracerProvider = tracerProviderBuilder.Build();

// 指令：定义 agent 的行为风格（系统提示）
const string instructions = "你是一位江湖说书人，擅长用幽默、接地气的方式讲笑话和故事。";
// 用户输入的 prompt：想要 agent 执行的具体任务
const string prompt = "给我讲一个发生在茶馆里的段子，轻松一点的那种。";

// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// 说明：演示如何使用 Azure OpenAI SDK（通过 Azure CLI 登录凭据）来构建一个 AIAgent
// AsIChatClient()/AsAIAgent() 等扩展方法用于将 SDK 客户端适配为通用的 Agent 接口
AIAgent openAIAgent = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(instructions: instructions, name: "Joker")
    .AsBuilder()
    // 将 OpenTelemetry 集成到 Agent，用于记录追踪信息
    .UseOpenTelemetry(sourceName: sourceName)
    .Build();

// 同步运行 agent 并输出结果到控制台
Console.WriteLine(await openAIAgent.RunAsync(prompt));

// 如果需要流式输出（逐步接收模型生成的中间结果），可以使用 RunStreamingAsync
// 以下代码为示例（已注释）：
// await foreach (var update in openAIAgent.RunStreamingAsync(prompt))
// {
//     Console.WriteLine(update);
// }

// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================
// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// 说明：使用 Azure.Identity 的 DefaultAzureCredential（更适合托管环境或本地开发）
AIAgent foundryAgent = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential())
    .AsAIAgent(model: deploymentName, instructions: instructions, name: "Joker")
    .AsBuilder()
    .UseOpenTelemetry(sourceName: sourceName)
    .Build();

// 再次运行 agent 并打印结果（与上面的 openAIAgent 功能等价，但演示另一种客户端构建方式）
Console.WriteLine(await foundryAgent.RunAsync(prompt));

// 流式输出示例（已注释）
// await foreach (var update in foundryAgent.RunStreamingAsync(prompt))
// {
//     Console.WriteLine(update);
// }
