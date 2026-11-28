using Microsoft.AspNetCore.Mvc;
using BlazorWasm.Grains;
using BlazorWasm.Models;
using System.ComponentModel.DataAnnotations;

namespace Sample.Silo.Api;

[ApiController]
[Route("api/todo")]
public class TodoController : ControllerBase
{
    private readonly IGrainFactory _factory;

    public TodoController(IGrainFactory factory) => _factory = factory;

    [HttpGet("{itemKey}")]
    public Task<TodoItem?> GetAsync([Required] Guid itemKey) =>
        _factory.GetGrain<ITodoGrain>(itemKey).GetAsync();

    [HttpDelete("{itemKey}")]
    public Task DeleteAsync([Required] Guid itemKey) =>
        _factory.GetGrain<ITodoGrain>(itemKey).ClearAsync();

    [HttpGet("list/{ownerKey}", Name = "list")]
    public async Task<IEnumerable<TodoItem>> ListAsync([Required] Guid ownerKey)
    {
        // 获取此用户的所有待办事项键。
        var itemKeys = await _factory
            .GetGrain<ITodoManagerGrain>(ownerKey)
            .GetAllAsync();

        // 扇出调用，并行从集群获取各个事项。
        // 同时发起所有单独的请求。
        var tasks = itemKeys
            .Select(async itemId =>
            {
                var item = await _factory
                    .GetGrain<ITodoGrain>(itemId)
                    .GetAsync();

                // 如果对应 Grain 未能取消注册，可能会得到 null 结果。
                // 此时可以在这里完成清理工作。
                if (item is null)
                {
                    await _factory
                        .GetGrain<ITodoManagerGrain>(ownerKey)
                        .UnregisterAsync(itemId);
                }

                return item;
            });

        var result = await Task.WhenAll(tasks);

        return result.OfType<TodoItem>();
    }

    [HttpPost]
    public async Task<ActionResult> PostAsync([FromBody] TodoItemModel model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var item = new TodoItem(model.Key, model.Title, model.IsDone, model.OwnerKey, DateTime.UtcNow);
        await _factory.GetGrain<ITodoGrain>(item.Key).SetAsync(item);
        return Ok();
    }

    public record class TodoItemModel(
        [Required] Guid Key,
        [Required] string Title,
        [Required] bool IsDone,
        [Required] Guid OwnerKey);
}
