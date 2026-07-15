// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a Azure OpenAI AI agent as a function tool.

using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text;


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

[Description("获取指定地点的天气信息。")]
static string GetWeather([Description("要获取天气的地点。")] string location)
    => $"{location} 多云，最高气温 15°C。";

// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
AIAgent agentTool = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(
        name: "WeatherAgent",
        instructions: "你专注于回答天气相关的问题，必要时调用工具获取信息后再回答。",
        description: "一个提供天气信息的智能体。",
        tools: [AIFunctionFactory.Create(GetWeather)]
    );

AIAgent openAIAgent = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(
        instructions: """
你是一个助手，必须只用日语回答。
工具返回的内容可能是中文，请将其翻译成自然的日语后再输出。
不要输出中文或英文。
""",
        tools: [agentTool.AsAIFunction()]
    );

Console.WriteLine(await openAIAgent.RunAsync("东京的天气如何？"));

// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================
var aiProjectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
var foundryTool = aiProjectClient
    .AsAIAgent(
       model: deploymentName,
       instructions: "你专注于回答天气相关的问题，必要时调用工具获取信息后再回答。",
       name: "WeatherAgent",
       description: "一个提供天气信息的智能体。",
       tools: [AIFunctionFactory.Create(GetWeather)]);

var foundryAgent = aiProjectClient
   .AsAIAgent(
       model: deploymentName,
       instructions: """
 你是一个助手，必须只用日语回答。       
 工具返回的内容可能是中文，请将其翻译成自然的日语后再输出。
 不要输出中文或英文。
 """,
       tools: [foundryTool.AsAIFunction()]);

Console.WriteLine(await foundryAgent.RunAsync("东京的天气如何？"));
