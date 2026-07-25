# EventManager — Coding Standards

Project-specific coding guidelines applied to all generated and hand-written code. These supplement
the AI-DLC workflow; code generation must honor them.

## CS-1 — Avoid the ternary conditional operator (`?:`)  *(added 2026-07-25)*
**Rule**: Do not use the ternary conditional operator `condition ? a : b`. Prefer clearer alternatives:
- a plain `if`/`else` statement, or an early-return guard;
- a `switch` expression when selecting among several cases;
- a well-named local variable or helper method when the expression is non-trivial.

**Scope**: This applies to the `?:` conditional operator only. The **null-coalescing** operator (`??`),
**null-coalescing assignment** (`??=`), and **null-conditional** access (`?.`, `?[]`) are **allowed** —
they are not ternary conditionals and generally read clearly.

**Why**: Ternaries — especially nested or compound ones — hurt readability and reviewability; explicit
control flow is easier to scan, debug, and extend.

**How to apply**:
```csharp
// Avoid:
var status = pay.IsSuccess ? "Paid" : "Owed";

// Prefer:
string status = "Owed";
if (pay.IsSuccess) status = "Paid";

// Or a switch expression for multiple cases:
var code = error.Type switch
{
    ErrorType.NotFound => 404,
    ErrorType.Conflict => 409,
    _ => 400,
};
```
