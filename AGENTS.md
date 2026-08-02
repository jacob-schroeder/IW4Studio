# AGENTS

## Engineering Priorities

Correctly managing code complexity, cleanliness, and orderliness must be a focal point of every design and implementation decision.

- Prefer the simplest design that fully satisfies the documented requirements.
- Keep responsibilities clearly separated and abstractions purposeful.
- Maintain coherent project structure, naming, formatting, and dependency direction.
- Avoid unnecessary layers, duplication, hidden coupling, premature generalization, and oversized modules or functions.
- Refactor when needed to keep the code understandable and maintainable, without expanding scope beyond the specifications and plan.
- Treat readability, testability, and ease of future change as required qualities rather than optional polish.
- Leave touched code at least as clean and orderly as it was before the change.
- Do not preserve backward compatibility.
- Choose the simplest implementation that fully meets the current requirements.
- Prefer established, well-maintained libraries over custom implementations.

When tradeoffs arise, explicitly evaluate their effect on complexity and maintainability alongside functional correctness.

## Repository and workspace layout

- C# solution root: `/Users/jacob/Repositories/IW4Studio`
- C# source projects: `/Users/jacob/Repositories/IW4Studio/src`
- C# test projects: `/Users/jacob/Repositories/IW4Studio/tests`
- Shared application resources: `/Users/jacob/Repositories/IW4Studio/Resources`
- Reverse-engineering workspace root: `/Users/jacob/Repositories/MW2`
- Ghidra persistent project: `/Users/jacob/Repositories/MW2/dissassembled/ps3/default_mp`
- Official fastfiles: `/Users/jacob/Repositories/MW2/fastfiles`
- Ghidra scripts: `/Users/jacob/Repositories/MW2/scripts`
- Raw/runtime dumps: `/Users/jacob/Repositories/MW2/dumps`
- Handoffs: Markdown files under `/Users/jacob/Repositories/MW2/handoffs`

## Headless Ghidra

Use `analyzeHeadless` from the workspace project folder:

```bash
/opt/homebrew/Cellar/ghidra/12.1.1/libexec/support/analyzeHeadless \
  /Users/jacob/Repositories/MW2/dissassembled/ps3/default_mp \
  default_mp \
  -process /Users/jacob/Repositories/MW2/assemblies/ps3/default_mp.elf \
  -scriptPath /Users/jacob/Repositories/MW2/scripts \
  -postScript <ScriptName>.java \
  -noanalysis
```

Add or remove `-noanalysis` based on whether the script writes project state.

## Repository hygiene

- Keep reverse-engineering evidence (dumps, scripts, traces, graphs, handoffs, etc.) outside `/Users/jacob/Repositories/IW4Studio`.
- Do not create dumps or tooling scripts in `/Users/jacob/Repositories/IW4Studio`; only C# solution files belong there.
- Keep the repository focused on solution source and support code.

## Testing rules

- Testing should be done sparingly, only create tests for something that is absolutely 100% necessary, not just because you implemented some internal interface or method.
- Running any tests at all automatically is strictly forbidden. Any tests you want to run will need manual approval by me.

## YAGNI principle
- Apply YAGNI to speculative requirements and premature abstraction, not to correctness, security, testing, maintainability, or explicitly requested product quality.
