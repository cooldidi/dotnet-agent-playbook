using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;

/// <summary>
/// Agent相关的公共辅助方法
/// </summary>
public static class AgentHelper
{
    /// <summary>
    /// 实时流式输出Agent的响应更新
    /// </summary>
    public static void WriteStreamingUpdate(AgentResponseUpdateEvent updateEvent, ref string? lastResponseId)
    {
        string responseId = updateEvent.Update.ResponseId ?? updateEvent.Update.MessageId ?? updateEvent.ExecutorId;
        if (!string.Equals(responseId, lastResponseId, StringComparison.Ordinal))
        {
            if (lastResponseId is not null)
            {
                Console.WriteLine();
                Console.WriteLine();
            }

            Console.Write($"- {updateEvent.ExecutorId}: ");
            lastResponseId = responseId;
        }

        if (!string.IsNullOrEmpty(updateEvent.Update.Text))
        {
            Console.Write(updateEvent.Update.Text);
        }
    }

    /// <summary>
    /// 输出Magentic消息（带翻译）
    /// </summary>
    public static async Task WriteMagenticMessage(string title, string? content, ChatClientAgent translatorAgent)
    {
        Console.WriteLine();
        Console.WriteLine($"[Magentic {title}]");

        if (!string.IsNullOrEmpty(content))
        {
            var response = await translatorAgent.RunAsync(new ChatMessage(ChatRole.User, content));
            Console.WriteLine(response.Text);
        }
    }

    /// <summary>
    /// 格式化进度账本信息
    /// </summary>
    public static string FormatProgressLedger(MagenticProgressLedger ledger) =>
        string.Join(Environment.NewLine,
            $"请求满足: {ledger.IsRequestSatisfied}",
            $"循环中: {ledger.IsInLoop}",
            $"正在取得进展: {ledger.IsProgressBeingMade}",
            $"下一个发言者: {ledger.NextSpeaker}",
            $"指令: {ledger.InstructionOrQuestion}");

    /// <summary>
    /// 交互式暂停，等待用户按回车继续
    /// </summary>
    public static void PauseIfInteractive()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return;
        }

        Console.Write("按回车键继续...");
        Console.ReadLine();
        Console.WriteLine();
    }
}