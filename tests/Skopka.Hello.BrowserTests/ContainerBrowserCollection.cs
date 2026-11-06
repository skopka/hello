namespace Skopka.Hello.BrowserTests;

// Container network changes must not interrupt other classes' local browser requests.
[CollectionDefinition("Container-backed browser tests", DisableParallelization = true)]
public sealed class ContainerBrowserTestGroup;
