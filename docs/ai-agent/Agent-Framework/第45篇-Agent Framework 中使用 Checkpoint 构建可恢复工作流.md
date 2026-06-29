# Agent Framework 中使用 Checkpoint 构建可恢复工作流

在前面的几篇文章中，我们介绍了 Workflow 的多种编排能力，例如通过 Switch 实现多分支路由，通过 Fan-Out 实现并行执行，以及通过 Loop 构建循环工作流。

这些能力解决的是 Workflow 如何执行的问题。

但在真实业务系统中，仅仅能够执行还远远不够。

很多Workflow往往需要运行很长时间，例如一个审批流程可能持续几天，一个文档处理任务可能需要几十分钟，一个 Human-in-the-Loop 工作流甚至可能需要等待用户几小时之后才会继续执行。

如果 Workflow在执行过程中因为程序退出、服务器重启或者异常中断而终止，那么之前已经完成的工作是否还能继续？如果每次都只能重新开始，不仅会浪费计算资源，也会影响整个业务流程。

因此，Agent Framework 提供了 Checkpoint（检查点）机制。

Checkpoint 可以在 Workflow 执行过程中保存当前运行状态，当 Workflow 中断之后，再从保存的位置恢复执行，而不是重新开始整个流程。

## 示例场景

示例场景

本示例继续使用上一篇文章中的猜数字 Workflow。

Workflow 由两个 Executor 组成：

GuessNumberExecutor：根据当前范围不断计算下一次猜测的数字。
JudgeExecutor：负责判断当前猜测是否正确，并根据结果告诉 GuessNumberExecutor 应该继续缩小范围还是结束流程。

整个 Workflow 会不断循环，直到找到正确答案。

与上一篇文章不同的是，本示例在 Workflow 运行过程中会自动创建 Checkpoint。

根据 Agent Framework 官方文档：

Checkpoints are created at the end of each superstep, after all executors in that superstep have completed their execution.

也就是说，Checkpoint 并不是开发者主动调用某个 API 创建的，而是在每个 Super Step 完成之后，由 Framework 自动生成。

如果程序此时退出，下次启动时不需要重新从第一次猜测开始，而是可以直接恢复到之前保存的位置，然后继续执行剩余流程。

这就是 Checkpoint 最重要的价值。


## Checkpoint 保存了什么

很多人第一次接触 Checkpoint 时，都会认为它只是记录了 Workflow 当前执行到了哪个节点。

实际上并不是。

根据 Agent Framework 官方文档，一个 Checkpoint 保存的是整个 Workflow 的运行快照（Snapshot），其中包括：

当前所有 Executor 的状态（The current state of all executors）
下一 Super Step 的待处理消息（All pending messages）
等待处理的请求与响应（Pending requests and responses）
Workflow 的共享状态（Shared states）

因此，一个 Checkpoint 保存的并不是某一个 Executor，而是整个 Workflow 在某一时刻的完整运行现场。

正因为保存的是整个运行现场，所以 Framework 才能够在恢复之后继续执行，而不是重新开始整个 Workflow。

## 核心代码实现

整个示例与上一篇 Loop 示例相比，Workflow 的定义几乎没有变化：

return new WorkflowBuilder(guessNumberExecutor)
        .AddEdge(guessNumberExecutor, judgeExecutor)
        .AddEdge(judgeExecutor, guessNumberExecutor)
        .WithOutputFrom(judgeExecutor)
        .Build();

真正发生变化的是 Workflow 的启动方式。

首先创建一个 CheckpointManager：

var checkpointManager = CheckpointManager.Default;

随后，在启动 Workflow 时，将 CheckpointManager 一起传入：

await using StreamingRun checkpointedRun =
        await InProcessExecution.RunStreamingAsync(
                workflow,
                NumberSignal.Init,
                checkpointManager);

Framework 检测到提供了 CheckpointManager 后，便会在 Workflow 运行过程中自动管理 Checkpoint 的生命周期，而无需开发者手动保存 Workflow 状态。

整个生命周期可以理解为：

Executor.HandleAsync()
        ↓
ExecutorCompletedEvent
        ↓
Framework 收集 Workflow 当前状态
        ↓
创建 Checkpoint
        ↓
SuperStepCompletedEvent

当 SuperStepCompletedEvent 触发时，说明当前 Checkpoint 已经创建完成。

此时可以通过：

CheckpointInfo? checkpoint =
        superStepCompletedEvt.CompletionInfo!.Checkpoint;

获取当前生成的 CheckpointInfo。

示例中将这些 CheckpointInfo 保存到一个集合中，便于后续恢复 Workflow。

## 从 Checkpoint 恢复 Workflow

Workflow 完成之后，示例重新创建了一个新的 Workflow 实例：

```csharp
var newWorkflow = WorkflowFactory.BuildWorkflow();
```

随后，从之前保存的 Checkpoint 中取出一个：

```csharp
CheckpointInfo savedCheckpoint =
        checkpoints[CheckpointIndex];
```

这里没有再次调用 `RunStreamingAsync`，而是调用了：

```csharp
await InProcessExecution.ResumeStreamingAsync(
        newWorkflow,
        savedCheckpoint,
        checkpointManager);
```

这也是整个示例最核心的一步。

`ResumeStreamingAsync` 会根据 Checkpoint 中保存的信息恢复 Workflow。

恢复之后，Workflow 会继续执行剩余流程，而不会重新回到起点。

整个恢复过程可以理解为：

```text
Workflow
    ↓
创建 Checkpoint
    ↓
程序退出
    ↓
重新创建 Workflow
    ↓
ResumeStreamingAsync
    ↓
继续执行
```

从 Workflow 的角度来看，它就像从未中断过一样。

## Executor 如何保存自己的状态

Checkpoint 不仅需要恢复 Workflow 当前执行到了哪里，还需要恢复每个 Executor 的内部状态。

例如 GuessNumberExecutor 中维护了两个成员变量：

LowerBound
UpperBound

它们记录了当前猜测范围。

如果恢复 Workflow 时没有恢复这两个变量，那么 Workflow 即使知道当前执行到了 Guess 节点，也无法继续计算下一次猜测结果。

因此，Executor 可以重写：

OnCheckpointingAsync(...)

在 Framework 创建 Checkpoint 时，将需要持久化的数据写入 Workflow State：

context.QueueStateUpdateAsync(
        StateKey,
        (LowerBound, UpperBound));

需要注意的是，OnCheckpointingAsync() 本身并不会创建 Checkpoint。

它的职责只是告诉 Framework："当前 Executor 有哪些状态需要保存。"

Framework 收集完所有 Executor 的状态之后，才会统一创建一个新的 Checkpoint。

当 Workflow 恢复时，Framework 会调用：

OnCheckpointRestoredAsync(...)

重新读取之前保存的数据：

context.ReadStateAsync<(int, int)>(StateKey);

这样，GuessNumberExecutor 就能够继续使用恢复后的上下界，而不是重新回到初始范围。

JudgeExecutor 的实现方式也是一样。

它保存的是成员变量 _tries，因此恢复 Workflow 后，已经猜测了多少次也能够继续保持，而不会重新开始计数。

## Super Step 与 Checkpoint

本示例中还出现了一个新的概念——Super Step。

可以把它理解成 Workflow 的一个执行阶段，也是 Framework 创建 Checkpoint 的基本单位。

根据官方文档：

Checkpoints are created at the end of each superstep.

也就是说，Checkpoint 总是在一个 Super Step 完成之后创建，而不是某个 Executor 完成之后立即创建。

每一个 Checkpoint 保存的都是整个 Workflow 在该执行阶段结束时的一致性快照（Consistent Snapshot）。

因此，Checkpoint 保存的不是某一个 Executor 的局部状态，而是整个 Workflow 的运行现场。

## Checkpoint 与 Workflow State 的关系

Checkpoint 与 Workflow State 的关系

很多人第一次接触 Checkpoint 时，很容易把它与 Workflow State 混淆。

实际上，两者承担着完全不同的职责。

Workflow State 负责保存业务数据，例如邮件内容、订单信息或者用户上下文。

Checkpoint 则负责保存 Workflow 的运行状态，包括当前执行位置、待处理消息以及各个 Executor 保存的状态。

在本示例中：

LowerBound、UpperBound 和 _tries 都通过 Workflow State 保存。

Checkpoint 并不会关心这些数据代表什么业务含义，它只是负责在创建 Checkpoint 时将这些状态统一保存，并在恢复 Workflow 时重新恢复回来。

可以理解为：

Workflow State 保存的是业务数据。

Checkpoint 保存的是整个 Workflow 的执行现场。

两者相互配合，最终实现 Workflow 的断点恢复能力。

## 小结

本示例介绍了 Agent Framework 中 Checkpoint 的基本使用方式。

通过 CheckpointManager，Framework 能够在 Workflow 运行过程中自动创建 Checkpoint，并在 Workflow 中断之后，通过 ResumeStreamingAsync 从指定位置恢复执行。

为了保证恢复后的 Workflow 能够继续运行，每个 Executor 还可以通过 OnCheckpointingAsync 和 OnCheckpointRestoredAsync 保存和恢复自己的内部状态。

从架构角度来看，Checkpoint 解决的是 Workflow 的可靠性（Reliability）问题。它让 Workflow 不再依赖进程生命周期，即使发生程序退出、服务器重启或系统故障，也能够从之前保存的位置继续执行，而不是重新开始。