> **Status:** Historical migration sketch from before this repository existed in its current form. It is not a description of the code today. See `docs/sk-to-maf-migration.md` and `docs/maf-workflow-evolution-backlog.md`.

 **Architectural Decision:** To create a separate successor to csharp-semantic-document-processor powered by the **Microsoft Agent Framework (MAF)**.
 
 **Rationale:** Semantic Kernel (SK) provided an excellent foundation for integrating LLMs via a single-agent, prompt-chaining architecture. However, as the document processing requirements have grown to require long-running tasks, multi-step validation, and potentially multi-agent collaboration, SK’s linear pipelines have become a constraint [1].
 
 By creating an independent MAF successor (using the official Microsoft successor that merges Semantic Kernel and AutoGen) [2], we make a clean break from legacy SK abstractions. This allows the new codebase to natively adopt MAF’s graph-based workflows, Executor classes, and durable state management[3, 4], without shoehorning new paradigms into the old V1 architecture. The original SK repository will be preserved in maintenance mode as an example of V1 prompt orchestration, while this V2 repository drives all feature development forward [5].

----------

### 🚀 The Migration Plan

Moving from Semantic Kernel to Microsoft Agent Framework is not just a package swap; it's a shift from prompt-chaining to graph-based agent workflows [4, 6]. Here is your step-by-step roadmap.

#### Phase 1: Separate Successor Repository & Setup

1.  **Duplicate the Codebase:** Clone the existing repo and push it to a new repository (e.g., csharp-maf-document-processor).
    
2.  **Scrub the NuGets:** Remove all Microsoft.SemanticKernel.* NuGet packages from your .csproj files.
    
3.  **Install MAF Packages:** Add the new core libraries:
    
    -   dotnet add package Microsoft.Agents.AI (Core agent abstractions) [3, 7]
        
    -   dotnet add package Microsoft.Agents.AI.Workflows (The new graph-based orchestration) [3, 4]
        
    -   dotnet add package Microsoft.Extensions.AI (Microsoft's new standard DI and middleware abstractions) [8]
        

#### Phase 2: Refactoring Core Abstractions (The Translation Layer)

You will need to map your existing Semantic Kernel concepts to their new MAF equivalents [5].

-   **From Kernel to AIAgent:** Instead of injecting a heavy Kernel object everywhere, you will instantiate an AIAgent. This represents the persona/model doing the work[7, 9].
    
-   **From [KernelFunction] to Tools/Executors:** In SK, you wrapped C# methods in [KernelFunction] attributes [10]. In MAF, complex logic is better handled by creating discrete **Executors**. You will rewrite your document parsing logic into classes that inherit from Executor<TInput, TOutput> [4].
    
-   **From PromptExecutionSettings to Structured Output:** Instead of begging the LLM for JSON via prompt templates, use MAF’s native RunAsync<T>() [11]. You define a C# record (e.g., DocumentSummary) and MAF automatically forces the model to return that exact schema [11].
    

#### Phase 3: Building the MAF Workflow (The "Aha!" Moment)

In your SK app, you likely had a linear pipeline (e.g., Extract Text -> Summarize -> Save). MAF replaces this with a **Directed Graph** [4].

1.  **Define the Steps:** Create a class for each step in your document processor.
    
    codeC#
    
    ```
    internal sealed class ExtractTextExecutor : Executor<FileRequest, DocumentText>("ExtractText") { ... }
    internal sealed class SummarizeAgentExecutor : Executor<DocumentText, DocumentSummary>("SummarizeAgent") { ... }
    ```
    
2.  **Wire the Graph:** Use MAF's workflow builder to wire these together. You can now easily introduce **Conditional Edges** (e.g., IF the document is a W2, send it to the Tax Agent; IF it's a resume, send it to the HR Agent) [1].
    

#### Phase 4: Implementing the "Walk Away" Features

Now that you are on MAF, you can build the features that would have been a nightmare to implement in Semantic Kernel[2]:

1.  **Add Human-in-the-Loop (Handoff Orchestration):** If the document confidence score is below 80%, have the MAF workflow pause and wait for a human to approve the extraction before continuing [1, 4].
    
2.  **Add Durability:** Add the Microsoft.Agents.AI.DurableTask package [12]. Now, if your server crashes while processing a massive 500-page PDF, the MAF workflow will automatically resume exactly where it left off upon reboot [12, 13]. (Doing this in SK required building custom state machines).
    
3.  **Multi-Agent Collaboration:** Introduce an AnalystAgent to read the document, and a CriticAgent to review the Analyst's work for hallucinations before finalizing the output[9, 14]. MAF handles the chat loop between these two natively [1].
    

### Target Outcome

The migration should leave the document processor with a clear separation between model-backed reasoning and deterministic application logic, explicit graph-based orchestration where it adds value, and a tested path for introducing long-running or human-reviewed workflows when those requirements become concrete.
