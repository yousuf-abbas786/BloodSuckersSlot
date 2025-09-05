using Microsoft.AspNetCore.Mvc;
using BloodSuckersSlot.Api.Models;
using BloodSuckersSlot.Api.Services;
using BloodSuckersSlot.Shared.Models;
using Microsoft.AspNetCore.SignalR;
using ApiGamingEntity = BloodSuckersSlot.Api.Models.GamingEntity;

namespace BloodSuckersSlot.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GamingEntitiesController : ControllerBase
    {
        private readonly IGamingEntityService _gamingEntityService;
        private readonly ILogger<GamingEntitiesController> _logger;
        private readonly IHubContext<GamingEntityHub> _hubContext;

        public GamingEntitiesController(
            IGamingEntityService gamingEntityService, 
            ILogger<GamingEntitiesController> logger,
            IHubContext<GamingEntityHub> hubContext)
        {
            _gamingEntityService = gamingEntityService;
            _logger = logger;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Get all gaming entities with optional filtering
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<PaginatedResult<GamingEntityListItem>>> GetEntities(
            [FromQuery] EntityRole? role = null,
            [FromQuery] string? superAgentId = null,
            [FromQuery] string? agentId = null,
            [FromQuery] string? tokenId = null,
            [FromQuery] bool? active = null,
            [FromQuery] string? searchTerm = null,
            [FromQuery] string? currency = null,
            [FromQuery] int? minRtp = null,
            [FromQuery] int? maxRtp = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                var filter = new GamingEntityFilter
                {
                    Role = role,
                    SuperAgentId = superAgentId,
                    AgentId = agentId,
                    TokenId = tokenId,
                    Active = active,
                    SearchTerm = searchTerm,
                    Currency = currency,
                    MinRtp = minRtp,
                    MaxRtp = maxRtp,
                    PageNumber = pageNumber,
                    PageSize = Math.Min(pageSize, 100) // Limit page size
                };

                var result = await _gamingEntityService.GetEntitiesAsync(filter);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get all entities optimized for hierarchical display
        /// </summary>
        [HttpGet("hierarchical")]
        [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "*" })]
        public async Task<ActionResult<List<GamingEntityListItem>>> GetHierarchicalEntities()
        {
            try
            {
                var entities = await _gamingEntityService.GetHierarchicalEntitiesAsync();
                return Ok(entities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting hierarchical entities");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get all entities optimized for hierarchical display (lightweight version)
        /// </summary>
        [HttpGet("hierarchical-light")]
        [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "*" })]
        public async Task<ActionResult<List<GamingEntityListItem>>> GetHierarchicalEntitiesLight()
        {
            try
            {
                var entities = await _gamingEntityService.GetHierarchicalEntitiesLightAsync();
                return Ok(entities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting hierarchical entities light");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get a specific gaming entity by ID
        /// </summary>
        [HttpGet("{id}")]
        [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "id" })]
        public async Task<ActionResult<GamingEntityDetail>> GetEntity(string id)
        {
            try
            {
                var entity = await _gamingEntityService.GetEntityByIdAsync(id);
                if (entity == null)
                {
                    return NotFound();
                }

                return Ok(entity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity: {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get entity with complete hierarchy (parents and children)
        /// </summary>
        [HttpGet("{id}/hierarchy")]
        public async Task<ActionResult<GamingEntityHierarchy>> GetEntityHierarchy(string id)
        {
            try
            {
                var hierarchy = await _gamingEntityService.GetEntityHierarchyAsync(id);
                if (hierarchy == null)
                {
                    return NotFound();
                }

                return Ok(hierarchy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entity hierarchy: {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get children of a specific entity
        /// </summary>
        [HttpGet("{id}/children")]
        public async Task<ActionResult<List<GamingEntityListItem>>> GetChildren(string id)
        {
            try
            {
                var entity = await _gamingEntityService.GetEntityByIdAsync(id);
                if (entity == null)
                {
                    return NotFound();
                }

                var children = await _gamingEntityService.GetChildrenAsync(id, entity.Role);
                return Ok(children);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting children for entity: {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Create a new gaming entity
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<GamingEntityDetail>> CreateEntity([FromBody] BloodSuckersSlot.Shared.Models.GamingEntity entity)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var createdEntity = await _gamingEntityService.CreateEntityAsync(entity);
                if (createdEntity == null)
                {
                    return BadRequest("Failed to create entity");
                }

                // Notify clients via SignalR
                await _hubContext.NotifyEntityCreated(createdEntity);

                return CreatedAtAction(nameof(GetEntity), new { id = createdEntity.Id }, createdEntity);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating entity");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Update an existing gaming entity
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<GamingEntityDetail>> UpdateEntity(string id, [FromBody] BloodSuckersSlot.Shared.Models.GamingEntity entity)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var updatedEntity = await _gamingEntityService.UpdateEntityAsync(id, entity);
                if (updatedEntity == null)
                {
                    return NotFound();
                }

                // Notify clients via SignalR
                await _hubContext.NotifyEntityUpdated(updatedEntity);

                return Ok(updatedEntity);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating entity: {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Delete a gaming entity
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteEntity(string id)
        {
            try
            {
                // Get entity details before deletion for SignalR notification
                var entity = await _gamingEntityService.GetEntityByIdAsync(id);
                
                var deleted = await _gamingEntityService.DeleteEntityAsync(id);
                if (!deleted)
                {
                    return NotFound();
                }

                // Notify clients via SignalR
                if (entity != null)
                {
                    await _hubContext.NotifyEntityDeleted(id, entity.Role);
                }

                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting entity: {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Toggle active status of an entity
        /// </summary>
        [HttpPatch("{id}/toggle-active")]
        public async Task<ActionResult> ToggleActive(string id)
        {
            try
            {
                // Get entity details before toggle for SignalR notification
                var entity = await _gamingEntityService.GetEntityByIdAsync(id);
                
                var toggled = await _gamingEntityService.ToggleActiveAsync(id);
                if (!toggled)
                {
                    return NotFound();
                }

                // Notify clients via SignalR
                if (entity != null)
                {
                    await _hubContext.NotifyEntityStatusChanged(id, entity.Role, !entity.Active);
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling active status for entity: {Id}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get available currencies
        /// </summary>
        [HttpGet("currencies")]
        public async Task<ActionResult<List<string>>> GetCurrencies()
        {
            try
            {
                var currencies = await _gamingEntityService.GetAvailableCurrenciesAsync();
                return Ok(currencies);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting currencies");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get entity statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<ActionResult<Dictionary<string, int>>> GetStats()
        {
            try
            {
                var stats = await _gamingEntityService.GetEntityStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stats");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get entities by role
        /// </summary>
        [HttpGet("by-role/{role}")]
        public async Task<ActionResult<List<GamingEntityListItem>>> GetEntitiesByRole(EntityRole role)
        {
            try
            {
                var filter = new GamingEntityFilter
                {
                    Role = role,
                    PageSize = 1000 // Get all entities of this role
                };

                var result = await _gamingEntityService.GetEntitiesAsync(filter);
                return Ok(result.Items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting entities by role: {Role}", role);
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
