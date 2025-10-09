using Microsoft.AspNetCore.Mvc;
using x402.Facilitator;
using x402.Facilitator.Models;
using x402.Models;

namespace x402.FacilitatorMock.Controllers;

[ApiController]
[Route("/")]
public class FacilitatorController : ControllerBase
{
    private readonly ILogger<FacilitatorController> _logger;

    public FacilitatorController(ILogger<FacilitatorController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    [Route("verify")]
    public VerificationResponse Verify([FromBody] FacilitatorRequest req)
    {
        if (req.X402Version != 1)
            return VerificationError(FacilitatorErrorCodes.InvalidX402Version);

        var paymentHeader = req.PaymentHeader as PaymentPayloadHeader;
        var payer = paymentHeader?.ExtractPayerFromPayload();

        return new()
        {
            IsValid = true,
            Payer = payer
        };
    }

    private VerificationResponse VerificationError(string invalidX402Version)
    {
        return new()
        {
            InvalidReason = invalidX402Version,
            IsValid = false
        };
    }

    [HttpPost]
    [Route("settle")]
    public SettlementResponse Settle([FromBody] FacilitatorRequest req)
    {
        var paymentHeader = req.PaymentHeader as PaymentPayloadHeader;
        var payer = paymentHeader?.ExtractPayerFromPayload();

        return new()
        {
            Success = true,
            Payer = payer,
            NetworkId = req.PaymentRequirements.Network,
            TxHash = "0xFacilitatorMockServer"
        };
    }

    [HttpGet]
    [Route("supported")]
    public List<FacilitatorKind> Supported()
    {
        return new()
        {
             new FacilitatorKind("USDC", "mainnet")
        };
    }
       
}
