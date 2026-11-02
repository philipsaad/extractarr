using Microsoft.AspNetCore.Mvc;
using Extractarr.Core.Extraction;
using Extractarr.Core.Instances;
using Extractarr.Core.Jobs;
using Extractarr.Core.Messaging.Commands;
using Extractarr.SignalR;

namespace Extractarr.Api.Instances;

[ApiController]
[Route("api/v3/instance")]
public class InstanceController : ControllerBase
{
    private readonly IArrInstanceRepository _repository;
    private readonly IArrApiClient _apiClient;
    private readonly IManageCommandQueue _commandQueue;
    private readonly IBroadcastSignalRMessage _signalR;

    public InstanceController(
        IArrInstanceRepository repository,
        IArrApiClient apiClient,
        IManageCommandQueue commandQueue,
        IBroadcastSignalRMessage signalR)
    {
        _repository = repository;
        _apiClient = apiClient;
        _commandQueue = commandQueue;
        _signalR = signalR;
    }

    [HttpGet]
    public ActionResult<List<ArrInstance>> GetAll()
    {
        return Ok(_repository.All().ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<ArrInstance> GetById(int id)
    {
        var item = _repository.Find(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public ActionResult<ArrInstance> Create([FromBody] ArrInstance model)
    {
        model.Id = 0;
        if (string.IsNullOrWhiteSpace(model.HealthStatus))
        {
            model.HealthStatus = "Unknown";
        }
        var created = _repository.Insert(model);
        _signalR.BroadcastMessage("instance", "created", created);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public ActionResult<ArrInstance> Update(int id, [FromBody] ArrInstance model)
    {
        var existing = _repository.Find(id);
        if (existing == null) return NotFound();

        model.Id = id;
        var updated = _repository.Update(model);
        _signalR.BroadcastMessage("instance", "updated", updated);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        _repository.Delete(id);
        _signalR.BroadcastMessage("instance", "deleted", new { id });
        return NoContent();
    }

    [HttpPost("test")]
    public async Task<ActionResult<ArrConnectionTestResult>> TestConnection([FromBody] ArrInstance model, CancellationToken cancellationToken)
    {
        var result = await _apiClient.TestConnectionAsync(model, cancellationToken);
        if (model.Id > 0)
        {
            var existing = _repository.Find(model.Id);
            if (existing != null)
            {
                existing.HealthStatus = result.IsValid ? "Online" : "Error";
                existing.LastErrorMessage = result.IsValid ? null : result.Message;
                existing.Version = result.Version ?? existing.Version;
                _repository.Update(existing);
                _signalR.BroadcastMessage("instance", "updated", existing);
            }
        }
        return Ok(result);
    }

    [HttpPost("{id:int}/poll")]
    public ActionResult PollNow(int id)
    {
        var existing = _repository.Find(id);
        if (existing == null) return NotFound();

        var cmd = _commandQueue.Push(new PollArrInstancesCommand { InstanceId = id }, CommandPriority.High, CommandTrigger.Manual);
        return Ok(CommandQueueManager.ToDto(cmd));
    }
}

[ApiController]
[Route("api/v3/webhook")]
public class WebhookController : ControllerBase
{
    private readonly IExtractionLifecycleCoordinator _coordinator;

    public WebhookController(IExtractionLifecycleCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveWebhook([FromBody] ArrWebhookPayload payload, CancellationToken cancellationToken)
    {
        await _coordinator.HandleWebhookAsync(payload, cancellationToken);
        return Ok(new { status = "accepted", eventType = payload.EventType });
    }
}

