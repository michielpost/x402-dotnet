using Microsoft.AspNetCore.Mvc;
using x402.Core.Models.Facilitator;

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

        var payer = req.PaymentPayload.ExtractPayerFromPayload();

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
        var payer = req.PaymentPayload.ExtractPayerFromPayload();

        return new()
        {
            Success = true,
            Payer = payer,
            Network = req.PaymentRequirements.Network,
            Transaction = "0xFacilitatorMockServer"
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
