using Jarvis.Mind.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Jarvis.Mind.Api.Controllers;

[ApiController]
[Route("api/mind-map")]
public sealed class MindMapController : ControllerBase
{
    private readonly IMindMapAssembler _assembler;
    private readonly INodeDetailService _detailService;

    public MindMapController(IMindMapAssembler assembler, INodeDetailService detailService)
    {
        _assembler = assembler;
        _detailService = detailService;
    }

    [HttpGet]
    public async Task<IActionResult> GetSkeleton(CancellationToken ct)
    {
        var skeleton = await _assembler.AssembleAsync(ct);
        return Ok(skeleton);
    }

    [HttpGet("node/{id}")]
    public async Task<IActionResult> GetNodeDetail(string id, CancellationToken ct)
    {
        var detail = await _detailService.GetAsync(id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }
}