namespace VTMF.Settings.Entities;

public class FormValidationResult
{
    public bool IsValid { get; set; }
    public Dictionary<string, string> Errors { get; set; } = new();

    public FormValidationResult()
    {
        IsValid = true;
    }

    public void AddError(string fieldName, string errorMessage)
    {
        IsValid = false;
        Errors[fieldName] = errorMessage;
    }

    public bool HasError(string fieldName) => Errors.ContainsKey(fieldName);

    public string? GetError(string fieldName) => Errors.TryGetValue(fieldName, out var error) ? error : null;

    public void ClearErrors()
    {
        IsValid = true;
        Errors.Clear();
    }
}
