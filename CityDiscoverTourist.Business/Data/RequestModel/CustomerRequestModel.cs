using Microsoft.AspNetCore.Identity;

namespace CityDiscoverTourist.Business.Data.RequestModel;

public class CustomerRequestModel
{
    public string? Id { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool Gender { get; set; }
    public string? ImagePath { get; set; }
}