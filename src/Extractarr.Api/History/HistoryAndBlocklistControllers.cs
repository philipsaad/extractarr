using Microsoft.AspNetCore.Mvc;
using Extractarr.Core.Datastore;
using Extractarr.Core.Extraction;
using Extractarr.SignalR;

namespace Extractarr.Api.History;

[ApiController]
[Route("api/v3/history")]
public class HistoryController : ControllerBase
{
    private readonly IExtractionHistoryRepository _historyRepository;

    public HistoryController(IExtractionHistoryRepository historyRepository)
    {
        _historyRepository = historyRepository;
    }

    [HttpGet]
    public ActionResult<PagingSpec<ExtractionHistory>> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sortKey = "Timestamp",
        [FromQuery] string sortDirection = "descending",
        [FromQuery] string? eventType = null,
        [FromQuery] string? search = null)
    {
        var spec = new PagingSpec<ExtractionHistory>
        {
            Page = page,
            PageSize = pageSize,
            SortKey = sortKey,
            SortDirection = sortDirection
        };

        var clauses = new List<string>();
        var dyn = new Dapper.DynamicParameters();

        if (!string.IsNullOrWhiteSpace(eventType) && !eventType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            clauses.Add("LOWER(\"EventType\") = LOWER(@EventType)");
            dyn.Add("EventType", eventType.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            clauses.Add("(\"ReleaseTitle\" LIKE @Search OR \"ArchivePath\" LIKE @Search OR \"InstanceName\" LIKE @Search)");
            dyn.Add("Search", $"%{search.Trim()}%");
        }

        var whereClause = clauses.Count > 0 ? string.Join(" AND ", clauses) : null;
        return Ok(_historyRepository.GetPaged(spec, whereClause, dyn));
    }

    [HttpGet("since")]
    public ActionResult<List<ExtractionHistory>> GetSince([FromQuery] DateTime? date = null)
    {
        var sinceDate = date ?? DateTime.UtcNow.AddDays(-7);
        return Ok(_historyRepository.GetSince(sinceDate));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        _historyRepository.Delete(id);
        return NoContent();
    }
}

public class BlocklistRetryRequest
{
    public string? ManualPassword { get; set; }
}

public class BulkDeleteRequest
{
    public List<int> Ids { get; set; } = new();
}

[ApiController]
[Route("api/v3/blocklist")]
public class BlocklistController : ControllerBase
{
    private readonly IBlocklistRepository _blocklistRepository;
    private readonly IExtractionLifecycleCoordinator _coordinator;
    private readonly IBroadcastSignalRMessage _signalR;

    public BlocklistController(
        IBlocklistRepository blocklistRepository,
        IExtractionLifecycleCoordinator coordinator,
        IBroadcastSignalRMessage signalR)
    {
        _blocklistRepository = blocklistRepository;
        _coordinator = coordinator;
        _signalR = signalR;
    }

    [HttpGet]
    public ActionResult<PagingSpec<BlocklistItem>> GetBlocklist(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sortKey = "CreatedAt",
        [FromQuery] string sortDirection = "descending")
    {
        var spec = new PagingSpec<BlocklistItem>
        {
            Page = page,
            PageSize = pageSize,
            SortKey = sortKey,
            SortDirection = sortDirection
        };
        return Ok(_blocklistRepository.GetPaged(spec));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Remove(int id)
    {
        _blocklistRepository.Delete(id);
        _signalR.BroadcastMessage("blocklist", "deleted", new { id });
        return NoContent();
    }

    [HttpDelete("bulk")]
    public IActionResult RemoveBulk([FromBody] BulkDeleteRequest request)
    {
        _blocklistRepository.DeleteMany(request.Ids);
        _signalR.BroadcastMessage("blocklist", "updated", new { ids = request.Ids });
        return NoContent();
    }

    [HttpPost("{id:int}/retry")]
    public async Task<IActionResult> RetryWithPassword(int id, [FromBody] BlocklistRetryRequest? request, CancellationToken cancellationToken)
    {
        var job = await _coordinator.RetryBlocklistItemAsync(id, request?.ManualPassword, cancellationToken);
        return Ok(new { retried = true, job });
    }
}

