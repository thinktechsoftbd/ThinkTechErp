namespace ThinkTechErpService.Security;

public sealed class SecurityConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
