using Microsoft.Agents.AI.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace _44_Human_in_the_Loop;

internal static class WorkflowFactory
{
    /// <summary>
    /// 获取一个工作流，该工作流通过人机交互进行数字猜测游戏。
    /// 一个输入端口允许外部世界在请求时向工作流提供输入。
    /// </summary>
    internal static Workflow BuildWorkflow()
    {
        // Create the executors
        RequestPort numberRequestPort = RequestPort.Create<NumberSignal, int>("GuessNumber");
        JudgeExecutor judgeExecutor = new(42);

        // Build the workflow by connecting executors in a loop
        return new WorkflowBuilder(numberRequestPort)
            .AddEdge(numberRequestPort, judgeExecutor)
            .AddEdge(judgeExecutor, numberRequestPort)
            .WithOutputFrom(judgeExecutor)
            .Build();
    }
}

/// <summary>
/// 使用Signals在猜测和JudgeExecutor之间进行通信。
/// </summary>
internal enum NumberSignal
{
    Init,
    Above,
    Below,
}

/// <summary>
/// 判断猜测并提供反馈的执行器。
/// </summary>
[SendsMessage(typeof(NumberSignal))]
[YieldsOutput(typeof(string))]
internal sealed class JudgeExecutor() : Executor<int>("Judge")
{
    private readonly int _targetNumber;
    private int _tries;

    /// <summary>
    /// 初始化一个新的<see cref="JudgeExecutor"/>类的实例。
    /// </summary>
    /// <param name="targetNumber">要猜测的数字。</param>
    public JudgeExecutor(int targetNumber) : this()
    {
        this._targetNumber = targetNumber;
    }

    public override async ValueTask HandleAsync(int message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        this._tries++;
        if (message == this._targetNumber)
        {
            await context.YieldOutputAsync($"{this._targetNumber} 猜对了，共尝试了 {this._tries} 次！", cancellationToken);
        }
        else if (message < this._targetNumber)
        {
            await context.SendMessageAsync(NumberSignal.Below, cancellationToken: cancellationToken);
        }
        else
        {
            await context.SendMessageAsync(NumberSignal.Above, cancellationToken: cancellationToken);
        }
    }
}
