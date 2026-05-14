# 使用 Agent Framework 组合子工作流

在前面的文章中，我们已经介绍了 Agent Framework 中如何定义流程节点，以及 Workflow 的流式执行事件。

如果你对这些概念还不太熟悉，可以先回顾上一篇文章：

Agent Framework 定义节点以及节点的流程输出

这一节我们来介绍 Workflow Composition（工作流组合） 的概念，也就是如何把一个 Workflow 封装成一个可复用的 Executor，再嵌入到另一个 Workflow 中使用。

这种设计方式非常适合将一些通用流程模块化，提高整体流程的复用性和可维护性。

## 第一个流程：定义一个可复用的文本处理流程

先按照正常方式定义一个简单的 Workflow：

```csharp
UppercaseExecutor uppercase = new();
ReverseExecutor reverse = new();
AppendSuffixExecutor append = new(" [已处理]");

var workflow = new WorkflowBuilder(uppercase)
            .AddEdge(uppercase, reverse)
            .AddEdge(reverse, append)
            .WithOutputFrom(append)
            .Build();
```

这个流程的执行逻辑很简单，首先将输入字符串转换为大写, 其次将结果进行反转, 最后追加后缀 " [已处理]"

通过：
```csharp
.WithOutputFrom(append)
```
我们显式指定 append 为当前 Workflow 的输出节点。这意味着当流程执行完成后，对外返回的就是该节点的执行结果。

## 将 Workflow 包装成 Executor

当前这个 workflow 本身是一个完整流程，还不能直接作为节点加入其他 Workflow。如果想复用它，可以通过 BindAsExecutor() 将其封装成一个可执行节点：

```csharp
ExecutorBinding subWorkflowExecutor = workflow.BindAsExecutor("文本处理子工作流");
```

将一个完整 Workflow 包装成一个普通 Executor，这样它就可以像普通节点一样参与新的流程编排，这也是 Workflow Composition 的核心能力之一。


既然这个流程已经被封装成标准 Executor，那么就可以像普通节点一样参与更大的流程编排。

## 第二个流程：在主流程中使用子工作流

现在我们创建另一个 Workflow。这个主流程先给输入添加前缀，然后调用刚才定义好的子流程，最后再进行收尾处理：

```csharp

 PrefixExecutor prefix = new("input： ");
 PostProcessExecutor postProcess = new();

 var mainWorkflow = new WorkflowBuilder(prefix)
     .AddEdge(prefix, subWorkflowExecutor)
     .AddEdge(subWorkflowExecutor, postProcess)
     .WithOutputFrom(postProcess)
     .Build();
```

对于主流程来说，subWorkflowExecutor 和普通 Executor 没有区别。它只关心输入和输出，并不需要知道内部具体执行了哪些步骤。




## 总结

BindAsExecutor() 提供了一种 Workflow 复用机制，它可以将一个完整的 Workflow 封装成标准 Executor，从而参与更高层级的流程编排。

这意味着 Workflow 不再只是一次性的执行链路，而是可以像组件一样被组合、嵌套和复用。

这种方式本质上是一种 组合式流程设计（Workflow Composition），能够有效降低主流程复杂度，减少重复定义节点关系，让主流程结构更清晰。