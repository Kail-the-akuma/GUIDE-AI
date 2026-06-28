# Challenge GUIDE: Critique Guidelines & FAQ

Every software engineering methodology is refined through challenge, criticism, and debate. We do not present GUIDE as a set of perfect, immutable rules. We present it as a starting point. 

We invite you to challenge these principles, find edge cases, and help us improve how developers collaborate with AI.

---

## Community Critique Guidelines

If you believe one of the GUIDE principles is incorrect, incomplete, or counterproductive in practice, we want to hear about it. To keep discussions constructive, we ask that critiques follow these guidelines:

1. **Provide Concrete Scenarios:** Instead of saying *"Principle X is wrong,"* describe a specific development scenario or codebase structure where applying that principle leads to worse outcomes (e.g., higher development latency, lower quality, or increased developer cognitive load).
2. **Back with Empirical Data (When Possible):** If your critique involves token counts, cost efficiency, or error rates, share the codebases and model logs used so we can reproduce and learn from them.
3. **Propose Alternatives:** If you challenge a principle, offer a modified version of that principle or a new principle that better addresses the underlying problem.

---

## Frequently Asked Questions (FAQ)

### Q: Is GUIDE arguing against using prompts?
**No.** Prompts are the interface to Large Language Models. GUIDE is arguing against **prompt-only development methodologies**. Relying solely on prompts to guide AI through complex codebases leads to conversational amnesia and architectural drift. GUIDE suggests wrapping prompts inside a deterministic engineering process.

### Q: Why not just use models with infinite context windows?
While context windows are growing larger, feeding entire repositories into the prompt for every task has major drawbacks:
* **Attention Degradation:** LLMs suffer from "lost in the middle" phenomena, where they miss critical details when overwhelmed with large amounts of irrelevant context.
* **Economic Costs:** API costs scale with context size. Naive context ingestion becomes prohibitively expensive at scale.
* **Lack of Verification:** Having a larger context window does not prevent the model from generating code that violates architecture or fails to compile. You still need deterministic validation.

### Q: Is the reference implementation specific to C#?
The **reference implementation** in this repository is built in C# using Roslyn for AST parsing, but the **GUIDE principles themselves are language-agnostic**. The same principles can be applied to projects in TypeScript, Go, Python, OutSystems, or any other language ecosystem.

### Q: How does the Auto-Healer differ from naive retry loops?
Naive retry loops simply feed the error log back to the LLM and ask it to try again. GUIDE's Auto-Healer integrates with an **Engineering Memory** system. It first checks if a similar compiler error has been encountered and successfully patched in the past. It retrieves the patched pattern and guides the correction process, reducing LLM calls and preventing infinite loops.

### Q: Does GUIDE replace the human developer?
**Absolutely not.** The final principle—**Engineering Responsibility**—states that AI is an assistant, not the engineer. Humans remain fully responsible for what is checked in, how the code is structured, and the long-term maintainability of the project.
