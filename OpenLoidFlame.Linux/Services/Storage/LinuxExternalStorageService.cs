using OpenLoidFlame.Storage;

namespace OpenLoidFlame.Linux.Services.Storage;

public class LinuxExternalStorageService : IExternalStorageService
{
    public bool HasManageExternalStoragePermissionAsync()
    {
        return true;
    }

    public void RequestManageExternalStoragePermission()
    {
        // No action needed on desktop.
    }
}

