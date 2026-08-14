namespace Proxytrace.Api.Auth;

internal interface ISigningKeyProvider
{
    string EnsureSigningKey(string? configured);
}
