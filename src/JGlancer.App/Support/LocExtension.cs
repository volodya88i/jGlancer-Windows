using Avalonia.Markup.Xaml;
using JGlancer.Core.Localization;

namespace JGlancer.App.Support;

/// <summary>
/// <c>{l:T 'Add Folder…'}</c> in XAML: the localized string for an English key (resolved once, when the view is
/// built; the language changes at the next launch, as on macOS).
/// </summary>
internal sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
