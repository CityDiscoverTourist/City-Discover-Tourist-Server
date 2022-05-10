using AutoMapper;
using CityDiscoverTourist.Business.Data.RequestModel;
using CityDiscoverTourist.Business.Data.ResponseModel;
using CityDiscoverTourist.Business.Helper;
using CityDiscoverTourist.Business.Helper.Params;
using CityDiscoverTourist.Data.IRepositories;
using CityDiscoverTourist.Data.Models;

namespace CityDiscoverTourist.Business.IServices.Services;

public class AreaService: BaseService, IAreaService
{
    private readonly IAreaRepository _areaRepository;
    private readonly IMapper _mapper;
    private readonly ISortHelper<Area> _sortHelper;

    public AreaService(IAreaRepository areaRepository, IMapper mapper, ISortHelper<Area> sortHelper)
    {
        _areaRepository = areaRepository;
        _mapper = mapper;
        _sortHelper = sortHelper;
    }

    public PageList<AreaResponseModel> GetAll(AreaParams @params)
    {
        var listAll = _areaRepository.GetAll();

        Search(ref listAll, @params);

        var sortedQuests = _sortHelper.ApplySort(listAll, @params.OrderBy);
        var mappedData = _mapper.Map<IEnumerable<AreaResponseModel>>(sortedQuests);
        return PageList<AreaResponseModel>.ToPageList(mappedData, @params.PageNume, @params.PageSize);
    }

    public async Task<AreaResponseModel> Get(int id)
    {
        var entity = await _areaRepository.Get(id);
        CheckDataNotNull("Area", entity);
        return _mapper.Map<AreaResponseModel>(entity);
    }

    public async Task<AreaResponseModel> CreateAsync(AreaRequestModel request)
    {
        var entity = _mapper.Map<Area>(request);
        entity = await _areaRepository.Add(entity);
        return _mapper.Map<AreaResponseModel>(entity);
    }

    public async Task<AreaResponseModel> UpdateAsync(AreaRequestModel request)
    {
        var entity = _mapper.Map<Area>(request);
        entity = await _areaRepository.Update(entity);
        return _mapper.Map<AreaResponseModel>(entity);
    }

    public async Task<AreaResponseModel> DeleteAsync(int id)
    {
        var entity = await _areaRepository.Delete(id);
        return _mapper.Map<AreaResponseModel>(entity);
    }

    private static void Search(ref IQueryable<Area> entities, AreaParams param)
    {
        if (!entities.Any()) return;

        if (param.CityId != 0)
        {
            entities = entities.Where(x => x.CityId == param.CityId);
        }
    }
}