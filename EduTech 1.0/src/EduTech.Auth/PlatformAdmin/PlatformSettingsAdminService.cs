using EduTech.Shared.Constants;
using EduTech.Shared.Context;
using EduTech.Shared.Exceptions;
using EduTech.Shared.Persistence;

namespace EduTech.Auth.PlatformAdmin;

public interface IPlatformSettingsAdminService
{
    Task<decimal> GetPaymentFeeAsync(CancellationToken cancellationToken);
    Task SetPaymentFeeAsync(decimal amount, CancellationToken cancellationToken);
}

internal sealed class PlatformSettingsAdminService : IPlatformSettingsAdminService
{
    private readonly IPlatformSettingsRepository _settings;
    private readonly IEduTechRequestContext _requestContext;

    public PlatformSettingsAdminService(IPlatformSettingsRepository settings, IEduTechRequestContext requestContext)
    {
        _settings = settings;
        _requestContext = requestContext;
    }

    public Task<decimal> GetPaymentFeeAsync(CancellationToken cancellationToken)
        => _settings.GetDecimalAsync(PlatformSettingKeys.PaymentPlatformFee, 0m, cancellationToken);

    public async Task SetPaymentFeeAsync(decimal amount, CancellationToken cancellationToken)
    {
        string? role = _requestContext.Role;
        if (role != PlatformAdminRoles.SuperAdmin && role != PlatformAdminRoles.Finance)
        {
            throw new AppErrorException("Only finance or super admins can change the payment fee.",
                403, ErrorCodes.AccessDenied);
        }

        if (amount < 0)
        {
            throw new AppErrorException("The fee can't be negative.", 400, ErrorCodes.ValidationError);
        }

        await _settings.SetDecimalAsync(PlatformSettingKeys.PaymentPlatformFee, amount, cancellationToken);
    }
}
