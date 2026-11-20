namespace DicomAgent.Application.Abstractions;

public interface IRequestSecretStore
{
    string Protect(string value);
    string Unprotect(string value);
}
