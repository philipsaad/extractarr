using Microsoft.AspNetCore.Mvc;
using Extractarr.Common.Disk;
using Extractarr.Core.Jobs;
using Extractarr.Core.Messaging.Commands;
using Extractarr.Core.RemotePathMappings;
using Extractarr.Core.WatchFolders;
using Extractarr.SignalR;

namespace Extractarr.Api.WatchFolders;

[ApiController]
[Route("api/v3/watchfolder")]
public class WatchFolderController : ControllerBase
{
    private readonly IWatchFolderRepository _repository;
    private readonly IWatchFolderService _watchFolderService;
    private readonly IManageCommandQueue _commandQueue;
    private readonly IBroadcastSignalRMessage _signalR;

    public WatchFolderController(
        IWatchFolderRepository repository,
        IWatchFolderService watchFolderService,
        IManageCommandQueue commandQueue,
        IBroadcastSignalRMessage signalR)
    {
        _repository = repository;
        _watchFolderService = watchFolderService;
        _commandQueue = commandQueue;
        _signalR = signalR;
    }

    [HttpGet]
    public ActionResult<List<WatchFolder>> GetAll()
    {
        return Ok(_repository.All().ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<WatchFolder> GetById(int id)
    {
        var item = _repository.Find(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public ActionResult<WatchFolder> Create([FromBody] WatchFolder model)
    {
        model.Id = 0;
        model.Path = OsPathTranslator.TranslateToCurrentOs(model.Path);
        if (!string.IsNullOrWhiteSpace(model.TargetPath))
        {
            model.TargetPath = OsPathTranslator.TranslateToCurrentOs(model.TargetPath);
        }
        var created = _repository.Insert(model);
        _watchFolderService.RefreshWatchers();
        _signalR.BroadcastMessage("watchfolder", "created", created);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public ActionResult<WatchFolder> Update(int id, [FromBody] WatchFolder model)
    {
        var existing = _repository.Find(id);
        if (existing == null) return NotFound();
        model.Id = id;
        model.Path = OsPathTranslator.TranslateToCurrentOs(model.Path);
        if (!string.IsNullOrWhiteSpace(model.TargetPath))
        {
            model.TargetPath = OsPathTranslator.TranslateToCurrentOs(model.TargetPath);
        }
        var updated = _repository.Update(model);
        _watchFolderService.RefreshWatchers();
        _signalR.BroadcastMessage("watchfolder", "updated", updated);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        _repository.Delete(id);
        _watchFolderService.RefreshWatchers();
        _signalR.BroadcastMessage("watchfolder", "deleted", new { id });
        return NoContent();
    }

    [HttpPost("{id:int}/scan")]
    public ActionResult ScanNow(int id)
    {
        var existing = _repository.Find(id);
        if (existing == null) return NotFound();
        var cmd = _commandQueue.Push(new ScanWatchFoldersCommand { WatchFolderId = id }, CommandPriority.High, CommandTrigger.Manual);
        return Ok(CommandQueueManager.ToDto(cmd));
    }
}

[ApiController]
[Route("api/v3/remotepathmapping")]
public class RemotePathMappingController : ControllerBase
{
    private readonly IRemotePathMappingRepository _repository;

    public RemotePathMappingController(IRemotePathMappingRepository repository)
    {
        _repository = repository;
    }

    [HttpGet]
    public ActionResult<List<RemotePathMapping>> GetAll()
    {
        return Ok(_repository.All().ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<RemotePathMapping> GetById(int id)
    {
        var item = _repository.Find(id);
        if (item == null) return NotFound();
        return Ok(item);
    }

    [HttpPost]
    public ActionResult<RemotePathMapping> Create([FromBody] RemotePathMapping model)
    {
        model.Id = 0;
        model.LocalPath = OsPathTranslator.TranslateToCurrentOs(model.LocalPath);
        var created = _repository.Insert(model);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public ActionResult<RemotePathMapping> Update(int id, [FromBody] RemotePathMapping model)
    {
        var existing = _repository.Find(id);
        if (existing == null) return NotFound();
        model.Id = id;
        model.LocalPath = OsPathTranslator.TranslateToCurrentOs(model.LocalPath);
        return Ok(_repository.Update(model));
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        _repository.Delete(id);
        return NoContent();
    }
}


