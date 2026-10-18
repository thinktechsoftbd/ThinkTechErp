namespace ThinkTechErpService.Security;

public sealed class SecurityValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
