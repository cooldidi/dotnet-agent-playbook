// 版权所有 (c) Microsoft。保留所有权利。

using Azure.AI.OpenAI;
using Azure.Identity;
using Google.GenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OllamaSharp;
using System.Text;


Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
// 定义讨论主题。
const string Topic = "金毛贵宾犬是最好的宠物。";

//  Google Provider
//AIAgent google = new Client(vertexAI: false, apiKey: Environment.GetEnvironmentVariable("GOOGLE_GENAI_API_KEY"))
//    .AsIChatClient("model").AsAIAgent("", "", "");

//  Anthropic Claude Provider
//AIAgent anthropic = new Anthropic.AnthropicClient(
//    new() { ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") })
//    .AsIChatClient("claude-sonnet-4-20250514").AsAIAgent("", "", "");


#region Ollama Provider
var endpoint = "http://localhost:11434/";
var modelName = "qwen3:1.7b";
AIAgent researcher = new OllamaApiClient(new Uri(endpoint), modelName)
                    .AsAIAgent(instructions: @"针对用户指定的主题写一篇短文。文章应为三到五段，使用高中阅读水平的语言，并包含相关背景信息、
                    关键论点和显著观点。你必须至少包含一条关于该主题的愚蠢且客观错误的信息，但你要相信它是真的。",
    name: "researcher",
    description: "研究某个主题并撰写相关内容。");
#endregion


#region OpenAI Provider
#pragma warning disable OPENAI001
AIAgent checker = new OpenAI.OpenAIClient(
    "sk-XXXXXXXX")
    .GetResponsesClient()
    .AsIChatClient("gpt-5")
    .AsAIAgent(instructions: @"评估研究人员的文章。针对其中的任何声明，根据可靠来源验证其准确性，并标明其属于以下哪种情况：
                    支持、
                    部分支持、
                    未验证、
                    错误，
                    同时提供简短的理由说明。",
    name: "checker",
    description: "根据可靠来源进行事实核查，并标记不准确的信息。", [new HostedWebSearchTool()]);
#pragma warning restore OPENAI001 
#endregion


#region Azure OpenAI
endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("未设置 AZURE_OPENAI_ENDPOINT。");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") ?? "gpt-5.4-mini";
AIAgent reporter = new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential())
                             .GetChatClient(deploymentName).AsIChatClient()
                             .AsAIAgent(instructions: """
                                        将原始文章总结为一个单段落，同时结合后续的事实核查来纠正任何不准确之处。只包含已被事实核查员确认的事实。
                                        省略任何被标记为不准确或未验证的信息。总结应清晰、简洁且信息充分。
                                        你绝对不要解释你正在做什么。只需输出最终段落。
                                        """,
                                        name: "reporter",
                                        description: "将研究员的文章总结为单个段落，只关注事实核查员已确认的事实。");
#endregion



// 构建一个顺序工作流：研究员 -> 历史导师 -> 记者
AIAgent workflowAgent = AgentWorkflowBuilder.BuildSequential(researcher, checker, reporter).AsAIAgent();

// 运行工作流，并在输出到达时以流式方式显示。
string? lastAuthor = null;
await foreach (var update in workflowAgent.RunStreamingAsync(Topic))
{
    // 跳过仅包含 WorkflowEvent 的更新
    if ((update.Contents == null || update.Contents.Count == 0) && update.RawRepresentation is WorkflowEvent)
    {
        continue;
    }

    if (lastAuthor != update.AuthorName)
    {
        lastAuthor = update.AuthorName;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n\n** {update.AuthorName} **");
        Console.ResetColor();
    }

    Console.Write(update.Text);
}