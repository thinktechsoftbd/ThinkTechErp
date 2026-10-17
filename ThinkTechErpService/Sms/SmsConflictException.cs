namespace ThinkTechErpService.Sms;

public sealed class SmsConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
