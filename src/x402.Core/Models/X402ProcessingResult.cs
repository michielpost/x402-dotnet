using x402.Core.Models.Facilitator;

namespace x402.Core.Models;

/// <summary>
/// Contains the complete result of X402 payment processing, including all information needed to generate HTTP responses.
/// </summary>
public record X402ProcessingResult
{
    /// <summary>
    /// Whether the request can continue (payment was successful).
    /// </summary>
    public bool CanContinueRequest { get; init; }

    /// <summary>
    /// The payment requirements used for processing.
    /// </summary>
    public PaymentRequirements PaymentRequirements { get; init; } = null!;

    /// <summary>
    /// Error message if processing failed.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The verification response from the facilitator.
    /// </summary>
    public VerificationResponse? VerificationResponse { get; init; }

    /// <summary>
    /// The settlement response from the facilitator.
    /// </summary>
    public SettlementResponse? SettlementResponse { get; init; }

    /// <summary>
    /// The HTTP status code to return.
    /// </summary>
    public int StatusCode { get; init; }

    /// <summary>
    /// The payment payload header that was processed.
    /// </summary>
    public PaymentPayloadHeader? PaymentPayload { get; init; }

    /// <summary>
    /// The full URL that was processed.
    /// </summary>
    public string FullUrl { get; init; } = string.Empty;

    /// <summary>
    /// Whether settlement was performed pessimistically (before response).
    /// </summary>
    public bool PessimisticSettlement { get; init; }

    /// <summary>
    /// Exception that occurred during settlement, if any.
    /// </summary>
    public Exception? SettlementException { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static X402ProcessingResult Success(
        PaymentRequirements paymentRequirements,
        VerificationResponse verificationResponse,
        SettlementResponse? settlementResponse = null,
        PaymentPayloadHeader? paymentPayload = null,
        string fullUrl = "",
        bool pessimisticSettlement = false)
    {
        return new X402ProcessingResult
        {
            CanContinueRequest = true,
            PaymentRequirements = paymentRequirements,
            VerificationResponse = verificationResponse,
            SettlementResponse = settlementResponse,
            PaymentPayload = paymentPayload,
            FullUrl = fullUrl,
            StatusCode = 200, // Will be overridden by actual response
            PessimisticSettlement = pessimisticSettlement
        };
    }

    /// <summary>
    /// Creates an error result.
    /// </summary>
    public static X402ProcessingResult CreateError(
        PaymentRequirements paymentRequirements,
        string error,
        int statusCode,
        VerificationResponse? verificationResponse = null,
        SettlementResponse? settlementResponse = null,
        PaymentPayloadHeader? paymentPayload = null,
        string fullUrl = "",
        Exception? settlementException = null)
    {
        return new X402ProcessingResult
        {
            CanContinueRequest = false,
            PaymentRequirements = paymentRequirements,
            Error = error,
            StatusCode = statusCode,
            VerificationResponse = verificationResponse,
            SettlementResponse = settlementResponse,
            PaymentPayload = paymentPayload,
            FullUrl = fullUrl,
            SettlementException = settlementException
        };
    }
}
