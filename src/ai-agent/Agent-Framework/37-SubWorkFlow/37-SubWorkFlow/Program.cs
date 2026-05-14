// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI.Workflows;
using System.Text;

/// <summary>
/// 本示例演示如何通过将一个工作流作为另一个工作流中的执行器来实现
/// 分层组合工作流（子工作流）。
///
/// 子工作流是作为执行器嵌入到父工作流中的工作流。
/// 这样可以：
/// 1. 将复杂的工作流逻辑封装并复用为模块化组件
/// 2. 构建分层的工作流结构
/// 3. 创建可组合、可维护的工作流架构
///
/// 在此示例中，我们创建了：
/// - 一个文本处理子工作流（转大写 → 反转 → 追加后缀）
/// - 一个父工作流，它会先添加前缀，再经过子工作流处理，最后进行后处理
///
/// 对于输入“hello”，工作流会产生：“输入： [最终] OLLEH [已处理] [结束]”
/// </summary>
public static class Program
{
    private static async Task Main()
    {

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        Console.WriteLine("\n=== 子工作流演示 ===\n");

        // 第 1 步：构建一个简单的文本处理子工作流
        Console.WriteLine("正在构建子工作流：转大写 → 反转 → 追加后缀...\n");

        UppercaseExecutor uppercase = new();
        ReverseExecutor reverse = new();
        AppendSuffixExecutor append = new(" [已处理]");

        var workflow = new WorkflowBuilder(uppercase)
            .AddEdge(uppercase, reverse)
            .AddEdge(reverse, append)
            .WithOutputFrom(append)
            .Build();

        // 第 2 步：将子工作流配置为执行器，以便在父工作流中使用
        ExecutorBinding subWorkflowExecutor = workflow.BindAsExecutor("文本处理子工作流");

        // 第 3 步：构建一个使用子工作流作为执行器的主工作流
        Console.WriteLine("正在构建将子工作流作为执行器使用的主工作流...\n");

        PrefixExecutor prefix = new("input： ");
        PostProcessExecutor postProcess = new();

        var mainWorkflow = new WorkflowBuilder(prefix)
            .AddEdge(prefix, subWorkflowExecutor)
            .AddEdge(subWorkflowExecutor, postProcess)
            .WithOutputFrom(postProcess)
            .Build();

        // 第 4 步：执行主工作流
        Console.WriteLine("正在使用输入“hello”执行主工作流\n");
        await using Run run = await InProcessExecution.RunAsync(mainWorkflow, "hello");

        // 显示结果
        foreach (WorkflowEvent evt in run.NewEvents)
        {
            if (evt is ExecutorCompletedEvent executorComplete && executorComplete.Data is not null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[{executorComplete.ExecutorId}] {executorComplete.Data}");
                Console.ResetColor();
            }
            else if (evt is WorkflowOutputEvent output)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n=== 主工作流已完成 ===");
                Console.WriteLine($"最终输出：{output.Data}");
                Console.ResetColor();
            }
            else if (evt is WorkflowErrorEvent workflowError)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine(workflowError.Exception?.ToString() ?? "发生了未知的工作流错误。");
                Console.ResetColor();
            }
            else if (evt is ExecutorFailedEvent executorFailed)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"执行器“{executorFailed.ExecutorId}”失败，原因：{(executorFailed.Data == null ? "未知错误" : $"异常 {executorFailed.Data}")}。");
                Console.ResetColor();
            }
        }

        // 可选：可视化工作流结构——请注意，子工作流不会被渲染
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n=== 工作流可视化 ===\n");
        Console.WriteLine(mainWorkflow.ToMermaidString());
        Console.ResetColor();

        Console.WriteLine("\n✅ 示例完成：可以使用子工作流以分层方式组合工作流\n");
    }
}

// ====================================
// 文本处理执行器
// ====================================

/// <summary>
/// 为输入文本添加前缀。
/// </summary>
internal sealed class PrefixExecutor(string prefix) : Executor<string, string>("前缀执行器")
{
    public override ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        string result = prefix + message;
        Console.WriteLine($"[前缀] '{message}' → '{result}'");
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// 将输入文本转换为大写。
/// </summary>
internal sealed class UppercaseExecutor() : Executor<string, string>("转大写执行器")
{
    public override ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        string result = message.ToUpperInvariant();
        Console.WriteLine($"[转大写] '{message}' → '{result}'");
        return ValueTask.FromResult(result);
    }
}
/// <summary>
/// 反转输入文本。
/// </summary>
internal sealed class ReverseExecutor() : Executor<string, string>("反转执行器")
{
    public override ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        string result = string.Concat(message.Reverse());
        Console.WriteLine($"[反转] '{message}' → '{result}'");
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// 为输入文本追加后缀。
/// </summary>
internal sealed class AppendSuffixExecutor(string suffix) : Executor<string, string>("追加后缀执行器")
{
    public override ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        string result = message + suffix;
        Console.WriteLine($"[追加后缀] '{message}' → '{result}'");
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// 通过包裹文本执行最终后处理。
/// </summary>
internal sealed class PostProcessExecutor() : Executor<string, string>("后处理执行器")
{
    public override ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        string result = $"[最终] {message} [结束]";
        Console.WriteLine($"[后处理] '{message}' → '{result}'");
        return ValueTask.FromResult(result);
    }
}