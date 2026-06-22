// Test harness configuration + namespace stubs.

// xUnit parallelises test classes by default. The persistence tests construct
// ProfilerDatabase, which mutates the shared static BsonMapper.Global via
// BsonShortNames.Apply. Two test classes doing that concurrently race on the
// non-thread-safe global mapper and corrupt its entity table mid-registration
// (surfaces as spurious "Member 'X' not found in type ..." errors even though
// the member is a real property). Production calls Apply once per process, so
// this race is test-only. The suite is tiny (~70 tests, sub-second), so serial
// execution costs nothing and removes the shared-global race.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

// --- Namespace stubs ---
//
// The v0.10/v0.11/v0.12 reorganisation gave essentially every source file a
// uniform "using" header that imports the whole Data.* namespace set, including
// Data.Collectors and Data.Aggregators.Segments. The test project deliberately
// does NOT compile those two namespaces' real files — the Collectors are
// inherently Terraria-coupled (they read live game state via MetricCollector),
// and the Segments detector/store pull the same pipeline in. Those header
// usings are inert in the lifted Terraria-free files (no type from these
// namespaces is actually referenced), but without the namespace existing they
// fail the test compile with CS0234.
//
// Declaring the namespaces (empty) here lets the otherwise-unused header usings
// resolve without dragging the real, Terraria-coupled types into the test
// runner. If a lifted file ever genuinely USES a type from these namespaces,
// that surfaces as CS0246 (not CS0234) and the real source must be linked
// instead of stubbed.
namespace PerformanceProfiler.Data.Collectors { }
namespace PerformanceProfiler.Data.Aggregators.Segments { }
