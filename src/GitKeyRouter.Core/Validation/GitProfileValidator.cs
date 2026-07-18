using System.Net.Mail;
using GitKeyRouter.Core.Models;

namespace GitKeyRouter.Core.Validation;

public static class GitProfileValidator
{
    public static ValidationResult Validate(GitProfile profile, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(config);
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            result.Add("Profile display name is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.UserName))
        {
            result.Add("Git user.name is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.UserEmail))
        {
            result.Add("Git user.email is required.");
        }
        else
        {
            try
            {
                _ = new MailAddress(profile.UserEmail.Trim());
            }
            catch (FormatException)
            {
                result.Add("Git user.email is invalid.");
            }
        }

        if (profile.EnableCommitSigning && string.IsNullOrWhiteSpace(profile.SigningKey))
        {
            result.Add("A signing key is required when commit signing is enabled.");
        }

        if (config.GitProfiles.Any(item => !string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.DisplayName, profile.DisplayName, StringComparison.CurrentCultureIgnoreCase)))
        {
            result.Add($"Git Profile '{profile.DisplayName}' already exists.");
        }

        var service = string.IsNullOrWhiteSpace(profile.DefaultServiceInstanceId)
            ? null
            : config.FindService(profile.DefaultServiceInstanceId);
        if (!string.IsNullOrWhiteSpace(profile.DefaultServiceInstanceId) && service is null)
        {
            result.Add("The selected default Git service does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(profile.DefaultIdentityId))
        {
            var identity = config.Identities.FirstOrDefault(item =>
                string.Equals(item.Id, profile.DefaultIdentityId, StringComparison.OrdinalIgnoreCase));
            if (identity is null)
            {
                result.Add("The selected default SSH identity does not exist.");
            }
            else if (service is null
                     || !string.Equals(identity.ServiceInstanceId, service.Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Add("The default SSH identity must belong to the selected default Git service.");
            }
        }

        return result;
    }
}
