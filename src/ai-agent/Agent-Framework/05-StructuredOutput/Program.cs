// Copyright (c) Microsoft. All rights reserved.
// This sample shows how to configure ChatClientAgent to produce structured output.
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using System.ComponentModel;
using System.Runtime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";


const string instructions = "你是一个乐于助人的助手";
const string prompt = "请提供关于桂兵兵的信息，他是一名 39 岁的软件工程师。";
AzureCliCredential credential = new AzureCliCredential();


// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
AIAgent openAIAgent = new AzureOpenAIClient(
    new Uri(endpoint),
    credential)
   .GetChatClient(deploymentName)
   .AsIChatClient()
   .AsAIAgent(instructions: instructions, name: "HelpfulAssistant");


AgentResponse<PersonInfo> openAIResponse = await openAIAgent.RunAsync<PersonInfo>(
        prompt
);

Console.WriteLine("助理输出:");
Console.WriteLine($"姓名: {openAIResponse.Result.Name}");
Console.WriteLine($"年龄: {openAIResponse.Result.Age}");
Console.WriteLine($"职业: {openAIResponse.Result.Occupation}");

// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================
AIAgent foundryAgent = new AIProjectClient(new Uri(endpoint), credential)
                    .AsAIAgent(new ChatClientAgentOptions()
                    {
                        Name = "HelpfulAssistant",
                        ChatOptions = new()
                        {
                            ModelId = deploymentName,
                            Instructions = instructions,
                            ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<PersonInfo>()
                        }
                    });

AgentResponse<PersonInfo> foundryResponse = await foundryAgent.RunAsync<PersonInfo>(prompt);


Console.WriteLine("助理输出 JSON:");
Console.WriteLine(foundryResponse.Text);

Console.WriteLine("助理输出 (反序列化):");
Console.WriteLine($"姓名: {foundryResponse.Result.Name}");
Console.WriteLine($"年龄: {foundryResponse.Result.Age}");
Console.WriteLine($"职业: {foundryResponse.Result.Occupation}");
Console.WriteLine();


[Description("个人信息，包括他们的姓名、年龄和职业。")]
public class PersonInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("age")]
    public int? Age { get; set; }

    [JsonPropertyName("occupation")]
    public string? Occupation { get; set; }
}