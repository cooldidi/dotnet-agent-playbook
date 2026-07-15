// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a simple AI agent with tools from an MCP Server.

using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI.Chat;
using System.Text;



Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);


var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";


await using var mcpClient = await McpClient.CreateAsync(new StdioClientTransport(new()
{
    Name = "MCPServer",
    Command = "npx",
    Arguments = ["-y", "--verbose", "@modelcontextprotocol/server-github"],
}));


var mcpTools = await mcpClient.ListToolsAsync().ConfigureAwait(false);

// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
AIAgent agent = new AzureOpenAIClient(
    new Uri(endpoint),
    new AzureCliCredential())
     .GetChatClient(deploymentName)
     .AsAIAgent(instructions: "你是一个只回答与GitHub仓库相关问题的AI助手。", tools: [.. mcpTools.Cast<AITool>()]);

Console.WriteLine(await agent.RunAsync("总结一下 microsoft/semantic-kernel 仓库的最近四次提交？"));


// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================
AIAgent foundryAgent = new AIProjectClient(
    new Uri(endpoint),
    new AzureCliCredential())
    .AsAIAgent(
        model: deploymentName,
        name: "SpaceNovelWriter",
        instructions: "你是一个帮助人们查找信息的 AI 助手。", tools: [.. mcpTools.Cast<AITool>()]);

Console.WriteLine(await foundryAgent.RunAsync("总结一下 microsoft/semantic-kernel 仓库的最近四次提交？"));

