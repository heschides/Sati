using System.Net;
using System.Net.Http;
using System.Security;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data.Cloud;

public sealed class CloudAuthService(CloudApiClient api) : IAuthService
{
    public async Task<User?> AuthenticateAsync(string username, SecureString password)
    {
        var plainText = new NetworkCredential(string.Empty, password).Password;
        try
        {
            var response = await api.PostAnonymousAsync<LoginRequest, LoginResponse>(
                "/api/v1/auth/login",
                new LoginRequest(username, plainText));
            api.SetAccessToken(response.AccessToken, response.ExpiresAtUtc);
            return CloudContractMapper.ToUser(response.User);
        }
        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }
        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new AuthenticationServiceException(
                AuthenticationServiceIssue.TooManyAttempts,
                ex.Message,
                ex.RetryAfter,
                ex);
        }
        catch (CloudApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable &&
                                           ex.Code == "demo_reset_in_progress")
        {
            // DemoMutationLeaseMiddleware turns sign-ins away while a full reset holds its lock.
            // That takes minutes, so it must not read as the few-second cold start below.
            throw new AuthenticationServiceException(
                AuthenticationServiceIssue.ServiceUnavailable,
                "The Demo is being reset, which takes about five minutes. Sign in again when it finishes.",
                innerException: ex);
        }
        catch (CloudApiException ex) when ((int)ex.StatusCode >= 500)
        {
            throw new AuthenticationServiceException(
                AuthenticationServiceIssue.ServiceUnavailable,
                "The Demo service is waking up or temporarily unavailable. Wait a moment and try again.",
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AuthenticationServiceException(
                AuthenticationServiceIssue.NetworkUnavailable,
                "Sati could not reach the Demo service. Check your internet connection and try again.",
                innerException: ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new AuthenticationServiceException(
                AuthenticationServiceIssue.ServiceUnavailable,
                "The Demo service took too long to respond. It may still be waking up; wait a moment and try again.",
                innerException: ex);
        }
        finally
        {
            plainText = string.Empty;
        }
    }
}
