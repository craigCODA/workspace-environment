namespace Workspace.Desktop.Core.Voice;

public sealed class VoiceRuntimeUnavailableException : Exception
{
    public VoiceRuntimeUnavailableException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }

    public bool IsRecoverable => true;
}
