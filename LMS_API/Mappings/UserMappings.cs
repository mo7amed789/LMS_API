using LMS_API.Domain.Entities;
using LMS_API.DTOs;

namespace LMS_API.Mappings
{
    public static class UserMappings
    {
        public static UserDto ToDto(this User user)
        {
            return new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role
            };
        }
    }
}
