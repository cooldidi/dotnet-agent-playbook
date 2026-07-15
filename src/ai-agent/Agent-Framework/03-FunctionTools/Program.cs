// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a simple AI agent with a multi-turn conversation.

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

[Description("获取指定国家的最新新闻标题。")]
static string GetNews([Description("国家名称。")] string country)
    => $"来自 {country} 的头条新闻：AI 正在革新软件开发领域。";

var credential = new DefaultAzureCredential();
var newsTool = AIFunctionFactory.Create(GetNews);

const string instructions = "你是一个乐于助人的助手。";
const string prompt = "美国的最新新闻头条有哪些？";

// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
AIAgent openAIAgent = new AzureOpenAIClient(
    new Uri(endpoint),
    credential)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(instructions: instructions, tools: [newsTool]);
//.CreateAIAgent(instructions: "你是一个乐于助人的助手", tools: [AIFunctionFactory.Create(GetNews)]);
//非流式输出
Console.WriteLine(await openAIAgent.RunAsync(prompt));

//流式输出
await foreach (var update in openAIAgent.RunStreamingAsync(prompt))
{
    Console.WriteLine(update);
}

// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================
AIAgent foundryAgent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(model: deploymentName, instructions: instructions, tools: [newsTool]);
//非流式输出
Console.WriteLine(await foundryAgent.RunAsync(prompt));

//流式输出
await foreach (var update in foundryAgent.RunStreamingAsync(prompt))
{
    Console.WriteLine(update);
}

