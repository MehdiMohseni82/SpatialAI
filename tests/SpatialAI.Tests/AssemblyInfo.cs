// The catalog is a process-wide singleton (FurnitureFactory.UseCatalog); serializing tests keeps a
// catalog-swap test from racing the many tests that build items from the default catalog.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
