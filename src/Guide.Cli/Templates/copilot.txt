# Copilot Instructions

- Follow the Clean Architecture boundaries: Core is shared; other projects are isolated.
- Avoid introducing compiler warnings (TreatWarningsAsErrors is enabled).
- Write C# code targeting .NET 8.0 following standard PascalCase naming conventions.
- Keep C# syntax parsing resilient and protect against null reference exceptions in CSharpSyntaxWalker.
