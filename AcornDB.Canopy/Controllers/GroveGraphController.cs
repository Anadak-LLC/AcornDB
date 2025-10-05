
using Microsoft.AspNetCore.Mvc;
using AcornDB.Models;

namespace AcornDB.Controllers
{
    [ApiController]
    [Route("grove")]
    public class GroveGraphController : ControllerBase
    {
        private readonly Grove _grove;

        public GroveGraphController(Grove grove)
        {
            _grove = grove;
        }

        [HttpGet("describe")]
        public ActionResult<GroveGraphDto> Describe()
        {
            var result = new GroveGraphDto();

            foreach (var tree in _grove.GetAllTrees())
            {
                result.Trees.Add(new TreeNodeDto
                {
                    Id = tree.TreeId,
                    Type = tree.TypeName,
                    NutCount = tree.Count,
                    IsRemote = tree.IsRemote
                });
            }

            foreach (var tangle in _grove.GetTangleStats())
            {
                result.Tangles.Add(new TangleEdgeDto
                {
                    FromTreeId = tangle.LocalTreeId,
                    ToTreeId = tangle.RemoteTreeId,
                    Url = tangle.RemoteUrl
                });
            }

            return Ok(result);
        }
    }
}
