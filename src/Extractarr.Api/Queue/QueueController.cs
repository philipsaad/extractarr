using Microsoft.AspNetCore.Mvc;
using Extractarr.Core.Datastore;
using Extractarr.Core.Extraction;

namespace Extractarr.Api.Queue;

public class RetryJobRequest
{
    public string? ManualPassword { get; set; }
}

[ApiController]
[Route("api/v3/queue")]
public class QueueController : ControllerBase
{
    private readonly IExtractionJobRepository _jobRepository;
    private readonly IExtractionLifecycleCoordinator _coordinator;

    public QueueController(
        IExtractionJobRepository jobRepository,
        IExtractionLifecycleCoordinator coordinator)
    {
        _jobRepository = jobRepository;
        _coordinator = coordinator;
    }

    [HttpGet]
    public ActionResult<PagingSpec<ExtractionJob>> GetQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sortKey = "CreatedAt",
        [FromQuery] string sortDirection = "descending")
    {
        var active = _jobRepository.GetActiveQueue();
        var spec = new PagingSpec<ExtractionJob>
        {
            Page = page,
            PageSize = pageSize,
            SortKey = sortKey,
            SortDirection = sortDirection,
            TotalRecords = active.Count,
            Records = active
                .Skip((Math.Max(1, page) - 1) * pageSize)
                .Take(pageSize)
                .ToList()
        };
        return Ok(spec);
    }

    [HttpGet("details")]
    public ActionResult<List<ExtractionJob>> GetQueueDetails()
    {
        return Ok(_jobRepository.GetActiveQueue());
    }

    [HttpDelete("{id:int}")]
    public IActionResult CancelJob(int id)
    {
        var cancelled = _coordinator.CancelJob(id);
        if (!cancelled) return NotFound();
        return NoContent();
    }

    [HttpPost("{id:int}/force")]
    public async Task<ActionResult<ExtractionJob>> ForceExtract(int id, CancellationToken cancellationToken)
    {
        var updated = await _coordinator.ForceExtractJobAsync(id, cancellationToken);
        if (updated == null) return NotFound();
        return Ok(updated);
    }

    [HttpPost("{id:int}/retry")]
    public async Task<ActionResult<ExtractionJob>> RetryJob(int id, [FromBody] RetryJobRequest? request, CancellationToken cancellationToken)
    {
        var updated = await _coordinator.RetryJobAsync(id, request?.ManualPassword, cancellationToken);
        if (updated == null) return NotFound();
        return Ok(updated);
    }
}

