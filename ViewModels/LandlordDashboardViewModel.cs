namespace RentRight.ViewModels;

public class LandlordDashboardViewModel
{
    public IReadOnlyList<LandlordDashboardPropertyViewModel> Properties { get; init; } = [];
}

public class LandlordDashboardPropertyViewModel
{
    public int Id { get; init; }
    public required string Title { get; init; }
    public required string Address { get; init; }
    public decimal RentAmount { get; init; }
}
