using Microsoft.AspNetCore.Mvc;
using x402.Facilitator.Models;

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
        return new()
        {
            IsValid = true
        };
    }

    [HttpPost]
    [Route("settle")]
    public SettlementResponse Settle([FromBody] FacilitatorRequest req)
    {
        return new()
        {
            Success = true,
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
