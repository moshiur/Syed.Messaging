public record Job
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string Department { get; set; }
    public decimal Salary { get; set; }    
}