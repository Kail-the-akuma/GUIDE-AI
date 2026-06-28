# Contributing to GUIDE

Thank you for your interest in contributing to the GUIDE platform! Whether you are proposing changes to our philosophy, reporting a bug, suggesting a new feature, or contributing to the C# reference implementation, we welcome your involvement.

---

## 1. Contributing Ideas & Philosophy

GUIDE is as much an engineering philosophy as it is a codebase. If you want to challenge or suggest revisions to the principles:
* Check out [Challenge-GUIDE.md](file:///c:/Users/Hugo/Documents/GitHub/EngenieerAssistance/docs/community/Challenge-GUIDE.md) to read existing FAQs and design trade-offs.
* Open an issue or start a discussion detailing your proposal, backing it with concrete code patterns or real-world development scenarios.

---

## 2. Development Setup

The GUIDE reference implementation is built using **.NET 8** and C#.

### Prerequisites
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer.
* A suitable IDE, such as Visual Studio 2022, JetBrains Rider, or VS Code with the C# Dev Kit.
* Git.

### Building the Project
Clone the repository and build the C# solution:
```bash
dotnet build
```

### Running Unit and Integration Tests
To execute the test suite, run:
```bash
dotnet test
```

To run the end-to-end verification script (using PowerShell):
```powershell
tests/qa_verification_suite.ps1
```

---

## 3. Code Guidelines & Standards

When contributing code, please ensure your changes adhere to our architectural and style guidelines:

* **No External LLM Dependencies:** The semantic engine must maintain complete offline determinism. Do not add NuGet packages or HTTP integrations with LLM providers (e.g., OpenAI, Semantic Kernel, Google Generative AI) in the library projects.
* **Separation of Concerns:** Keep core models and contracts inside `Guide.Core`. Do not reference database logic (`Guide.Memory`, `Guide.Knowledge`) inside the parsing layers.
* **Brace Style:** Use Allman-style braces (braces on new lines).
* **Test Coverage:** Ensure every new component, parsing logic, or validation rule is covered by unit tests in the `tests/` directory.
* **Clean Code:** Write code that is simple and readable. Write code that a tired developer can easily debug at 4:00 AM. Avoid complex, clever, or hard-to-follow constructs.

---

## 4. Pull Request Process

1. **Fork the Repository:** Create a feature branch off of the main development branch.
2. **Implement Changes:** Write clean code, add tests, and verify that the build succeeds and all tests pass.
3. **Format Code:** Ensure your code is properly formatted before submitting.
4. **Submit PR:** Open a Pull Request on GitHub. Provide a clear description of the problem solved, the implementation details, and references to any relevant issues.
