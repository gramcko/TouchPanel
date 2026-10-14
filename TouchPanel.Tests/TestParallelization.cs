// WPF resource loading is process-global: the compiled-BAML loader and Application resource
// dictionaries are not safe to exercise from multiple STA threads at once. The template tests
// (Application.LoadComponent) and the HwndSource/STA window tests therefore race when they run
// in parallel within the xUnit host, producing intermittent failures in full-suite runs.
//
// The suite is small (~724 tests, ~1s), so serializing the whole assembly is the most reliable
// minimal fix and removes the race for every current and future WPF resource test.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
