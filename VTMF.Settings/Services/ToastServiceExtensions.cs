using BlazorBootstrap;

namespace VTMF.Settings.Services;

public static class ToastServiceExtensions
{
    public static void ShowError(this ToastService toastService, string message, string title = "Error")
    {
        toastService.Notify(new ToastMessage(ToastType.Danger, message)
        {
            Title = title
        });
    }

    public static void ShowSuccess(this ToastService toastService, string message, string title = "Success")
    {
        toastService.Notify(new ToastMessage(ToastType.Success, message)
        {
            Title = title
        });
    }

    public static void ShowWarning(this ToastService toastService, string message, string title = "Warning")
    {
        toastService.Notify(new ToastMessage(ToastType.Warning, message)
        {
            Title = title
        });
    }
}
