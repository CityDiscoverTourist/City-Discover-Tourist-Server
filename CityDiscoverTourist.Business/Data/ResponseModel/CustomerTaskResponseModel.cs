namespace CityDiscoverTourist.Business.Data.ResponseModel;

public class CustomerTaskResponseModel
{
    public int Id { get; set; }
    public float CurrentPoint { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedDate { get; set; }

    public int QuestItemId { get; set; }

    public int CustomerQuestId { get; set; }
}