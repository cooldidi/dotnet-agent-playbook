
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-4o-mini";

const string instructions = "你是一位江湖说书人，擅长用幽默、接地气的方式讲笑话和故事。";
const string prompt = "给我讲一个发生在茶馆里的段子，轻松一点的那种。";
const string promptEmoji = "现在把这个段子加上一些表情符号，并用说书人的语气再讲一遍。";

// ============================================================
// 方式一：通过 AzureOpenAIClient / ChatClient 创建 Agent
// ============================================================
AIAgent openAIAgent = new AzureOpenAIClient(
    new Uri(endpoint),
    new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient()
    .AsAIAgent(instructions: instructions, name: "Joker");

// 开始Agent会话
AgentSession openAIAgentSession = await openAIAgent.CreateSessionAsync();
// 运行Agent
Console.WriteLine(await openAIAgent.RunAsync(prompt, openAIAgentSession));
// 序列化线程状态以便稍后恢复
JsonElement openAISerializedSession = await openAIAgent.SerializeSessionAsync(openAIAgentSession);

// 保存序列化的线程到临时文件
string tempFilePath = Path.GetTempFileName();
await File.WriteAllTextAsync(tempFilePath, JsonSerializer.Serialize(openAISerializedSession));

// 从临时文件加载序列化的线程（仅用于演示）。
JsonElement openAIReloadedSerializedSession = JsonElement.Parse(await File.ReadAllTextAsync(tempFilePath));
// 反序列化线程以恢复状态
AgentSession openAIResumedSession = await openAIAgent.DeserializeSessionAsync(openAIReloadedSerializedSession);

Console.WriteLine(await openAIAgent.RunAsync(promptEmoji, openAIResumedSession));


// ============================================================
// 方式二：通过 AIProjectClient 创建 Agent
// ============================================================

AIAgent foundryAgent = new AIProjectClient(
    new Uri(endpoint),
    new DefaultAzureCredential())
    .AsAIAgent(model: deploymentName, instructions: instructions, name: "Joker");

AgentSession foundrySession = await foundryAgent.CreateSessionAsync();

Console.WriteLine(await foundryAgent.RunAsync(prompt, foundrySession));

JsonElement foundrySerializedSession = await foundryAgent.SerializeSessionAsync(foundrySession);

Console.WriteLine("\n--- Serialized session ---\n");
Console.WriteLine(JsonSerializer.Serialize(foundrySerializedSession, new JsonSerializerOptions { WriteIndented = true }) + "\n");
AgentSession foundryResumedSession = await foundryAgent.DeserializeSessionAsync(foundrySerializedSession);
Console.WriteLine(await foundryAgent.RunAsync(promptEmoji, foundryResumedSession));