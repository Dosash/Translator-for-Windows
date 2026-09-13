using Xunit;

// Core tests mutate process-wide static state (L10n.Selected, CultureInfo.CurrentUICulture in some
// environments). Run collections serially so tests don't interfere with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
