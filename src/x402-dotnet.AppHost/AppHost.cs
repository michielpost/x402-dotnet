var builder = DistributedApplication.CreateBuilder(args);

var facilitatorMock = builder.AddProject<Projects.x402_FacilitatorMock>("x402-facilitatormock");

builder.AddProject<Projects.x402_SampleWeb>("x402-sampleweb")
    .WithReference(facilitatorMock);

builder.Build().Run();
