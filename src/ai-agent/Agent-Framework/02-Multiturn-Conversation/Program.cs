// Copyright (c) Microsoft. All rights reserved.

// This sample shows how to create and use a simple AI agent with a multi-turn conversation.

using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System.Text;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

const string instructions = "你是一位江湖说书人，擅长用幽默、接地气的方式讲笑话和故事。";
const string prompt = "给我讲一个发生在茶馆里的段子，轻松一点的那种。";
const string prompt_emoji = "给我讲一个发生在茶馆里的段子，轻松一点的那种。加上表情符号。";
AzureCliCredential credential = new AzureCliCredential();

// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
AIAgent openAIAgent = new AzureOpenAIClient(
    new Uri(endpoint),
    credential)
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(instructions: instructions, name: "Joker");

AgentSession session = await openAIAgent.CreateSessionAsync();
Console.WriteLine(await openAIAgent.RunAsync(prompt, session));
Console.WriteLine(await openAIAgent.RunAsync(prompt_emoji, session));

// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================
AIAgent foundryAgent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(model: deploymentName, instructions: instructions, name: "Joker");

AgentSession session2 = await foundryAgent.CreateSessionAsync();
Console.WriteLine(await foundryAgent.RunAsync(prompt, session2));
Console.WriteLine(await foundryAgent.RunAsync(prompt_emoji, session2));



