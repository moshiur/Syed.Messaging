namespace Syed.BuildingBlocks.RpcContracts;

/// <summary>
/// RPC request to validate if a user exists.
/// </summary>
public record ValidateUserRequest(Guid UserId);

/// <summary>
/// RPC response indicating user existence and name.
/// </summary>
public record ValidateUserResponse(bool Exists, string? FullName);

/// <summary>
/// RPC request to validate if a post exists.
/// </summary>
public record ValidatePostRequest(Guid PostId);

/// <summary>
/// RPC response indicating post existence and title.
/// </summary>
public record ValidatePostResponse(bool Exists, string? Title);
