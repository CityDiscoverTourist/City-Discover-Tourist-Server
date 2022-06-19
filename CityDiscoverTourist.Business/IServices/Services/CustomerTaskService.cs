using AutoMapper;
using CityDiscoverTourist.Business.Data.RequestModel;
using CityDiscoverTourist.Business.Data.ResponseModel;
using CityDiscoverTourist.Business.Enums;
using CityDiscoverTourist.Business.Exceptions;
using CityDiscoverTourist.Business.Helper;
using CityDiscoverTourist.Business.Helper.Params;
using CityDiscoverTourist.Business.HubConfig;
using CityDiscoverTourist.Business.Settings;
using CityDiscoverTourist.Data.IRepositories;
using CityDiscoverTourist.Data.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace CityDiscoverTourist.Business.IServices.Services;

public class CustomerTaskService : BaseService, ICustomerTaskService
{
    private const int PointWhenHitSuggestion = 150;
    private const int PointWhenWrongAnswer = 100;
    private const float DistanceThreshold = 500;
    private static  GoongApiSetting? _googleApiSettings;
    private readonly ICustomerAnswerService _customerAnswerService;
    private readonly ICustomerQuestRepository _customerQuestRepo;
    private readonly ICustomerTaskRepository _customerTaskRepo;
    private readonly ILocationRepository _locationRepo;
    private readonly IMapper _mapper;
    private readonly IQuestItemRepository _questItemRepo;
    private readonly ISortHelper<CustomerTask> _sortHelper;
    private readonly ISuggestionRepository _suggestionRepo;
    private readonly IHubContext<CustomerTaskHub> _hubContext;


    public CustomerTaskService(ICustomerTaskRepository customerTaskRepository, IMapper mapper,
        ISortHelper<CustomerTask> sortHelper, ICustomerQuestRepository customerQuestRepo,
        IQuestItemRepository questItemRepo, GoongApiSetting? googleApiSettings,
        ICustomerAnswerService customerAnswerService, ILocationRepository locationRepo,
        ISuggestionRepository suggestionRepo, IHubContext<CustomerTaskHub> hubContext)
    {
        _customerTaskRepo = customerTaskRepository;
        _mapper = mapper;
        _sortHelper = sortHelper;
        _customerQuestRepo = customerQuestRepo;
        _questItemRepo = questItemRepo;
        _googleApiSettings = googleApiSettings;
        _customerAnswerService = customerAnswerService;
        _locationRepo = locationRepo;
        _suggestionRepo = suggestionRepo;
        _hubContext = hubContext;
    }

    public async Task<PageList<CustomerTaskResponseModel>> GetAll(CustomerTaskParams @params)
    {
        var listAll = _customerTaskRepo.GetAll();

        Search(ref listAll, @params);

        var sortedQuests = _sortHelper.ApplySort(listAll, @params.OrderBy);
        var mappedData = _mapper.Map<IEnumerable<CustomerTaskResponseModel>>(sortedQuests);
       // await _hubContext.Clients.All.SendAsync("demo", mappedData);
        return PageList<CustomerTaskResponseModel>.ToPageList(mappedData, @params.PageNumber, @params.PageSize);
    }

    public async Task<CustomerTaskResponseModel> Get(int id)
    {
        var entity = await _customerTaskRepo.Get(id);
        CheckDataNotNull("CustomerTask", entity);
        return _mapper.Map<CustomerTaskResponseModel>(entity);
    }

    public async Task<CustomerTaskResponseModel> CustomerStartQuest(CustomerTaskRequestModel request, int questId)
    {
        //insert customer task for first time customer join the quest
        var entity = _mapper.Map<CustomerTask>(request);

        entity.CurrentPoint = Convert.ToSingle(GetBeginPointsAsync(entity.CustomerQuestId));
        entity.IsFinished = false;
        entity.QuestItemId = GetFirstQuestItemIdOfQuest(questId);

        entity = await _customerTaskRepo.Add(entity);
        await _hubContext.Clients.All.SendAsync("AddCustomerTask", _mapper.Map<CustomerTaskResponseModel>(entity));
        return _mapper.Map<CustomerTaskResponseModel>(entity);
    }

    public async Task<int> MoveCustomerToNextTask(int questId, int customerQuestId)
    {
        var nextQuestItemId = 0;
        // get last quest item customer has done
        var lastQuestItemCustomerFinished = _customerTaskRepo.GetAll()
            .Where(x => x.CustomerQuestId == customerQuestId && x.IsFinished == true).AsEnumerable().LastOrDefault();

        var questItems = _questItemRepo.GetByCondition(x => x.QuestId == questId).ToList();

        // get next quest item
        for (var i = 0; i < questItems.Count; i++)
        {
            if (lastQuestItemCustomerFinished!.QuestItemId != questItems[i].Id) continue;
            //move to next quest item
            var nextQuestItem = questItems.FirstOrDefault(x => x.ItemId == questItems[i].Id);

            if (nextQuestItem == null) throw new AppException("This quest is finished");

            // recursive when ever to get next quest item with status is not deleted
            nextQuestItem = NextQuestItem(nextQuestItem, questItems, i);

            nextQuestItemId = nextQuestItem.Id;
            var customerTask = new CustomerTaskResponseModel
            {
                CustomerQuestId = customerQuestId,
                QuestItemId = nextQuestItem.Id,
                IsFinished = false,
                CurrentPoint = lastQuestItemCustomerFinished.CurrentPoint,
                Status = "Progress"
            };
            await _customerTaskRepo.Add(_mapper.Map<CustomerTask>(customerTask));
        }

        return nextQuestItemId == 0 ? 0 : nextQuestItemId;
    }

    public async Task<CustomerTaskResponseModel> DeleteAsync(int id)
    {
        var entity = await _customerTaskRepo.Delete(id);
        return _mapper.Map<CustomerTaskResponseModel>(entity);
    }

    public string GetBeginPointsAsync(int customerQuestId)
    {
        return _customerQuestRepo.Get(customerQuestId).Result.BeginPoint!;
    }

    public async Task<CustomerTaskResponseModel> DecreasePointWhenHitSuggestion(int customerQuestId)
    {
        var currentPoint = _customerTaskRepo
            .GetByCondition(x => x.CustomerQuestId == customerQuestId).OrderByDescending(x => x.CurrentPoint)
            .LastOrDefaultAsync().Result!.CurrentPoint;

        var customerTask = await _customerTaskRepo.GetByCondition(x => x.CustomerQuestId == customerQuestId)
            .OrderByDescending(x => x.CurrentPoint).LastOrDefaultAsync();

        if (customerTask!.CountSuggestion >= 3) throw new AppException("You have already hit 3 suggestions");

        customerTask.CurrentPoint = currentPoint - PointWhenHitSuggestion;
        customerTask.CountSuggestion++;

        customerTask = await _customerTaskRepo.UpdateFields(customerTask, r => r.CurrentPoint, r => r.CountSuggestion);

        return _mapper.Map<CustomerTaskResponseModel>(customerTask);
    }

    public async Task<CustomerTaskResponseModel> CheckCustomerAnswer(int customerQuestId, string customerReply,
        int questItemId)
    {
        var isCustomerReplyCorrect = true;

        var currentPoint = _customerTaskRepo
            .GetByCondition(x => x.CustomerQuestId == customerQuestId).OrderByDescending(x => x.CurrentPoint)
            .LastOrDefaultAsync().Result!.CurrentPoint;

        //get current quest item of customer
        var customerTask = await _customerTaskRepo.GetByCondition(x => x.CustomerQuestId == customerQuestId)
            .Where(x => x.QuestItemId == questItemId).OrderByDescending(x => x.CurrentPoint).LastOrDefaultAsync();

        //compare with correct answer
        var correctAnswer = await _questItemRepo.Get(customerTask!.QuestItemId);
        if (!correctAnswer.RightAnswer!.ToLower().Equals(customerReply.ToLower()))
        {
            // count number of customer answer wrong
            // throw when it >= 3
            // show the suggestion
            // not decrease point
            // code here:

            // if count number of customer answer wrong >= 5
            // show right answer move to next quest item
            // code here:
            if (customerTask.CountWrongAnswer >= 5)
                throw new AppException("You have already hit 5 wrong answers, We will show the right answer");

            customerTask.CurrentPoint = currentPoint - PointWhenWrongAnswer;
            customerTask.CountWrongAnswer++;
            customerTask = await _customerTaskRepo.UpdateFields(customerTask, r => r.CurrentPoint,
                r => r.CountWrongAnswer);
            isCustomerReplyCorrect = false;
        }
        else
        {
            // correct answer
            customerTask.Status = "Finished";
            customerTask.IsFinished = true;
            await _customerTaskRepo.UpdateFields(customerTask, r => r.Status!, r => r.IsFinished);
        }

        //save customer answer for each time customer answer wrong/true
        if (!isCustomerReplyCorrect)
            await SaveCustomerAnswer(customerTask, customerReply, NoteCustomerAnswer.WrongAnswer);
        else
            await SaveCustomerAnswer(customerTask, customerReply, NoteCustomerAnswer.CorrectAnswer);

        return _mapper.Map<CustomerTaskResponseModel>(customerTask);
    }

    public Task<bool> IsLastQuestItem(int customerQuestId)
    {
        var numOfItemCustomerHasDone = _customerTaskRepo
            .GetByCondition(x => x.CustomerQuestId == customerQuestId).Count();
        var numOfItemInQuest = _questItemRepo
            .GetByCondition(x => x.QuestId == _customerQuestRepo.Get(customerQuestId).Result.QuestId).Count();
        return Task.FromResult(numOfItemCustomerHasDone == numOfItemInQuest);
    }

    public float GetLastPoint(int customerQuestId)
    {
        return _customerTaskRepo.GetByCondition(x => x.CustomerQuestId == customerQuestId)
            .OrderByDescending(x => x.CurrentPoint).LastOrDefaultAsync().Result!.CurrentPoint;
    }

    public IEnumerable<string> GetLongLatFromCurrentQuestItemOfCustomer(int customerQuestId)
    {
        //get long lat of current quest item customer prepare to do
        var itemId = _customerTaskRepo.GetByCondition(x => x.CustomerQuestId == customerQuestId)
            .OrderByDescending(x => x.CurrentPoint).LastOrDefaultAsync().Result!.QuestItemId;

        var locationOfQuestItem = _questItemRepo.Get(itemId).Result.LocationId;
        var latLong = _locationRepo.Get(locationOfQuestItem).Result.Latitude + "," +
                      _locationRepo.Get(locationOfQuestItem).Result.Longitude;
        return new List<string> { latLong };
    }

    public bool IsCustomerAtQuestItemLocation(int customerQuestId, float latitude, float longitude)
    {
        // get from db
        var longLatOfQuestItem = GetLongLatFromCurrentQuestItemOfCustomer(customerQuestId);

        var distance = CalculateDistance(longLatOfQuestItem.First(), latitude + "," + longitude);
        return distance < DistanceThreshold;
    }

    public Task<string> ShowSuggestions(int questItemId)
    {
        var suggestions = _suggestionRepo.GetByCondition(x => x.QuestItemId == questItemId).Select(x => x.Content)
            .ToList();
        return Task.FromResult(string.Join(",", suggestions));
    }

    public bool CheckCustomerLocationWithQuestLocation(int questId, float latitude, float longitude)
    {
        // check if customer is at quest location
        var latLong = GetStartingAddress(questId);
        var distance = CalculateDistance(latLong, latitude + "," + longitude);
        return distance < DistanceThreshold;
    }

    private static void Search(ref IQueryable<CustomerTask> entities, CustomerTaskParams param)
    {
        if (!entities.Any()) return;

        if (param.Status != null)
            entities = entities.Where(x => x.Status == param.Status);
    }

    #region MyRegion

    private static QuestItem NextQuestItem(QuestItem? nextQuestItem, List<QuestItem> questItems, int i)
    {
        if (nextQuestItem!.Status != "Deleted") return nextQuestItem;

        if(i >= questItems.Count - 1) throw new AppException("This quest is finished");

        nextQuestItem = questItems[i + 1];
        return NextQuestItem(nextQuestItem, questItems, i + 1);
    }

    private static float CalculateDistance(string latLongFromLocation, string latLongFromUserDevice)
    {
        var baseUrl = $"https://rsapi.goong.io/DistanceMatrix?origins={latLongFromLocation}" +
                      $"&destinations={latLongFromUserDevice}&vehicle=car&api_key={_googleApiSettings!.ApiKey}";
        var client = new HttpClient();
        var response = client.GetAsync(baseUrl).Result;
        var content = response.Content.ReadAsStringAsync().Result;
        var json = JObject.Parse(content);
        var distance = json["rows"]![0]!["elements"]![0]!["distance"]!["value"];
        return distance!.Value<float>();
    }

    private int GetFirstQuestItemIdOfQuest(int questId)
    {
        var questItems = _questItemRepo.GetByCondition(x => x.QuestId == questId);
        return (from questItem in questItems.ToList() where questItem.ItemId == null select questItem.Id)
            .FirstOrDefault();
    }

    private string GetStartingAddress(int questId)
    {
        //get starting address of quest
        var questItems = _questItemRepo.GetByCondition(x => x.QuestId == questId);
        var locationId = questItems.FirstOrDefault(x => x.ItemId == null)!.LocationId;
        var location = _locationRepo.Get(locationId).Result;
        return  location.Latitude + "," + location.Longitude;
    }

    private async Task SaveCustomerAnswer(CustomerTask customerTask, string customerReply, NoteCustomerAnswer note)
    {
        var customerAnswer = new CustomerAnswerRequestModel
        {
            CustomerTaskId = customerTask.Id,
            QuestItemId = customerTask.QuestItemId,
            Note = note.ToString(),
            CustomerReply = customerReply
        };
        await _customerAnswerService.CreateAsync(customerAnswer);
    }

    #endregion
}