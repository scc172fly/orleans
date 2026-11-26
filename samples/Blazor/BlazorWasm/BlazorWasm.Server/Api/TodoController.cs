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
        // 获取此所有者的全部待办项目密钥
        var itemKeys = await _factory
            .GetGrain<ITodoManagerGrain>(ownerKey)
            .GetAllAsync();

        // 扇扇扇出以平行获得分组中的单个项目
        // 同时提出所有个别请求
        var tasks = itemKeys
            .Select(async itemId =>
            {
                var item = await _factory
                    .GetGrain<ITodoGrain>(itemId)
                    .GetAsync();

                // 如果个人 grain 未能解登记,我们就可以取得无效结果
                // 这件事,我们在这里可以完成
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
