# GUIDE Research: Open Questions

As generative artificial intelligence becomes a permanent participant in the software development lifecycle, we must address questions that go beyond simple code generation. 

The GUIDE principles propose surrounding probabilistic models with deterministic engineering layers. However, this raises several open research questions regarding the nature of AI technical debt, the limits of static validation boundaries, and human cognitive limits in the loop.

---

## 1. AI Technical Debt & Cognitive Complexity

AI has reduced the cost of writing code to near-zero, but has not changed the cost of maintaining it. We define *Instant Technical Debt* as the unnecessary complexity introduced by rapid, cheap generation.

### Key Research Questions
* **Quantifying AI-Native Complexity:** How can we define a mathematical or structural metric for "AI-generated entropy" that traditional metrics (like cyclomatic complexity or cognitive complexity) fail to capture?
* **Semantic Divergence Detection:** Can we detect when AI-generated code starts to diverge from the implicit design language of a repository, even if it is fully functional and compile-clean?
* **Optimal Refactoring Triggers:** At what point does the cost of incrementally healing AI-generated code exceed the cost of executing a structured rewrite by the engine?

---

## 2. The Deterministic Boundary

The GUIDE manifesto proposes that anything which can be solved deterministically should not consume probabilistic AI reasoning.

### Key Research Questions
* **The Static vs. Probabilistic Divide:** Where is the boundary between what can be verified via AST syntax analysis, FSM state verification, and architecture rules vs. what genuinely requires LLM semantic reasoning?
* **Executable Architectural Contracts:** How can we extend traditional architectural tests (e.g., project reference checks) to represent rich, executable behavioral contracts that guide agent generation?
* **State Space Limits in Auto-Healing:** To what level of code complexity can a deterministic Auto-Healer (relying on compiler logs and past correction lookups) succeed before it triggers infinite healing loops?

---

## 3. Human-in-the-Loop & Automation Bias

The final E of GUIDE stands for **Engineering Responsibility**. The responsibility for code quality and maintainability remains human.

### Key Research Questions
* **Combating Automation Bias:** As autonomous agents become more reliable and compile rates reach 100%, how do we prevent human reviewers from succumbing to automation bias (blindly approving pull requests)?
* **Explainable Code Transformations:** What representation format (e.g., semantic diffs, UML sequence diffs, explainability reasons) provides the highest cognitive relief for a human developer reviewing AI changes under pressure (e.g., at 4:00 AM)?
* **Collaborative Intent Signaling:** How should human developers express architectural intent to agents before development begins, so that the agent's reasoning matches the developer's mental model?
