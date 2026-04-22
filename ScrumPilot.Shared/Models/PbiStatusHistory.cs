namespace ScrumPilot.Shared.Models;

public class PbiStatusHistory
{
    public int Id { get; set; }
    public int PbiId { get; set; }
    public PbiStatus FromStatus { get; set; }
    public PbiStatus ToStatus { get; set; }
    public DateTime ChangedAt { get; set; }
}
