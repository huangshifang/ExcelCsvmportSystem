namespace ExcelImportSystem.Core.Interfaces;

public interface ICaptchaService
{
    (string Token, string Base64Image) Generate();
    bool Validate(string token, string code);
}
