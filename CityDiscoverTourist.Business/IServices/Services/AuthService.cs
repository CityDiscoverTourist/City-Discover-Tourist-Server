using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CityDiscoverTourist.Business.Data.RequestModel;
using CityDiscoverTourist.Business.Data.ResponseModel;
using CityDiscoverTourist.Business.Enums;
using CityDiscoverTourist.Business.Exceptions;
using CityDiscoverTourist.Data.Models;
using FirebaseAdmin.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CityDiscoverTourist.Business.IServices.Services;

public class AuthService: IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private static  IConfiguration? _configuration;
    private  readonly RoleManager<IdentityRole> _roleManager;


    public AuthService(UserManager<ApplicationUser> userManager, IConfiguration? configuration, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _configuration = configuration;
        _roleManager = roleManager;
    }

    public async Task<LoginResponseModel> LoginFirebase(LoginFirebaseModel model)
    {
        await CreateRole();
        var userViewModel = await VerifyFirebaseToken(model.TokenId);
        var user = await _userManager.FindByNameAsync(userViewModel.Email);

        if (await CreateUserIfNotExits(user, userViewModel)) return null!;

        if (user is {LockoutEnabled: false }) throw new AppException("User is locked");
        var authClaims = new List<Claim>
        {
            new (ClaimTypes.Name, userViewModel.Email ?? string.Empty),
            new (JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new (ClaimTypes.Email, userViewModel.Email ?? string.Empty),
            new (ClaimTypes.Expiration, DateTime.Now.AddHours(1).ToString(CultureInfo.CurrentCulture)),
        };

        var accessToken = GetJwtToken(authClaims);

        userViewModel.JwtToken = new JwtSecurityTokenHandler().WriteToken(accessToken);
        userViewModel.RefreshToken = GenerateRefreshToken();
        userViewModel.RefreshTokenExpiryTime = DateTime.Now.AddSeconds(7);
        userViewModel.AccountId = user.Id;

        return userViewModel;
    }

    public async Task<LoginResponseModel> LoginForAdmin(LoginRequestModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null) throw new AppException("User not found");
        if (!await _userManager.CheckPasswordAsync(user, model.Password))
            throw new UnauthorizedAccessException("Invalid credentials");
        var authClaims = new List<Claim>
        {
            new (ClaimTypes.Name, user.Email ?? string.Empty),
            new (JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new (ClaimTypes.Email, user.Email ?? string.Empty),
            new (ClaimTypes.Expiration, DateTime.Now.AddHours(1).ToString(CultureInfo.CurrentCulture)),
        };

        var accessToken = GetJwtToken(authClaims);

        var userViewModel = new LoginResponseModel
        {
            JwtToken = new JwtSecurityTokenHandler().WriteToken(accessToken),
            RefreshToken = GenerateRefreshToken(),
            RefreshTokenExpiryTime = DateTime.Now.AddSeconds(7),
            Email = user.Email,
            AccountId = user.Id
        };
        return userViewModel;
    }

    // register new user
    public async Task<LoginResponseModel> Register(LoginRequestModel model)
    {
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is { }) throw new AppException("User already exists");
        user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            EmailConfirmed = true,
            LockoutEnabled = false
        };
        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded) throw new AppException(result.Errors.First().Description);
        await _userManager.AddToRoleAsync(user, Role.Admin.ToString());
        var authClaims = new List<Claim>
        {
            new (ClaimTypes.Name, user.Email ?? string.Empty),
            new (JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new (ClaimTypes.Email, user.Email ?? string.Empty),
            new (ClaimTypes.Expiration, DateTime.Now.AddHours(1).ToString(CultureInfo.CurrentCulture)),
        };

        var accessToken = GetJwtToken(authClaims);

        var userViewModel = new LoginResponseModel
        {
            JwtToken = new JwtSecurityTokenHandler().WriteToken(accessToken),
            RefreshToken = GenerateRefreshToken(),
            RefreshTokenExpiryTime = DateTime.Now.AddSeconds(7),
            Email = user.Email
        };
        return userViewModel;
    }

    private async Task<bool> CreateUserIfNotExits(ApplicationUser user, LoginResponseModel userViewModel)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (user != null) return false;
        user = new ApplicationUser()
        {
            UserName = userViewModel.Email,
            Email = userViewModel.Email,
            SecurityStamp = Guid.NewGuid().ToString(),
            EmailConfirmed = true,
            NormalizedEmail = userViewModel.Email?.ToUpper(),
            NormalizedUserName = userViewModel.FullName?.ToUpper(),
            PhoneNumberConfirmed = false,
        };
        var loginInfo = new ExternalLoginInfo(new ClaimsPrincipal(), "Firebase-Email", userViewModel.IdProvider, userViewModel.Email);
        var result = await _userManager.CreateAsync(user);
        await _userManager.AddToRoleAsync(user, Role.User.ToString());
        await _userManager.AddLoginAsync(user, loginInfo);
        return !result.Succeeded;
    }

    public JwtSecurityToken GetJwtToken(IEnumerable<Claim> claims)
    {
        var secretKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration!["JWT:Secret"]));
        var signingCredentials = new SigningCredentials(secretKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            _configuration["JWT:ValidIssuer"],
            _configuration["JWT:ValidAudience"],
            claims,
            expires: DateTime.Now.AddHours(1),
            signingCredentials: signingCredentials
        );

        return token;
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private static async Task<LoginResponseModel> VerifyFirebaseToken(string? token)
    {
        var decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(token);

        var uid = decodedToken.Uid;
        var user = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
        // Query account table in DB

        var loginViewModel = new LoginResponseModel
        {
            IdProvider = uid,
            Email = user.Email,
            FullName = user.DisplayName,
            ImagePath = user.PhotoUrl
        };
        return loginViewModel;
    }

    public async Task CreateRole()
    {
        if (!_roleManager.RoleExistsAsync(Role.Admin.ToString()).GetAwaiter().GetResult())
        {
            await _roleManager.CreateAsync(new IdentityRole(Role.Admin.ToString()));
            await _roleManager.CreateAsync(new IdentityRole(Role.User.ToString()));
            await _roleManager.CreateAsync(new IdentityRole(Role.QuestOwner.ToString()));
        }
    }
}