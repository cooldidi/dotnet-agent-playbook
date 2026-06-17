# Agent Framework 中使用 Fan-Out 构建并行智能工作流

在上一篇文章中，我们介绍了如何通过 `AddSwitch` 实现多分支路由。

`AddSwitch` 解决的是一个非常典型的问题：当上游节点产生多个状态时，工作流应该进入哪一条处理路径。

但在真实业务系统中，一个结果往往不仅仅对应一个动作。

例如一封正常邮件进入系统后，我们不仅希望生成回复，还可能希望提取摘要、写入数据库、记录审计日志或者触发后续分析流程。

这时候系统需要解决的已经不是“选择哪条路”的问题，而是“当前结果应该触发哪些处理流程”的问题。

针对这种场景，Agent Framework 提供了 Fan-Out 机制。Fan-Out 允许一个节点根据运行时结果，将消息分发到一个或多个目标节点

## 示例场景

本示例继续沿用邮件处理场景。

当系统收到用户邮件后，首先会通过 EmailAnalysisExecutor 调用大模型分析邮件内容。分析结果会被转换成结构化的 AnalysisResult，其中包含垃圾邮件判断结果以及邮件长度等信息。

与上一篇文章最大的区别在于，这里的分析结果不会直接进入某一个固定执行器，而是交由 Fan-Out 根据分析结果动态决定后续处理流程。

除了垃圾邮件判断结果之外，本示例还会根据邮件长度决定是否生成邮件摘要。因此最终的路由结果不仅取决于 SpamDecision，也取决于 EmailLength。

整体执行流程如下图所示：

【流程图】

从流程图可以看到，邮件分析完成后，Workflow 可能进入以下几种处理路径：

- 垃圾邮件：进入 HandleSpamExecutor；
- 不确定邮件：进入 HandleUncertainExecutor；
- 正常短邮件：生成邮件回复，并将分析结果保存到数据库；
- 正常长邮件：同时生成邮件回复和邮件摘要，并将摘要结果保存到数据库。

其中，正常长邮件场景会同时触发多个执行器，这也是本示例中 Fan-Out 能力最典型的体现。

## 核心代码实现

整个示例最核心的代码如下：

```csharp
WorkflowBuilder builder = new(emailAnalysisExecutor);
builder.AddFanOutEdge(
    emailAnalysisExecutor,
    [
        handleSpamExecutor,
        emailAssistantExecutor,
        emailSummaryExecutor,
        handleUncertainExecutor,
    ],
    GetTargetAssigner()
)
.AddEdge(emailAssistantExecutor, sendEmailExecutor)
.AddEdge<AnalysisResult>(emailAnalysisExecutor, databaseAccessExecutor, condition: analysisResult => analysisResult?.EmailLength <= LongEmailThreshold)
.AddEdge(emailSummaryExecutor, databaseAccessExecutor)
.WithOutputFrom(handleUncertainExecutor, handleSpamExecutor, sendEmailExecutor);
```

这段代码虽然不长，但它同时完成了四件事情：指定工作流入口、定义 Fan-Out 路由、补充后续执行路径，以及声明 Workflow 的最终输出。

### 指定工作流入口

首先，通过 `WorkflowBuilder` 指定工作流的入口节点：

```csharp
WorkflowBuilder builder = new(emailAnalysisExecutor);
```
这里的 `EmailAnalysisExecutor` 是整个 `Workflow` 的起点。

它负责接收用户邮件，调用邮件分析 Agent，并返回结构化结果 `AnalysisResult`。后续无论是进入垃圾邮件处理、邮件回复、邮件摘要，还是数据库保存，都是基于这个 `AnalysisResult` 继续展开的。

也就是说，`EmailAnalysisExecutor` 的输出结果决定了后续 `Workflow` 的执行方向。

### 使用 Fan-Out 定义第一层路由

接下来，通过 `AddFanOutEdge` 定义第一层路由：

```csharp
builder.AddFanOutEdge(
    emailAnalysisExecutor,
    [
        handleSpamExecutor,
        emailAssistantExecutor,
        emailSummaryExecutor,
        handleUncertainExecutor,
    ],
    GetTargetAssigner()
)
```

这里表示：当`EmailAnalysisExecutor`执行完成后，`Workflow`不会固定进入某一个下游节点，而是调用 `GetTargetAssigner()` 计算目标节点集合。

Fan-Out 注册了四个候选目标节点（顺序非常重要）：

- `0 -> handleSpamExecutor`
- `1 -> emailAssistantExecutor`
- `2 -> emailSummaryExecutor`
- `3 -> handleUncertainExecutor`

`GetTargetAssigner()` 返回的整数集合会对应到这些目标节点。

例如返回 [0] 时，消息会被分发到 `handleSpamExecutor`；返回 [3] 时，会进入 `handleUncertainExecutor`；返回 [1] 时，会进入 `emailAssistantExecutor`；返回 [1, 2] 时，则会同时进入 `emailAssistantExecutor` 和 `emailSummaryExecutor`。

这也是 Fan-Out 与 AddSwitch 的关键区别：AddSwitch 最终只会选择一个目标节点，而 Fan-Out 返回的是目标节点集合，因此既可以路由到一个节点，也可以同时路由到多个节点。


### 使用AddEdge补充后续执行路径

Fan-Out 只负责决定第一批要触发的下游节点。

这些节点执行完成之后，如果还需要继续进入其他节点，就要通过 `AddEdge` 继续定义后续路径。

例如：

```csharp
.AddEdge(emailAssistantExecutor, sendEmailExecutor)
```

表示当`EmailAssistantExecutor`生成邮件回复后，Workflow 会继续进入 `SendEmailExecutor`，模拟发送邮件。

因此正常邮件的回复路径是：

```
EmailAnalysisExecutor
    -> EmailAssistantExecutor
    -> SendEmailExecutor
```
接下来这句代码使用的是条件边：

```csharp
.AddEdge<AnalysisResult>(
    emailAnalysisExecutor,
    databaseAccessExecutor,
    condition: analysisResult => analysisResult?.EmailLength <= LongEmailThreshold)
```

它表示：当邮件长度小于等于阈值时，EmailAnalysisExecutor 的分析结果会直接进入 DatabaseAccessExecutor，保存到数据库。

这里主要对应正常短邮件场景。因为短邮件不需要额外生成摘要，所以可以直接保存分析结果。

然后是：

```csharp
.AddEdge(emailSummaryExecutor, databaseAccessExecutor)
```

这表示如果邮件较长，并且前面触发了 EmailSummaryExecutor，那么摘要生成完成后，会继续进入 DatabaseAccessExecutor，保存分析结果和摘要信息。

因此，正常长邮件会形成两条后续路径：一条由 EmailAssistantExecutor 生成回复并发送邮件，另一条由 EmailSummaryExecutor 生成摘要并保存数据库。

这也说明 Fan-Out、普通 Edge 和 Condition Edge 并不是互斥的。实际 Workflow 中，它们经常会组合使用：Fan-Out 负责第一层动态分发，AddEdge 负责定义节点之间的后续关系，Condition Edge 则负责在特定条件下补充额外路径。

### 声明 Workflow 的最终输出

最后，通过 WithOutputFrom 指定 Workflow 的最终输出节点：

```csharp
.WithOutputFrom(
    handleUncertainExecutor,
    handleSpamExecutor,
    sendEmailExecutor)
```

这表示只有这三个执行器产生的输出会作为整个 Workflow 的对外输出。

在当前示例中，垃圾邮件路径的最终输出来自 HandleSpamExecutor；不确定邮件路径的最终输出来自 HandleUncertainExecutor；正常邮件路径的最终输出来自 SendEmailExecutor。

需要注意的是，DatabaseAccessExecutor 没有出现在 WithOutputFrom 中。

这意味着数据库保存并不是这个 Workflow 的最终输出结果，而是执行过程中的辅助处理逻辑。它可以通过事件记录数据库写入行为，但不会作为最终响应返回给用户。

## 路由逻辑是如何工作的

真正决定路由行为的是 GetTargetAssigner。该方法返回一个路由委托，用于根据上游节点的执行结果动态计算需要触发的目标节点。

在当前示例中，委托的第一个参数是 EmailAnalysisExecutor 输出的 AnalysisResult；第二个参数是 Fan-Out 注册的候选目标节点总数 targetCount；返回值则是一个整数集合，用于表示需要路由到的目标节点索引。

Framework 会根据返回的索引集合，将消息分发到对应的执行器。如果返回一个索引，则只触发一个目标节点；如果返回多个索引，则会同时触发多个目标节点。

```csharp
static Func<AnalysisResult?,int, IEnumerable<int>> GetTargetAssigner()
{
    return (analysisResult, targetCount) =>
    {
        if (analysisResult is not null)
        {
            if (analysisResult.spamDecision == SpamDecision.Spam)
            {
                return [0]; // 1. 路由到垃圾邮件处理器   
            }
            else if (analysisResult.spamDecision == SpamDecision.NotSpam)
            {
                List<int> targets = [1]; // 2. 路由到邮件助手

                if (analysisResult.EmailLength > LongEmailThreshold)
                {
                    targets.Add(2); // 3. 路由到邮件摘要器
                }

                return targets;
            }
            else
            {
                return [3]; // 4. 路由到不确定处理器
            }
        }
        throw new InvalidOperationException("无效的分析结果。");
    };
}
```

## 共享状态在并行流程中的作用

当多个执行器同时工作时，共享状态的重要性会进一步体现出来。

在 `EmailAnalysisExecutor` 中，系统首先生成 `EmailId`，然后将原始邮件内容保存到 Workflow State。

随后在 `AnalysisResult` 中仅保留 `EmailId`。

这样后续执行器之间传递的只是轻量级对象，而不是完整邮件内容。

当 `EmailAssistantExecutor` 需要生成回复时，可以通过 `EmailId` 从 State 中读取邮件内容。

当 `EmailSummaryExecutor` 需要生成摘要时，同样可以通过 `EmailId` 读取同一份数据。

多个执行器共享同一份上下文，而不需要在节点之间重复传递大型业务对象。

这种设计使 Workflow 中流转的是业务状态，而真正的业务数据统一保存在 State 中。

## 数据库执行器的设计

本示例还增加了一个 `DatabaseAccessExecutor`。

它模拟将邮件内容、分析结果以及邮件摘要保存到数据库。

与前面的执行器不同，它并不会产生 Workflow 输出，而是通过 `AddEventAsync` 主动向 Workflow 发布自定义事件。

这一点说明 Workflow 中不仅存在最终输出，还存在事件流：

DatabaseAccessExecutor 主要用于演示 Workflow 中的辅助处理流程。它不会产生最终输出，而是通过事件记录数据库写入行为。

这种模式在日志采集、审计记录、监控系统以及消息通知场景中非常常见。

## Fan-Out 与 AddSwitch 的区别

需要注意的是，Fan-Out 并不意味着一定会同时触发多个执行器。它的核心在于返回目标节点集合，而不是返回单个目标节点。因此 Fan-Out 既可以实现一对一的路由，也可以实现一对多的路由。本示例中的垃圾邮件和不确定邮件场景属于单目标路由，而长邮件场景则属于多目标路由。

两者都属于 Workflow 的路由能力，但关注点完全不同：

- AddSwitch 返回单个目标节点，因此更适合状态分流场景。
- Fan-Out 返回目标节点集合，因此更适合根据运行时结果触发一个或多个处理流程。

在企业级 Agent Workflow 中，这两种模式通常会同时出现。系统先通过 Agent 产生结构化决策结果，然后由 Workflow 根据业务需求选择单路径路由或者多路径并行处理，最终由不同的 Executor 执行具体业务动作。

## 小结

本示例介绍了 Agent Framework Workflow 中的 Fan-Out 路由模式。

在上一篇文章中，我们使用 AddSwitch 根据不同状态选择对应的处理路径。而 Fan-Out 的关注点并不是选择某一条路径，而是根据运行时结果决定需要触发哪些目标节点。

在本示例中，邮件首先经过 `EmailAnalysisExecutor` 分析，然后由 `GetTargetAssigner` 根据分析结果计算目标节点集合。

对于垃圾邮件和不确定邮件，工作流只会进入一个执行器；对于正常邮件，则可能同时触发邮件回复和邮件摘要两个执行器。

从实现角度来看，Fan-Out 返回的是目标节点集合，而不是单个目标节点。因此它既可以实现一对一的路由，也可以实现一对多的路由。

同时，本示例还结合了 Workflow State 共享上下文、条件边以及多执行器协作等机制，展示了一个相对完整的 Workflow 编排过程。

当一个节点的输出需要驱动多个后续处理流程时，Fan-Out 会比 AddSwitch 更适合表达这种业务场景。

