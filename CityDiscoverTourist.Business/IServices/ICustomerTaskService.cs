using CityDiscoverTourist.Business.Data.RequestModel;
using CityDiscoverTourist.Business.Data.ResponseModel;
using CityDiscoverTourist.Business.Helper;
using CityDiscoverTourist.Business.Helper.Params;
using CityDiscoverTourist.Data.Models;

namespace CityDiscoverTourist.Business.IServices;

public interface ICustomerTaskService
{
    public PageList<CustomerTaskResponseModel> GetAll(CustomerTaskParams @params);
    public Task<CustomerTaskResponseModel> Get(int id);
    public Task<CustomerTaskResponseModel> CreateAsync(CustomerTaskRequestModel request);
    public Task<CustomerTaskResponseModel> UpdateAsync(CustomerTaskRequestModel request);
    public Task<CustomerTaskResponseModel> DeleteAsync(int id);
}