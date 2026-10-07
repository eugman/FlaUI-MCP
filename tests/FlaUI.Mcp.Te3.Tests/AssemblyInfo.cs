using Xunit;

// System.Drawing/GDI+ encoder discovery is process-global. Parallel annotation and
// composition tests intermittently returned a null PNG encoder on Windows.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
