namespace MediaButler.Wpf.UI.Services;

/// <summary>
/// Holds at most one pending dialog request. <see cref="Shared.ConfirmDialog"/> (rendered once at
/// the app root) subscribes to <see cref="OnChange"/> and renders whatever <see cref="Current"/>
/// describes; the caller's <c>await</c> completes when the dialog calls <see cref="Complete"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    public enum DialogMode { Confirm, Prompt, Choose }

    public sealed class DialogRequest
    {
        public required DialogMode Mode { get; init; }
        public required string Title { get; init; }
        public string Message { get; init; } = "";
        public string AcceptText { get; init; } = "OK";
        public string CancelText { get; init; } = "Cancel";
        public string InitialValue { get; init; } = "";
        public int MaxLength { get; init; } = 260;
        public string[] Options { get; init; } = [];
    }

    public DialogRequest? Current { get; private set; }
    public event Action? OnChange;

    private TaskCompletionSource<object?>? pending;

    public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancelText) =>
        Show(new DialogRequest
        {
            Mode = DialogMode.Confirm,
            Title = title,
            Message = message,
            AcceptText = acceptText,
            CancelText = cancelText,
        }, defaultResult: false);

    public Task<string?> PromptAsync(string title, string message, string initialValue, int maxLength) =>
        Show(new DialogRequest
        {
            Mode = DialogMode.Prompt,
            Title = title,
            Message = message,
            InitialValue = initialValue,
            MaxLength = maxLength,
        }, defaultResult: (string?)null);

    public Task<string?> ChooseAsync(string title, string cancelText, params string[] options) =>
        Show(new DialogRequest
        {
            Mode = DialogMode.Choose,
            Title = title,
            CancelText = cancelText,
            Options = options,
        }, defaultResult: (string?)null);

    private Task<T> Show<T>(DialogRequest request, T defaultResult)
    {
        Current = request;
        var tcs = new TaskCompletionSource<object?>();
        pending = tcs;
        OnChange?.Invoke();
        return Resolve(tcs.Task, defaultResult);
    }

    private static async Task<T> Resolve<T>(Task<object?> task, T defaultResult)
    {
        var result = await task;
        return result is T typed ? typed : defaultResult;
    }

    /// <summary>Called by <see cref="Shared.ConfirmDialog"/> when the user answers or cancels.</summary>
    internal void Complete(object? result)
    {
        Current = null;
        var p = pending;
        pending = null;
        p?.TrySetResult(result);
        OnChange?.Invoke();
    }
}
