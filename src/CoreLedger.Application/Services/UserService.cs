using CoreLedger.Application.DTOs;
using CoreLedger.Domain.Entities;
using CoreLedger.Domain.Exceptions;
using CoreLedger.Domain.Repositories;

namespace CoreLedger.Application.Services;

public class UserService(IUserRepository userRepository, IUnitOfWork unitOfWork)
{
    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        if (await userRepository.ExistsByEmailAsync(request.Email, ct))
            throw new DomainException($"User with email '{request.Email}' already exists.");

        var user = new User(request.Email, request.FullName);
        await userRepository.AddAsync(user, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new UserResponse(user.Id, user.Email, user.FullName, user.CreatedAt);
    }
}
