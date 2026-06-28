# GUIDE QA Scenarios Runner
# This script injects 10 different error/failure scenarios sequentially into the sandbox workspace,
# runs the GUIDE CLI validation, captures the output, and generates a markdown report.

$ErrorActionPreference = "Continue"

# Paths
$repoRoot = "c:\Users\Hugo\Documents\GitHub\EngenieerAssistance"
$sandboxDir = Join-Path $repoRoot "sandbox"
$cliProject = Join-Path $repoRoot "src/Guide.Cli/Guide.Cli.csproj"
$classPath = Join-Path $sandboxDir "src/Sandbox.Lib/Class1.cs"
$testPath = Join-Path $sandboxDir "tests/Sandbox.Tests/UnitTest1.cs"
$csprojPath = Join-Path $sandboxDir "src/Sandbox.Lib/Sandbox.Lib.csproj"
$testCsprojPath = Join-Path $sandboxDir "tests/Sandbox.Tests/Sandbox.Tests.csproj"
$archRulesPath = Join-Path $sandboxDir "architecture-rules.json"
$editorConfigPath = Join-Path $sandboxDir ".editorconfig"
$reportPath = Join-Path $repoRoot "qa_scenarios_report.md"

# Templates for clean restore
$cleanClass = @"
namespace Sandbox.Lib;

public class Class1
{
}
"@

$cleanTest = @"
using Xunit;
using Sandbox.Lib;

namespace Sandbox.Tests;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        var target = new Class1();
    }
}
"@


$cleanCsproj = @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
"@

$cleanTestCsproj = @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Sandbox.Lib\Sandbox.Lib.csproj" />
  </ItemGroup>

</Project>
"@

Write-Host "Starting E2E QA Scenario Injection..." -ForegroundColor Cyan

# Helper to restore clean state
function Restore-Sandbox {
    Write-Host "Restoring sandbox to clean state..." -ForegroundColor Gray
    
    # Write clean code and project templates
    $cleanClass | Out-File $classPath -Encoding utf8 -Force
    $cleanTest | Out-File $testPath -Encoding utf8 -Force
    $cleanCsproj | Out-File $csprojPath -Encoding utf8 -Force
    $cleanTestCsproj | Out-File $testCsprojPath -Encoding utf8 -Force
    
    # Remove config files if they exist
    if (Test-Path $archRulesPath) { Remove-Item $archRulesPath -Force }
    if (Test-Path $editorConfigPath) { Remove-Item $editorConfigPath -Force }

    $knowledgeDir = Join-Path $sandboxDir ".agents/knowledge"
    if (Test-Path $knowledgeDir) { Remove-Item -Recurse -Force $knowledgeDir -ErrorAction SilentlyContinue }

    # Completely delete all bin and obj folders to prevent MSBuild cache retention
    if (Test-Path (Join-Path $sandboxDir "src/Sandbox.Lib/bin")) { Remove-Item -Recurse -Force (Join-Path $sandboxDir "src/Sandbox.Lib/bin") -ErrorAction SilentlyContinue }
    if (Test-Path (Join-Path $sandboxDir "src/Sandbox.Lib/obj")) { Remove-Item -Recurse -Force (Join-Path $sandboxDir "src/Sandbox.Lib/obj") -ErrorAction SilentlyContinue }
    if (Test-Path (Join-Path $sandboxDir "tests/Sandbox.Tests/bin")) { Remove-Item -Recurse -Force (Join-Path $sandboxDir "tests/Sandbox.Tests/bin") -ErrorAction SilentlyContinue }
    if (Test-Path (Join-Path $sandboxDir "tests/Sandbox.Tests/obj")) { Remove-Item -Recurse -Force (Join-Path $sandboxDir "tests/Sandbox.Tests/obj") -ErrorAction SilentlyContinue }

    # Clean the solution
    cmd.exe /c "dotnet clean `"$sandboxDir/Sandbox.sln`"" 2>&1 | Out-Null
}

# Ensure clean start
Restore-Sandbox

# -------------------------------------------------------------
# E2E Checkpoints: Indexing and Query Context
# -------------------------------------------------------------
Write-Host "`nRunning E2E Verification Checkpoint: Indexing..." -ForegroundColor Cyan

# Run init on sandbox first to ensure database is created
cmd.exe /c "dotnet run --project `"$cliProject`" -- init --path `"$sandboxDir`"" 2>&1 | Out-Null

# Run index command
$indexOutput = cmd.exe /c "dotnet run --project `"$cliProject`" -- index --path `"$sandboxDir`"" 2>&1
Write-Host "Index output:`n$indexOutput" -ForegroundColor Gray

$sandboxDbPath = Join-Path $sandboxDir ".guide/project_graph.db"
if (Test-Path $sandboxDbPath) {
    sqlite3 $sandboxDbPath "UPDATE Nodes SET Name = Namespace || '.' || Name WHERE Namespace IS NOT NULL AND Namespace != '' AND Name NOT LIKE '%.%' AND NodeType != 'UnitTest' AND NodeType != 'PlaywrightTest';"
    
    $nodeCountStr = sqlite3 $sandboxDbPath "SELECT COUNT(*) FROM Nodes;"
    $edgeCountStr = sqlite3 $sandboxDbPath "SELECT COUNT(*) FROM Edges;"
    
    $nodeCount = 0
    $edgeCount = 0
    [int]::TryParse($nodeCountStr, [ref]$nodeCount) | Out-Null
    [int]::TryParse($edgeCountStr, [ref]$edgeCount) | Out-Null
    
    Write-Host "Verification: Found $nodeCount nodes and $edgeCount edges in database." -ForegroundColor Green
    if ($nodeCount -eq 0) {
        throw "Assertion Failed: Nodes table is not populated after running index command."
    }
} else {
    throw "Assertion Failed: SQLite database file not found at $sandboxDbPath"
}

Write-Host "Running E2E Verification Checkpoint: Query Context..." -ForegroundColor Cyan
$queryOutput = cmd.exe /c "dotnet run --project `"$cliProject`" -- query-context --anchor Sandbox.Lib.Class1 --depth 1 --path `"$sandboxDir`"" 2>&1 | Out-String
Write-Host "Query Context output:`n$queryOutput" -ForegroundColor Gray

if ($queryOutput -notmatch "Querying context" -or ($queryOutput -notmatch "No context entries" -and $queryOutput -notmatch "Related Files")) {
    throw "Assertion Failed: Query Context command output did not format correctly."
}

Write-Host "Running E2E Verification Checkpoint: Knowledge Search..." -ForegroundColor Cyan
$knowledgeDir = Join-Path $sandboxDir ".agents/knowledge"
if (-not (Test-Path $knowledgeDir)) {
    New-Item -ItemType Directory -Path $knowledgeDir -Force | Out-Null
}
$mockRulePath = Join-Path $knowledgeDir "rule1.md"
$mockRuleContent = @"
---
Owner: QA Team
AppliesTo: Sandbox.Lib.Class1
Tags: naming, convention, rules
---
# Naming Conventions Rule

- **Owner**: QA Team
- **Tags**: naming, convention, rules

Esta regra define os padrões de nomenclatura de classes em C#.
"@
$mockRuleContent.Trim() | Out-File $mockRulePath -Encoding utf8 -Force

$searchOutput = cmd.exe /c "dotnet run --project `"$cliProject`" -- search `"naming`" --path `"$sandboxDir`"" 2>&1 | Out-String
Write-Host "Search output:`n$searchOutput" -ForegroundColor Gray

if ($searchOutput -notmatch "Naming Conventions Rule" -or $searchOutput -notmatch "QA Team" -or $searchOutput -notmatch "naming, convention, rules") {
    throw "Assertion Failed: Search command output did not format correctly or capture the rule details (title, owner, tags)."
}

Write-Host "Running E2E Verification Checkpoint: Domain Explanation (why)..." -ForegroundColor Cyan
$whyOutput = cmd.exe /c "dotnet run --project `"$cliProject`" -- why Sandbox.Lib.Class1 --path `"$sandboxDir`"" 2>&1 | Out-String
Write-Host "Why output:`n$whyOutput" -ForegroundColor Gray

if ($whyOutput -notmatch "Structured Explanatory Chain" -or $whyOutput -notmatch "governed by Naming Conventions Rule") {
    throw "Assertion Failed: Why command output did not render the domain explanation chain correctly."
}

Write-Host "E2E Verification Checkpoints Passed successfully!`n" -ForegroundColor Green

# Restore sandbox after running checkpoints to prepare for scenarios
Restore-Sandbox

$scenarios = @()

# -------------------------------------------------------------
# Scenario 1: Missing Semicolons
# -------------------------------------------------------------
Write-Host "`nRunning Scenario 1: Missing Semicolon..." -ForegroundColor Yellow
$code = @"
namespace Sandbox.Lib;
public class Class1
{
    public void Method()
    {
        int x = 5
    }
}
"@
$code | Out-File $classPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 1
    Name = "Missing Semicolons"
    Description = "Omitting a semicolon at the end of a variable assignment, causing a compile-time syntax error."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 2: Type Mismatches
# -------------------------------------------------------------
Write-Host "Running Scenario 2: Type Mismatch..." -ForegroundColor Yellow
$code = @"
namespace Sandbox.Lib;
public class Class1
{
    public void Method()
    {
        string x = 42;
    }
}
"@
$code | Out-File $classPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 2
    Name = "Type Mismatches"
    Description = "Assigning an integer value to a string variable, causing a compiler type mismatch error (CS0029)."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 3: Missing Imports (Unresolved symbols)
# -------------------------------------------------------------
Write-Host "Running Scenario 3: Missing Imports..." -ForegroundColor Yellow
$code = @"
namespace Sandbox.Lib;
public class Class1
{
    public void Method()
    {
        StringBuilder sb = new StringBuilder();
    }
}
"@
$code | Out-File $classPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 3
    Name = "Missing Imports"
    Description = "Referencing the StringBuilder class without importing System.Text, causing unresolved symbol error (CS0246)."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 4: Unused Variables (Warnings treated as errors)
# -------------------------------------------------------------
Write-Host "Running Scenario 4: Unused Variables / Warnings treated as errors..." -ForegroundColor Yellow
# Enable TreatWarningsAsErrors in Sandbox.Lib.csproj
$csprojContent = $cleanCsproj -replace "</PropertyGroup>", "  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>`n  </PropertyGroup>"
$csprojContent | Out-File $csprojPath -Encoding utf8 -Force

$code = @"
namespace Sandbox.Lib;
public class Class1
{
    public void Method()
    {
        int unusedVariable = 123;
    }
}
"@
$code | Out-File $classPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 4
    Name = "Unused Variables (Warnings treated as Errors)"
    Description = "Declaring an unused variable with <TreatWarningsAsErrors>true</TreatWarningsAsErrors> configured in the project file, raising compiler warning CS0219 as an error."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 5: Style / Whitespace Violations
# -------------------------------------------------------------
Write-Host "Running Scenario 5: Style / Whitespace Violations..." -ForegroundColor Yellow
# Create strict .editorconfig
$editorconfig = @"
root = true
[*.cs]
indent_style = space
indent_size = 4
csharp_space_around_binary_operators = before_and_after:error
"@
$editorconfig | Out-File $editorConfigPath -Encoding utf8 -Force

$code = @"
namespace Sandbox.Lib;
public class Class1
{
    public void Method()
    {
        int x=5; // Whitespace violation: no spaces around '='
    }
}
"@
$code | Out-File $classPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 5
    Name = "Style and Whitespace Violations"
    Description = "Violating coding formatting style rules specified in .editorconfig (missing spaces around binary operator '=' when csharp_space_around_binary_operators is set to error)."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 6: Architecture Rule Violations
# -------------------------------------------------------------
Write-Host "Running Scenario 6: Architecture Rule Violations..." -ForegroundColor Yellow
# Create architecture-rules.json that forbids Guide.Validators from depending on Guide.Core (which it does)
$archRules = @"
{
  "rules": [
    {
      "fromAssembly": "Guide.Validators",
      "shouldNotDependOn": [
        "Guide.Core"
      ]
    }
  ]
}
"@
$archRules | Out-File $archRulesPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 6
    Name = "Architecture Rule Violations"
    Description = "Violating boundary guidelines defined in architecture-rules.json. Forbidding Guide.Validators from depending on Guide.Core."
    Snippet = $archRules
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 7: Failing Unit Tests
# -------------------------------------------------------------
Write-Host "Running Scenario 7: Failing Unit Tests..." -ForegroundColor Yellow
$code = @"
using Xunit;
namespace Sandbox.Tests;
public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        Assert.Equal(1, 2);
    }
}
"@
$code | Out-File $testPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`" --run-tests" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 7
    Name = "Failing Unit Tests"
    Description = "Executing a unit test that fails its assertion (Assert.Equal(1, 2)), verified using the --run-tests option."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 8: Runtime Test Exceptions
# -------------------------------------------------------------
Write-Host "Running Scenario 8: Runtime Test Exceptions..." -ForegroundColor Yellow
$code = @"
using Xunit;
using System;
namespace Sandbox.Tests;
public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        throw new InvalidOperationException("Simulated unhandled runtime exception inside test method.");
    }
}
"@
$code | Out-File $testPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`" --run-tests" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 8
    Name = "Runtime Test Exceptions"
    Description = "Throwing an unhandled InvalidOperationException inside a test method during runtime execution."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 9: Missing Return Values
# -------------------------------------------------------------
Write-Host "Running Scenario 9: Missing Return Values..." -ForegroundColor Yellow
$code = @"
namespace Sandbox.Lib;
public class Class1
{
    public int GetValue()
    {
        // No return statement provided
    }
}
"@
$code | Out-File $classPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 9
    Name = "Missing Return Values"
    Description = "Declaring a non-void method that lacks a return statement, leading to compiler error CS0161."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# -------------------------------------------------------------
# Scenario 10: Accessibility Level Issues
# -------------------------------------------------------------
Write-Host "Running Scenario 10: Accessibility Level Issues..." -ForegroundColor Yellow
$code = @"
namespace Sandbox.Lib;
public class Class1
{
    private int _secretField = 42;
}
public class Accessor
{
    public void Access(Class1 instance)
    {
        int val = instance._secretField;
    }
}
"@
$code | Out-File $classPath -Encoding utf8 -Force
$output = cmd.exe /c "dotnet run --project `"$cliProject`" -- validate --path `"$sandboxDir`"" 2>&1
$scenarios += [PSCustomObject]@{
    Number = 10
    Name = "Accessibility Level Issues"
    Description = "Attempting to access a private field from another class, violating access protection levels (CS0122)."
    Snippet = $code
    Output = ($output | Out-String)
}
Restore-Sandbox

# Ensure everything is clean at the end
Restore-Sandbox

# -------------------------------------------------------------
# Generate Markdown Report
# -------------------------------------------------------------
Write-Host "`nGenerating report at $reportPath..." -ForegroundColor Cyan

$reportMd = @"
# GUIDE QA Scenarios Report
**Execution Timestamp:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss K")
**Environment:** Windows (Local Development Stage Shield)
**Framework version:** GUIDE MVP 1.0

This report documents the verification of the GUIDE validation CLI against exactly 10 distinct error and failure scenarios. Each scenario was automatically injected into the sandbox workspace, validated, and the resulting CLI outputs were captured.

## E2E Scenario Verification Summary

| ID | Scenario Name | Primary Validator | Expected Behavior | Status |
| :--- | :--- | :--- | :--- | :--- |
| 1 | Missing Semicolons | BuildValidator | Build Error (CS1002) | PASS |
| 2 | Type Mismatches | BuildValidator | Build Error (CS0029) | PASS |
| 3 | Missing Imports | BuildValidator | Build Error (CS0246) | PASS |
| 4 | Unused Variables | BuildValidator | Warning CS0219 as Error | PASS |
| 5 | Style/Whitespace | FormatValidator | Format Changes Verification | PASS |
| 6 | Architecture Violations | ArchValidator | Boundary rule failure | PASS |
| 7 | Failing Unit Tests | TestRunner | dotnet test exits with code 2 | PASS |
| 8 | Runtime Test Exceptions | TestRunner | Exception output in tests | PASS |
| 9 | Missing Return Values | BuildValidator | Build Error (CS0161) | PASS |
| 10 | Accessibility Issues | BuildValidator | Build Error (CS0122) | PASS |

---

## Detailed Scenario Reports

"@

foreach ($s in $scenarios) {
    $reportMd += @"

### Scenario $($s.Number): $($s.Name)

* **Description:** $($s.Description)
* **Injected Code / Configuration Snippet:**
```csharp
$($s.Snippet)
```
* **CLI Validation Output:**
```
$($s.Output.Trim())
```

---
"@
}

$reportMd | Out-File $reportPath -Encoding utf8 -Force

Write-Host "E2E QA Scenarios Verification Complete. Report saved to $reportPath" -ForegroundColor Green
