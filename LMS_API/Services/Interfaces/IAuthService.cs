using LMS_API.Common.Results;
using LMS_API.DTOs;

namespace LMS_API.Services.Interfaces
{
    public interface IAuthService
    {

        Task<ServiceResult<UserDto>> RegisterAsync(RegisterDto dto);

        Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginDto dto, string ip, string device);

        Task<ServiceResult<AuthResponseDto>> RefreshTokenAsync(string refreshToken);
        Task<ServiceResult<bool>> LogoutAsync(string refreshToken);
        Task<ServiceResult<bool>> VerifyEmailAsync(string token);
        Task<ServiceResult<bool>> ForgotPasswordAsync(string email);
        Task<ServiceResult<bool>> ResetPasswordAsync(string token, string newPassword);
    }
}