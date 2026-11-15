using OpenLoidFlame.Storage;

namespace OpenLoidFlame.MacOS.Services.Storage;

public class MacOSExternalStorageService : IExternalStorageService
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

