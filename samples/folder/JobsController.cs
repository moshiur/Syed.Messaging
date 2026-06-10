using Microsoft.AspNetCore.Mvc;


[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private static readonly List<Job> JobList = new List<Job>()
    {
        new Job { Id = 1, Title = "Software Engineer", Department = "Engineering", Salary = 80000 },
        new Job { Id = 2, Title = "Data Analyst", Department = "Analytics", Salary = 60000 },
        new Job { Id = 3, Title = "Project Manager", Department = "Management", Salary = 90000 }
    };

    [HttpGet]
    public IActionResult GetJob([FromQuery] string? department)
    {
        var Job = department != null
            ? JobList.Where(j => j.Department == department)
            : JobList;
        return Ok(Job);
    }

    [HttpGet("{id}")]
    public IActionResult GetJobById(int id)
    {
        var job = JobList.FirstOrDefault(j => j.Id == id);
        if (job == null)
        {
            return NotFound();
        }
        return Ok(job);
    }

    [HttpPost]
    public IActionResult CreateJob(Job newJob)
    {
        newJob.Id = JobList.Max(j => j.Id) + 1;
        JobList.Add(newJob);
        return CreatedAtAction(nameof(GetJobById), new { id = newJob.Id }, newJob);
    }

    [HttpPut("{id}")]
    public IActionResult UpdateJob(int id, Job updatedJob)
    {
        var job = JobList.FirstOrDefault(j => j.Id == id);
        if (job == null)
        {
            return NotFound();
        }
        job.Title = updatedJob.Title;
        job.Department = updatedJob.Department;
        job.Salary = updatedJob.Salary;
        return NoContent();
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteJob(int id)
    {
        var job = JobList.FirstOrDefault(j => j.Id == id);
        if (job == null)
        {
            return NotFound();
        }
        JobList.Remove(job);
        return NoContent();
    }
}