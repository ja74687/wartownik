namespace Wartownik.Dialogs;

public interface IConfirmationDialog
{
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken cancellationToken = default);
}

public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel,
    bool IsDestructive = false);
