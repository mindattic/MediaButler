namespace MediaButler.Wpf.UI.Services;

/// <summary>
/// Accessible modal dialog service. Replaces MAUI's <c>DisplayAlertAsync</c> /
/// <c>DisplayPromptAsync</c> / <c>DisplayActionSheetAsync</c> with a single
/// reusable dialog (<see cref="Shared.ConfirmDialog"/>) that implements
/// focus-trap / Escape-to-cancel / focus-restore itself.
/// </summary>
public interface IDialogService
{
    /// <summary>Two-button confirm. Replaces <c>DisplayAlertAsync</c>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText);

    /// <summary>Single free-text field. Replaces <c>DisplayPromptAsync</c>. Null result = cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string initialValue, int maxLength);

    /// <summary>A list of named choices plus an implicit cancel. Replaces <c>DisplayActionSheetAsync</c>.</summary>
    Task<string?> ChooseAsync(string title, string cancelText, params string[] options);
}
