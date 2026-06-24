# Note — the off-game compile gate (how to verify C# without launching tModLoader)

Resolved 2026-06-24. The mod can be compile-checked off-game, which makes large
refactors safe without an in-game Build + Reload.

## Two gates

1. **Pure-logic gate (full green):** `dotnet test Tests/PerformanceProfiler.Tests.csproj`.
   The Tests project does **not** import `tModLoader.targets`; it links only
   runtime-free source files, so it both compiles and runs xUnit. Baseline: 69
   tests pass. Any source file added under a globbed path (`Profiling/Persistence/*.cs`,
   `Data/Streams/*.cs`) that references Terraria/ModLoader types must be added to the
   `<Compile Remove>` list in `Tests/PerformanceProfiler.Tests.csproj`, or the gate
   breaks (this is how `DbReadModel.cs` broke it — it reads the `PerformanceProfiler`
   Mod class's static `Database` property).

2. **Full-mod compile gate (Roslyn against real Terraria refs):**
   `dotnet msbuild PerformanceProfiler.csproj -v m -nologo 2>&1 | grep -cE 'error CS'`.
   The SDK CoreCompile step compiles the **entire** mod against the tModLoader
   reference assemblies (tMLMod.targets supplies them) and emits the DLL **before**
   tML's packaging step runs. When tModLoader is running it then fails at packaging
   with `TML003` (the `.tmod` file lock) — that failure is **expected and ignored**.
   So: `error CS` count `0` ⇒ the C# compiles clean mod-wide; a non-zero count ⇒
   real compile errors to read. This works whether or not the game is open.

## Why this matters

The in-game Build + Reload remains the only gate for runtime behaviour, hooks, and
visual/interaction states. But every *compile* error mod-wide is catchable off-game
via gate 2, and every pure-logic *behaviour* via gate 1. A namespace-move or
signature-change refactor (e.g. the Insights consolidation) is fully compile-verifiable
without the game.
