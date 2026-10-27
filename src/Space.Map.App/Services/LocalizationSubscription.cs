namespace Space.Map.App.Services;

/// <summary>
/// Subscribes a short-lived view-model row to <see cref="ILocalizationService.LanguageChanged"/>
/// without keeping it alive.
/// </summary>
/// <remarks>
/// Summaries are rebuilt on every level change, so a plain event handler would pin
/// every row ever created for the life of the process. The event is long-lived and
/// the rows are not, so the subscription is the wrong way round -- hold the row
/// weakly and let the GC reclaim it, unsubscribing lazily on the next notification.
/// </remarks>
internal sealed class LocalizationSubscription
{
    private readonly ILocalizationService _localization;
    private readonly WeakReference<Action> _callback;
    private readonly EventHandler _handler;
    private bool _active;

    public LocalizationSubscription(ILocalizationService localization, Action callback)
    {
        _localization = localization;
        _callback = new WeakReference<Action>(callback);
        _handler = OnLanguageChanged;
    }

    public void Attach()
    {
        if (_active)
        {
            return;
        }

        _localization.LanguageChanged += _handler;
        _active = true;
    }

    public void Detach()
    {
        if (!_active)
        {
            return;
        }

        _localization.LanguageChanged -= _handler;
        _active = false;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_callback.TryGetTarget(out var callback))
        {
            callback();
            return;
        }

        // The row is gone; stop paying for it.
        Detach();
    }
}
