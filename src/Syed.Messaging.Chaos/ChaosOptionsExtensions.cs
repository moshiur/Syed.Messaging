namespace Syed.Messaging.Chaos;

/// <summary>
/// Fluent helpers on <see cref="ChaosOptions"/>.
/// </summary>
public static class ChaosOptionsExtensions
{
    /// <summary>
    /// Replace the default chaos injector with a custom implementation. The
    /// type is registered as a singleton and must implement
    /// <see cref="IChaosInjector"/>.
    /// </summary>
    public static ChaosOptions UseInjector<TInjector>(this ChaosOptions options)
        where TInjector : class, IChaosInjector
    {
        options.CustomInjectorType = typeof(TInjector);
        return options;
    }
}
